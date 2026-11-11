namespace Smes.Api.Infrastructure.Auth;

public sealed record AuthSession(string SessionId, long UserId, long TenantId, string RefreshJti, DateTimeOffset ExpiresAt);

/// <summary>Server-side revocable session store. RotateRefreshAsync must compare-and-swap atomically.</summary>
public interface ISessionStore
{
    Task<bool> CreateAsync(AuthSession session, CancellationToken cancellationToken = default);
    Task<bool> RotateRefreshAsync(string sessionId, long userId, long tenantId, string oldJti, string newJti, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
    Task<bool> IsActiveAsync(string sessionId, long userId, long tenantId, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<long> RevokeUserAsync(long userId, long tenantId, CancellationToken cancellationToken = default);
}
