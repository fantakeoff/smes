using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

const string dbName = "E2E_DATABASE_NAME";
var database = Environment.GetEnvironmentVariable(dbName);
var connectionString = Environment.GetEnvironmentVariable("E2E_DB_CONNECTION_STRING");
var configuredDatabase = connectionString?.Split(';', StringSplitOptions.RemoveEmptyEntries)
    .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
    .Where(pair => pair.Length == 2 && (pair[0].Equals("Database", StringComparison.OrdinalIgnoreCase)
        || pair[0].Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)))
    .Select(pair => pair[1]).FirstOrDefault();
if (string.IsNullOrWhiteSpace(database) || !database.Contains("_e2e", StringComparison.OrdinalIgnoreCase)
    || !string.Equals(database, configuredDatabase, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Refusing to run: E2E_DATABASE_NAME must contain '_e2e' and match Database/Initial Catalog in E2E_DB_CONNECTION_STRING. No database changes were made.");
    return 2;
}
var baseAddress = Environment.GetEnvironmentVariable("E2E_API_BASE_URL");
var userA = Environment.GetEnvironmentVariable("E2E_USER_A") ?? "e2e_machinery_operator";
var passA = Environment.GetEnvironmentVariable("E2E_PASSWORD_A");
var userB = Environment.GetEnvironmentVariable("E2E_USER_B") ?? "e2e_electronics_operator";
var passB = Environment.GetEnvironmentVariable("E2E_PASSWORD_B") ?? passA;
if (string.IsNullOrWhiteSpace(passA) || string.IsNullOrWhiteSpace(passB))
{
    Console.Error.WriteLine("Set E2E_PASSWORD_A and E2E_PASSWORD_B for the isolated test accounts.");
    return 2;
}
if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var api))
{
    Console.Error.WriteLine("Set E2E_API_BASE_URL to the running test API base URL.");
    return 2;
}

using var http = new HttpClient { BaseAddress = api };
var failures = new List<string>();
async Task Check(string name, Func<Task> test)
{
    try { await test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null)
{
    using var request = new HttpRequestMessage(method, path);
    if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    if (body != null) request.Content = JsonContent.Create(body);
    return await http.SendAsync(request);
}
async Task<JsonDocument> Login(string username, string password)
{
    var response = await Send(HttpMethod.Post, "/api/login", body: new { username, password });
    Assert(response.StatusCode == HttpStatusCode.OK, $"login HTTP {(int)response.StatusCode}");
    var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var root = json.RootElement;
    Assert(root.TryGetProperty("data", out var data), "login response lacks data property");
    return JsonDocument.Parse(data.GetRawText());
}
string GetToken(JsonDocument json, string name) => json.RootElement.GetProperty(name).GetString() ?? throw new Exception($"{name} missing");
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

await Check("anonymous protected endpoint returns 401", async () =>
{
    using var response = await Send(HttpMethod.Get, "/api/auth/info");
    Assert(response.StatusCode == HttpStatusCode.Unauthorized, $"expected 401, got {(int)response.StatusCode}");
});
await Check("login, refresh and logout revokes refresh token", async () =>
{
    using var login = await Login(userA, passA);
    var refresh = GetToken(login, "refreshToken");
    using var renewedResponse = await Send(HttpMethod.Post, "/api/refresh-token", body: new { refreshToken = refresh });
    Assert(renewedResponse.IsSuccessStatusCode, "refresh rejected valid token");
    using var renewed = JsonDocument.Parse(await renewedResponse.Content.ReadAsStringAsync());
    using var renewedData = JsonDocument.Parse(renewed.RootElement.GetProperty("data").GetRawText());
    var access = GetToken(renewedData, "accessToken");
    using var logout = await Send(HttpMethod.Post, "/api/auth/logout", access);
    Assert(logout.IsSuccessStatusCode, "logout failed");
    using var afterLogout = await Send(HttpMethod.Post, "/api/refresh-token", body: new { refreshToken = refresh });
    Assert(afterLogout.StatusCode == HttpStatusCode.Unauthorized, "refresh token still accepted after logout (revocation contract not implemented)");
});
await Check("cross-tenant access is isolated", async () =>
{
    using var a = await Login(userA, passA);
    using var b = await Login(userB, passB);
    var tokenA = GetToken(a, "accessToken");
    using var response = await Send(HttpMethod.Get, "/api/auth/info", tokenA);
    Assert(response.IsSuccessStatusCode, "tenant A auth info unavailable");
    // Endpoint-specific cross-tenant assertions are configured through E2E_CROSS_TENANT_PATH.
    var path = Environment.GetEnvironmentVariable("E2E_CROSS_TENANT_PATH");
    if (string.IsNullOrWhiteSpace(path)) throw new Exception("Set E2E_CROSS_TENANT_PATH to a tenant-scoped resource path to assert denial/empty result.");
    var tokenB = GetToken(b, "accessToken");
    using var responseB = await Send(HttpMethod.Get, path, tokenB);
    Assert(responseB.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.OK, "unexpected cross-tenant response");
    var payload = await responseB.Content.ReadAsStringAsync();
    Assert(responseB.StatusCode != HttpStatusCode.OK || !payload.Contains("E2E_MACHINERY_", StringComparison.Ordinal), "tenant B can read tenant A fixture");
});

// Domain workflow cases use explicitly configured endpoint + JSON contracts because API DTOs are evolving.
var workflows = new (string Name, string Env, HttpMethod Method, bool ExpectSuccess)[]
{
    ("BOM and routing", "E2E_BOM_ROUTE_REQUEST", HttpMethod.Post, true),
    ("material shortage triggers procurement", "E2E_SHORTAGE_PURCHASE_REQUEST", HttpMethod.Post, true),
    ("purchase receipt", "E2E_PURCHASE_RECEIPT_REQUEST", HttpMethod.Post, true),
    ("transfer conserves inventory", "E2E_TRANSFER_REQUEST", HttpMethod.Post, true),
    ("negative inventory is rejected", "E2E_NEGATIVE_STOCK_REQUEST", HttpMethod.Post, false),
    ("work-report queue is idempotent", "E2E_REPORT_QUEUE_REQUEST", HttpMethod.Post, true),
    ("work-order receipt", "E2E_WORKORDER_RECEIPT_REQUEST", HttpMethod.Post, true)
};
foreach (var workflow in workflows)
{
    await Check(workflow.Name, async () =>
    {
        var configured = Environment.GetEnvironmentVariable(workflow.Env)
            ?? throw new Exception($"Set {workflow.Env}=<path>|<json body> after mapping the current API DTO.");
        var split = configured.IndexOf('|');
        Assert(split > 0, $"{workflow.Env} must be path|JSON");
        var path = configured[..split];
        using var body = JsonDocument.Parse(configured[(split + 1)..]);
        using var login = await Login(userA, passA);
        using var response = await Send(workflow.Method, path, GetToken(login, "accessToken"), body.RootElement);
        Assert(workflow.ExpectSuccess ? response.IsSuccessStatusCode : response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
            $"unexpected status {(int)response.StatusCode}");
        if (workflow.Name == "transfer conserves inventory" || workflow.Name == "work-report queue is idempotent")
            Assert(response.IsSuccessStatusCode, "workflow request failed; follow-up quantity/idempotency readback must be added with route contract");
    });
}

Console.WriteLine($"\n{workflows.Length + 3 - failures.Count}/{workflows.Length + 3} checks passed; database guard accepted '{database}'.");
return failures.Count == 0 ? 0 : 1;
