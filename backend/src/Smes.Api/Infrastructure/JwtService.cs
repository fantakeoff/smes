using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Smes.Domain.Identity;
using Smes.Api.Infrastructure.Auth;

namespace Smes.Api.Infrastructure;

public class JwtOptions
{
    public string Issuer { get; set; } = "smes";
    public string Audience { get; set; } = "smes-web";
    public string Secret { get; set; } = "";
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 7;
}

public sealed record IssuedToken(string Token, string Jti, DateTime Expires);

public class JwtService
{
    private readonly JwtOptions _options;
    public JwtService(JwtOptions options) => _options = options;
    public TimeSpan RefreshLifetime => TimeSpan.FromDays(_options.RefreshDays);

    public IssuedToken CreateAccessToken(SysUser user, long tenantId, string tenantCode, string sessionId,
        IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.AccessMinutes);
        var jti = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("nickname", user.Nickname ?? user.Username),
            new("isAdmin", user.IsAdmin ? "1" : "0"),
            new("tenantId", tenantId.ToString()),
            new("tenantCode", tenantCode),
            new("sid", sessionId),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));
        return new IssuedToken(Sign(claims, expires), jti, expires);
    }

    public IssuedToken CreateRefreshToken(long userId, long tenantId, string tenantCode, string sessionId, string jti, DateTime expires)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("type", "refresh"),
            new("tenantId", tenantId.ToString()),
            new("tenantCode", tenantCode),
            new("sid", sessionId),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        return new IssuedToken(Sign(claims, expires), jti, expires);
    }

    public string CreateRefreshJti() => Guid.NewGuid().ToString("N");
    public DateTime CreateRefreshExpiry() => DateTime.UtcNow.Add(RefreshLifetime);

    private string Sign(IEnumerable<Claim> claims, DateTime expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims,
            notBefore: DateTime.UtcNow, expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Call from JwtBearer OnTokenValidated; reject refresh tokens and revoked sessions.</summary>
    public async Task<bool> IsActiveAccessSessionAsync(ClaimsPrincipal principal, ISessionStore sessions, CancellationToken cancellationToken = default)
    {
        if (principal.FindFirst("type") is not null) return false;
        var sid = principal.FindFirst("sid")?.Value;
        var userText = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var tenantText = principal.FindFirst("tenantId")?.Value;
        return !string.IsNullOrWhiteSpace(sid) && long.TryParse(userText, out var userId) &&
            long.TryParse(tenantText, out var tenantId) &&
            await sessions.IsActiveAsync(sid, userId, tenantId, cancellationToken);
    }

    public ClaimsPrincipal? Validate(string token, bool validateLifetime = true)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true,
                ValidateLifetime = validateLifetime, ValidIssuer = _options.Issuer, ValidAudience = _options.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)),
                ClockSkew = TimeSpan.Zero
            }, out _);
        }
        catch { return null; }
    }
}
