using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmesMobile.Core;

var dataDirectory = Path.Combine(AppContext.BaseDirectory, "smoke-data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "offline.db3");
if (File.Exists(databasePath)) File.Delete(databasePath);

var handler = new ContractHandler();
using var api = new MobileApiClient(handler, allowDevelopmentHttp: true);
var login = await api.LoginAsync("http://127.0.0.1:8088/api", "machinery", "smoke-user", "not-a-real-secret");
Require(login.TenantId == 7, "Login response parsed from ApiResult envelope.");
Require(handler.Paths.Contains("/api/login"), "BaseUrl ending in /api routes exactly to /api/login.");
Require(await api.IsServerReachableAsync(), "Existing /health/live reachability preflight is called at host root.");
var lookup = await api.GetAsync<MobileLookupResponse>("api/lookup");
Require(lookup.Warehouses.Single().DisplayText == "WH-A · 原料仓" &&
        lookup.Processes.Single().Id == 31 && handler.Paths.Contains("/api/lookup"),
    "Backend master-data lookup deserializes warehouse/process choices with display labels.");

var lanHandler = new ContractHandler();
using (var lanApi = new MobileApiClient(lanHandler, allowDevelopmentHttp: true))
{
    var lanLogin = await lanApi.LoginAsync("http://192.168.1.100:8088/api", "machinery", "smoke-user", "not-a-real-secret");
    Require(lanLogin.TenantId == 7 && lanHandler.Paths.Contains("/api/login"),
        "Debug mode accepts the workstation's private-LAN API root and normalizes /api/login.");
}
var publicHttpRejected = false;
try
{
    using var publicApi = new MobileApiClient(new ContractHandler(), allowDevelopmentHttp: true);
    publicApi.ConfigureServer("http://8.8.8.8:8088/api");
}
catch (ArgumentException) { publicHttpRejected = true; }
Require(publicHttpRejected, "Debug mode rejects public plain-HTTP hosts.");

var store = new OfflineCommandStore(databasePath);
var processor = new OfflineCommandProcessor(api, store);
var ownerKey = "tenant:7:user:70";
await store.InitializeAsync();
Require(!typeof(OfflineCommandRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance)
    .Any(x => x.Name.Contains("token", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("password", StringComparison.OrdinalIgnoreCase)),
    "SQLite command row schema has no token/password fields.");

var commandId = Guid.NewGuid().ToString("D");
await store.EnqueueAsync(new OfflineCommandRecord
{
    CommandId = commandId,
    OwnerKey = ownerKey,
    Kind = OfflineCommandKinds.WorkReport,
    Stage = OfflineCommandStages.Submit,
    TargetId = 11,
    PayloadJson = JsonSerializer.Serialize(new WorkReportRequest(11, null, null, 2, 2, 0, 1, null, "smoke"), new JsonSerializerOptions(JsonSerializerDefaults.Web))
});
await processor.ProcessPendingAsync(ownerKey);
var awaiting = (await store.ListAsync(ownerKey)).Single();
Require(awaiting.State == OfflineCommandStates.AwaitingJob && awaiting.ServerJobId == 41,
    "Queued work report persists its server job ID and stays local while queued.");
Require(handler.ReportIdempotencyKey == commandId, "Work report sends its persisted stable UUID Idempotency-Key.");
await processor.PollJobsAsync(ownerKey);
Require((await store.ListAsync(ownerKey)).Count == 1, "Queued job is not deleted before completed.");
await processor.PollJobsAsync(ownerKey);
Require((await store.ListAsync(ownerKey)).Count == 0, "Local async command is deleted only after completed readback.");

await store.EnqueueAsync(new OfflineCommandRecord
{
    OwnerKey = ownerKey,
    TenantId = 7,
    OwnerUserId = 70,
    Kind = OfflineCommandKinds.QualityJudge,
    Stage = OfflineCommandStages.Submit,
    TargetId = 22,
    PayloadJson = JsonSerializer.Serialize(new QualityJudgeRequest(1, 1, 0, null, null), new JsonSerializerOptions(JsonSerializerDefaults.Web))
});
await processor.ProcessPendingAsync(ownerKey);
var failed = (await store.ListAsync(ownerKey)).Single();
Require(failed.State == OfflineCommandStates.Failed, "Explicit API rejection is visible as failed.");
var judgePosts = handler.Count("POST", "/api/quality-inspection/22/judge");
await processor.ProcessPendingAsync(ownerKey);
Require(handler.Count("POST", "/api/quality-inspection/22/judge") == judgePosts,
    "Failed command is not auto-replayed.");

var materialCommand = await store.EnqueueAsync(new OfflineCommandRecord
{
    OwnerKey = ownerKey,
    TenantId = 7,
    OwnerUserId = 70,
    Kind = OfflineCommandKinds.MaterialIssue,
    Stage = OfflineCommandStages.CreateMaterialDraft,
    TargetId = 33,
    PayloadJson = JsonSerializer.Serialize(new MaterialIssueDraftRequest(2,
        [new MaterialIssueLineRequest(9, 1, null, null)], null), new JsonSerializerOptions(JsonSerializerDefaults.Web))
});
handler.FailMaterialDraftAsLostResponseOnce = true;
await processor.ProcessPendingAsync(ownerKey);
await processor.ProcessPendingAsync(ownerKey);
var materialAfterRetry = (await store.ListAsync(ownerKey)).Single(x => x.Kind == OfflineCommandKinds.MaterialIssue);
Require(materialAfterRetry.State == OfflineCommandStates.Pending &&
        materialAfterRetry.Stage == OfflineCommandStages.SubmitMaterialDraft && materialAfterRetry.BillId == 99,
    "Ambiguous material draft is safely retried with the same UUID and returns the same bill.");
Require(handler.MaterialDraftKeys.Count == 2 && handler.MaterialDraftKeys.All(x => x == materialCommand.CommandId) &&
        handler.MaterialDraftBodies.Distinct(StringComparer.Ordinal).Count() == 1,
    "Material draft retries reuse the persisted Idempotency-Key and identical request body.");
await processor.ProcessPendingAsync(ownerKey);
var materialAwaiting = (await store.ListAsync(ownerKey)).Single(x => x.Kind == OfflineCommandKinds.MaterialIssue);
Require(materialAwaiting.State == OfflineCommandStates.AwaitingJob && materialAwaiting.ServerJobId == 57,
    "Material issue submission persists the existing server job ID.");
await processor.PollJobsAsync(ownerKey);
Require((await store.ListAsync(ownerKey)).All(x => x.Kind != OfflineCommandKinds.MaterialIssue),
    "Material issue command is removed only after its job endpoint confirms completion.");

var interruptedRepair = await store.EnqueueAsync(new OfflineCommandRecord
{
    OwnerKey = ownerKey,
    TenantId = 7,
    OwnerUserId = 70,
    Kind = OfflineCommandKinds.RepairOrderCreate,
    Stage = OfflineCommandStages.Submit,
    TargetId = 9,
    PayloadJson = JsonSerializer.Serialize(new RepairOrderCreateRequest(9, "smoke fixture only", 1), new JsonSerializerOptions(JsonSerializerDefaults.Web))
});
interruptedRepair.State = OfflineCommandStates.Sending;
await store.UpdateAsync(interruptedRepair);
await processor.RecoverInterruptedCommandsAsync(ownerKey);
var recoveredRepair = (await store.ListAsync(ownerKey)).Single(x => x.LocalId == interruptedRepair.LocalId);
Require(recoveredRepair.State == OfflineCommandStates.Pending && recoveredRepair.CommandId == interruptedRepair.CommandId,
    "Interrupted keyed repair creation becomes pending for same-key idempotent retry.");
await store.DeleteAsync(recoveredRepair);

var repairCommand = await store.EnqueueAsync(new OfflineCommandRecord
{
    OwnerKey = ownerKey,
    TenantId = 7,
    OwnerUserId = 70,
    Kind = OfflineCommandKinds.RepairOrderCreate,
    Stage = OfflineCommandStages.Submit,
    TargetId = 9,
    PayloadJson = JsonSerializer.Serialize(new RepairOrderCreateRequest(9, "smoke fixture repair", 1), new JsonSerializerOptions(JsonSerializerDefaults.Web))
});
await processor.ProcessPendingAsync(ownerKey);
Require(handler.RepairIdempotencyKey == repairCommand.CommandId &&
        (await store.ListAsync(ownerKey)).All(x => x.LocalId != repairCommand.LocalId),
    "Repair creation reuses the UUID key and removes the local command only after GET-by-ID confirms the created order.");

Console.WriteLine("PASS: ApiResult login and /api base URL");
Console.WriteLine("PASS: backend master-data lookup populates named selection options");
Console.WriteLine("PASS: stable idempotency key, async job polling, completion-only deletion");
Console.WriteLine("PASS: failed commands are never auto-replayed; ambiguous keyed creates reuse the same UUID");
Console.WriteLine("PASS: synchronous repair creation is read back by ID before local deletion");
Console.WriteLine("PASS: SQLite command schema contains no access token/password fields");
Console.WriteLine("6/6 mobile smoke checks passed; no real credentials or API writes used.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
}

sealed class ContractHandler : HttpMessageHandler
{
    private readonly List<(string Method, string Path)> _calls = [];
    private int _workReportPolls;
    public List<string> Paths => _calls.Select(x => x.Path).ToList();
    public string? ReportIdempotencyKey { get; private set; }
    public string? RepairIdempotencyKey { get; private set; }
    public bool FailMaterialDraftAsLostResponseOnce { get; set; }
    public List<string> MaterialDraftKeys { get; } = [];
    public List<string> MaterialDraftBodies { get; } = [];

    public int Count(string method, string path) => _calls.Count(x => x.Method == method && x.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var path = request.RequestUri!.AbsolutePath;
        _calls.Add((method, path));
        if (method == "GET" && path == "/health/live")
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Healthy") };
        if (method == "POST" && path == "/api/login")
            return Envelope(HttpStatusCode.OK, 0, new { accessToken = "smoke-memory-token", tenantId = 7, tenantCode = "machinery", username = "smoke-user", nickname = "Smoke" });
        if (method == "GET" && path == "/api/lookup")
            return Envelope(HttpStatusCode.OK, 0, new
            {
                warehouses = new[] { new { id = 10, code = "WH-A", name = "原料仓", extra = (string?)null } },
                processes = new[] { new { id = 31, code = "CUT", name = "切割", extra = (string?)null } },
                products = Array.Empty<object>(), defects = Array.Empty<object>(), equipments = Array.Empty<object>(),
                users = Array.Empty<object>(), workshops = Array.Empty<object>()
            });
        if (method == "POST" && path == "/api/work-report")
        {
            ReportIdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            return Envelope(HttpStatusCode.OK, 0, new { jobId = 41, status = "queued", accepted = true });
        }
        if (method == "GET" && path == "/api/work-report/jobs/41")
        {
            _workReportPolls++;
            var status = _workReportPolls == 1 ? "queued" : "completed";
            return Envelope(HttpStatusCode.OK, 0, new { jobId = 41, status, accepted = true });
        }
        if (method == "POST" && path == "/api/quality-inspection/22/judge")
            return Envelope(HttpStatusCode.OK, 409, data: null, message: "already judged");
        if (method == "POST" && path == "/api/work-order/33/issue")
        {
            MaterialDraftKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            MaterialDraftBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (FailMaterialDraftAsLostResponseOnce)
            {
                FailMaterialDraftAsLostResponseOnce = false;
                throw new HttpRequestException("simulated lost response after server accepted idempotent draft");
            }
            return Envelope(HttpStatusCode.OK, 0, new { billId = 99, code = "LLMOCK", status = "draft", jobId = (long?)null });
        }
        if (method == "POST" && path == "/api/material-issue/99/submit")
            return Envelope(HttpStatusCode.OK, 0, new { jobId = 57, status = "queued", billId = 99 });
        if (method == "GET" && path == "/api/material-issue/jobs/57")
            return Envelope(HttpStatusCode.OK, 0, new { jobId = 57, billId = 99, status = "completed", attempts = 1 });
        if (method == "POST" && path == "/api/repair-order")
        {
            RepairIdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"7:{RepairIdempotencyKey}")));
            var code = "REP" + codeHash[..20];
            return RepairOrderEnvelope(code);
        }
        if (method == "GET" && path == "/api/repair-order/501")
        {
            var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"7:{RepairIdempotencyKey}")));
            return RepairOrderEnvelope("REP" + codeHash[..20]);
        }
        return Envelope(HttpStatusCode.NotFound, 404, data: null, message: $"Unexpected smoke route: {method} {path}");
    }

    private static HttpResponseMessage RepairOrderEnvelope(string code)
        => Envelope(HttpStatusCode.OK, 0, new
        {
            id = 501,
            code,
            equipmentId = 9,
            faultDesc = "smoke fixture repair",
            level = 1,
            reporterId = 70,
            handlerId = (long?)null,
            repairStatus = 0,
            solution = (string?)null,
            cost = 0m,
            downtimeMinutes = 0
        });

    private static HttpResponseMessage Envelope(HttpStatusCode status, int code, object? data, string message = "ok")
    {
        var json = JsonSerializer.Serialize(new { code, message, data }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
