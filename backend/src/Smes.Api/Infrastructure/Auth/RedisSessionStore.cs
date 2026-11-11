using System.Globalization;
using StackExchange.Redis;

namespace Smes.Api.Infrastructure.Auth;

/// <summary>Redis session state; refresh rotation uses a Lua compare-and-swap to reject replay.</summary>
public sealed class RedisSessionStore(IConnectionMultiplexer redis) : ISessionStore
{
    private readonly IDatabase _db = redis.GetDatabase();
    private static RedisKey Key(string sid) => $"smes:auth:session:{sid}";
    private static RedisKey UserKey(long tenantId, long userId) => $"smes:auth:user:{tenantId}:{userId}:sessions";

    public async Task<bool> CreateAsync(AuthSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ttl = session.ExpiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero) return false;
        var value = $"{session.UserId}|{session.TenantId}|{session.RefreshJti}";
        const string script = "if redis.call('SET',KEYS[1],ARGV[1],'EX',ARGV[2],'NX') then " +
            "redis.call('SADD',KEYS[2],ARGV[3]); redis.call('EXPIRE',KEYS[2],ARGV[2]); return 1 else return 0 end";
        var seconds = Math.Max(1, (long)Math.Ceiling(ttl.TotalSeconds));
        var result = await _db.ScriptEvaluateAsync(script,
            [Key(session.SessionId), UserKey(session.TenantId, session.UserId)],
            [value, seconds.ToString(CultureInfo.InvariantCulture), session.SessionId]);
        return (long)result == 1;
    }

    public async Task<bool> RotateRefreshAsync(string sessionId, long userId, long tenantId, string oldJti, string newJti, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ttl = expiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero) return false;
        const string script = "local v=redis.call('GET',KEYS[1]); if not v then return 0 end; " +
            "local u,t,j=string.match(v,'^(%d+)|(%d+)|(.+)$'); " +
            "if u~=ARGV[1] or t~=ARGV[2] or j~=ARGV[3] then return 0 end; " +
            "redis.call('SET',KEYS[1],ARGV[1]..'|'..ARGV[2]..'|'..ARGV[4],'EX',ARGV[5]); return 1";
        var result = await _db.ScriptEvaluateAsync(script,
            [Key(sessionId)],
            [userId.ToString(CultureInfo.InvariantCulture), tenantId.ToString(CultureInfo.InvariantCulture), oldJti, newJti, Math.Max(1, (long)Math.Ceiling(ttl.TotalSeconds)).ToString(CultureInfo.InvariantCulture)]);
        return (long)result == 1;
    }

    public async Task<bool> IsActiveAsync(string sessionId, long userId, long tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await _db.StringGetAsync(Key(sessionId));
        if (!value.HasValue) return false;
        var fields = value.ToString().Split('|');
        return fields.Length == 3 && long.TryParse(fields[0], out var storedUser) && storedUser == userId &&
            long.TryParse(fields[1], out var storedTenant) && storedTenant == tenantId;
    }

    public async Task<bool> RevokeAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _db.KeyDeleteAsync(Key(sessionId));
    }

    public async Task<long> RevokeUserAsync(long userId, long tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userKey = UserKey(tenantId, userId);
        var sessionIds = await _db.SetMembersAsync(userKey);
        if (sessionIds.Length == 0)
        {
            await _db.KeyDeleteAsync(userKey);
            return 0;
        }
        var keys = sessionIds.Select(x => Key(x.ToString())).Append(userKey).ToArray();
        return await _db.KeyDeleteAsync(keys);
    }
}
