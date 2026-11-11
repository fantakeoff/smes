using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SmesMobile.Core;

/// <summary>Thin client for the existing SMES API. The bearer token is memory-only.</summary>
public sealed class MobileApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private Uri? _serverOrigin;
    private Uri? _apiBase;
    private string? _accessToken;
    private readonly bool _allowDevelopmentHttp;

    public MobileApiClient(HttpMessageHandler? handler = null, bool allowDevelopmentHttp = false)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = TimeSpan.FromSeconds(20);
        _allowDevelopmentHttp = allowDevelopmentHttp;
    }

    public async Task<MobileLoginResponse> LoginAsync(
        string serverBaseUrl, string tenantCode, string username, string password, CancellationToken cancellationToken = default)
    {
        ConfigureServer(serverBaseUrl);
        _accessToken = null;
        var response = await SendAsync<MobileLoginResponse>(HttpMethod.Post, "api/login",
            new MobileLoginRequest { TenantCode = tenantCode, Username = username, Password = password },
            useAuthorization: false, idempotencyKey: null, cancellationToken);
        if (string.IsNullOrWhiteSpace(response.AccessToken))
            throw new InvalidOperationException("登录响应缺少 accessToken。");
        _accessToken = response.AccessToken;
        return response;
    }

    public void ConfigureServer(string serverBaseUrl)
    {
        if (!Uri.TryCreate(serverBaseUrl.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(parsed.Host))
            throw new ArgumentException("请输入有效的 http/https API 根地址（末尾可带 /api）。", nameof(serverBaseUrl));
        if (parsed.Scheme == "http" && (!_allowDevelopmentHttp || !IsLoopbackOrPrivateAddress(parsed.Host)))
            throw new ArgumentException("Debug 模式下的 HTTP 仅允许 localhost 或私有局域网 IP；正式构建必须使用 HTTPS。", nameof(serverBaseUrl));

        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path.Length > 0 && !path.Equals("/api", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("服务器地址只能是主机根地址或主机根地址加 /api。", nameof(serverBaseUrl));
        _serverOrigin = new Uri(parsed.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
        _apiBase = new Uri(_serverOrigin, "api/");
    }

    private static bool IsLoopbackOrPrivateAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var address))
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        return bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC; // IPv6 unique-local (fc00::/7)
    }

    /// <summary>Existing anonymous API health endpoint; used to leave offline commands pending without sending them while disconnected.</summary>
    public async Task<bool> IsServerReachableAsync(CancellationToken cancellationToken = default)
    {
        if (_serverOrigin is null) return false;
        try
        {
            using var response = await _http.GetAsync(new Uri(_serverOrigin, "health/live"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    public Task<T> GetAsync<T>(string relativePath, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Get, relativePath, body: null, useAuthorization: true,
            idempotencyKey: null, cancellationToken);

    public Task<T> PostAsync<T>(string relativePath, object body, string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Post, relativePath, body, useAuthorization: true,
            idempotencyKey, cancellationToken);

    public Task PostEmptyAsync(string relativePath, CancellationToken cancellationToken = default)
        => SendEmptyAsync(relativePath, cancellationToken);

    public void ClearSession() => _accessToken = null;

    private async Task SendEmptyAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, relativePath, body: null, useAuthorization: true, idempotencyKey: null);
        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        CheckResponse(response.StatusCode, json);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativePath, object? body,
        bool useAuthorization, string? idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, relativePath, body, useAuthorization, idempotencyKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        CheckHttpStatus(response.StatusCode, json);

        ApiEnvelope<T>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ApiEnvelope<T>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new MobileApiException("服务器返回了无法解析的响应。", (int)response.StatusCode, null, ex);
        }

        if (envelope is null)
            throw new MobileApiException("服务器返回了空响应。", (int)response.StatusCode, null);
        if (envelope.Code != 0)
            throw new MobileApiException(string.IsNullOrWhiteSpace(envelope.Message) ? "API 请求失败。" : envelope.Message,
                (int)response.StatusCode, envelope.Code);
        if (envelope.Data is null)
            return default!; // Existing synchronous APIs intentionally return ApiResult.Ok(null).
        return envelope.Data;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, object? body,
        bool useAuthorization, string? idempotencyKey)
    {
        if (_apiBase is null) throw new InvalidOperationException("请先配置服务器地址并登录。");
        var normalizedPath = relativePath.TrimStart('/');
        if (normalizedPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            normalizedPath = normalizedPath[4..];
        var request = new HttpRequestMessage(method, new Uri(_apiBase, normalizedPath));
        if (useAuthorization)
        {
            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                request.Dispose();
                throw new InvalidOperationException("当前没有内存中的登录令牌，请重新登录。");
            }
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    private static void CheckResponse(HttpStatusCode statusCode, string json)
    {
        CheckHttpStatus(statusCode, json);
        try
        {
            var envelope = JsonSerializer.Deserialize<ApiEnvelope<JsonElement>>(json, JsonOptions);
            if (envelope is null || envelope.Code != 0)
                throw new MobileApiException(envelope?.Message ?? "API 请求失败。", (int)statusCode, envelope?.Code);
        }
        catch (JsonException ex)
        {
            throw new MobileApiException("服务器返回了无法解析的响应。", (int)statusCode, null, ex);
        }
    }

    private static void CheckHttpStatus(HttpStatusCode statusCode, string json)
    {
        if ((int)statusCode is >= 200 and < 300) return;
        string message = $"HTTP {(int)statusCode} 请求失败。";
        try
        {
            var envelope = JsonSerializer.Deserialize<ApiEnvelope<JsonElement>>(json, JsonOptions);
            if (!string.IsNullOrWhiteSpace(envelope?.Message)) message = envelope.Message;
        }
        catch (JsonException) { }
        throw new MobileApiException(message, (int)statusCode, null);
    }

    public void Dispose() => _http.Dispose();
}

public sealed class MobileApiException(string message, int httpStatusCode, int? apiCode, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int HttpStatusCode { get; } = httpStatusCode;
    public int? ApiCode { get; } = apiCode;
}
