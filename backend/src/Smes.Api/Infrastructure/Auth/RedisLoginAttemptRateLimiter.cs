using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace Smes.Api.Infrastructure.Auth;

public enum LoginAttemptRateLimitResult
{
    Allowed,
    Throttled,
    Unavailable
}

public interface ILoginAttemptRateLimiter
{
    ValueTask<LoginAttemptRateLimitResult> CheckAndRecordAsync(
        string tenantCode,
        string username,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared account-scoped login throttling. The atomic Redis script increments a fixed-window
/// counter and ensures the key always expires. Redis failures deny the login attempt.
/// </summary>
public sealed class RedisLoginAttemptRateLimiter : ILoginAttemptRateLimiter
{
    private const int MaximumAttempts = 10;
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(5);
    private const string IncrementScript = """
        local count = redis.call('INCR', KEYS[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if count == 1 or ttl < 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    private readonly IDatabase _database;
    private readonly ILogger<RedisLoginAttemptRateLimiter> _logger;

    public RedisLoginAttemptRateLimiter(
        IConnectionMultiplexer redis,
        ILogger<RedisLoginAttemptRateLimiter> logger)
    {
        _database = redis.GetDatabase();
        _logger = logger;
    }

    public async ValueTask<LoginAttemptRateLimitResult> CheckAndRecordAsync(
        string tenantCode,
        string username,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(tenantCode) || tenantCode.Length > 64 ||
            string.IsNullOrWhiteSpace(username) || username.Length > 128)
        {
            return LoginAttemptRateLimitResult.Throttled;
        }

        var accountKey = $"{tenantCode.Trim().ToUpperInvariant()}\0{username.Trim().ToUpperInvariant()}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountKey)));
        var redisKey = (RedisKey)$"smes:auth:login:attempt:{digest}";

        try
        {
            var result = await _database.ScriptEvaluateAsync(
                IncrementScript,
                [redisKey],
                [(long)AttemptWindow.TotalMilliseconds],
                CommandFlags.None);
            var count = (long)result;
            return count <= MaximumAttempts
                ? LoginAttemptRateLimitResult.Allowed
                : LoginAttemptRateLimitResult.Throttled;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Login attempt counter failed because Redis is unavailable.");
            return LoginAttemptRateLimitResult.Unavailable;
        }
    }
}
