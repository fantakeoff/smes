using System.Security.Cryptography;
using System.Text;
using Altcha.AspNetCore;
using StackExchange.Redis;

namespace Smes.Api.Infrastructure.Auth;

/// <summary>
/// Cross-instance ALTCHA replay protection. SET NX with an expiry is one atomic Redis operation.
/// Redis errors deliberately reject the CAPTCHA claim so login never falls back open.
/// </summary>
public sealed class RedisAltchaReplayStore : IAltchaReplayStore
{
    private static readonly TimeSpan MaximumReplayLifetime = TimeSpan.FromMinutes(5);
    private readonly IDatabase _database;
    private readonly ILogger<RedisAltchaReplayStore> _logger;

    public RedisAltchaReplayStore(
        IConnectionMultiplexer redis,
        ILogger<RedisAltchaReplayStore> logger)
    {
        _database = redis.GetDatabase();
        _logger = logger;
    }

    public async ValueTask<bool> TryClaimAsync(
        string key,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256)
            return false;

        var lifetime = expiresAt - DateTimeOffset.UtcNow;
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumReplayLifetime)
            return false;

        var replayId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var redisKey = (RedisKey)$"smes:auth:altcha:replay:{replayId}";
        try
        {
            return await _database.StringSetAsync(
                redisKey,
                "1",
                lifetime,
                When.NotExists,
                CommandFlags.None);
        }
        catch (RedisException exception)
        {
            _logger.LogError(exception, "ALTCHA replay claim failed because Redis is unavailable.");
            return false;
        }
    }
}
