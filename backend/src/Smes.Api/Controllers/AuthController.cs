using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Altcha.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Api.Infrastructure.Auth;
using Smes.Domain.Identity;
using Smes.Infrastructure;
using Smes.Infrastructure.Tenancy;

namespace Smes.Api.Controllers;

[ApiController]
[Route("api")]
public class AuthController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly JwtService _jwt;
    private readonly ISessionStore _sessions;
    private readonly ITenantContext _tenant;
    private readonly IWebHostEnvironment _environment;
    private readonly ILoginAttemptRateLimiter _loginAttemptRateLimiter;

    public AuthController(
        SmesDbContext db,
        JwtService jwt,
        ISessionStore sessions,
        ITenantContext tenant,
        IWebHostEnvironment environment,
        ILoginAttemptRateLimiter loginAttemptRateLimiter)
    {
        _db = db;
        _jwt = jwt;
        _sessions = sessions;
        _tenant = tenant;
        _environment = environment;
        _loginAttemptRateLimiter = loginAttemptRateLimiter;
    }

    public sealed class LoginRequest : IAltchaPayloadCarrier
    {
        [Required, StringLength(128, MinimumLength = 1)]
        public string Username { get; init; } = "";

        [Required, StringLength(256, MinimumLength = 1)]
        public string Password { get; init; } = "";

        [StringLength(64)]
        public string TenantCode { get; init; } = "machinery";

        [JsonPropertyName("altcha"), StringLength(8192, MinimumLength = 1)]
        public string? AltchaPayload { get; init; }
    }
    public record RefreshRequest(string? RefreshToken = null);

    [HttpPost("login")]
    [AllowAnonymous]
    [RequestSizeLimit(16 * 1024)]
    [AltchaVerify(RejectOnFailure = true)]
    public async Task<ApiResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return ApiResult.Fail("请输入账号和密码");
        }
        var tenantCode = string.IsNullOrWhiteSpace(request.TenantCode) ? "machinery" : request.TenantCode.Trim();
        var attemptLimit = await _loginAttemptRateLimiter.CheckAndRecordAsync(
            tenantCode, request.Username, HttpContext.RequestAborted);
        if (attemptLimit == LoginAttemptRateLimitResult.Throttled)
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return ApiResult.Fail("登录尝试过于频繁，请稍后再试", StatusCodes.Status429TooManyRequests);
        }
        if (attemptLimit == LoginAttemptRateLimitResult.Unavailable)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return ApiResult.Fail("登录服务暂不可用，请稍后重试", StatusCodes.Status503ServiceUnavailable);
        }

        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == tenantCode && x.Status == 1);
        if (tenant is null) return ApiResult.Fail("租户不存在或已停用");
        _tenant.TenantId = tenant.Id;
        _tenant.TenantCode = tenant.Code;
        var user = await _db.Set<SysUser>().FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Username == request.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return ApiResult.Fail("账号或密码错误");
        }
        if (user.Status != 1) return ApiResult.Fail("账号已被停用");

        var (roles, permissions) = await LoadRolesAndPermissionsAsync(user);
        var sid = Guid.NewGuid().ToString("N");
        var refreshJti = _jwt.CreateRefreshJti();
        var refreshExpires = _jwt.CreateRefreshExpiry();
        if (!await _sessions.CreateAsync(new AuthSession(sid, user.Id, tenant.Id, refreshJti, refreshExpires)))
            return ApiResult.Fail("无法创建登录会话", 500);
        var access = _jwt.CreateAccessToken(user, tenant.Id, tenant.Code, sid, roles, permissions);
        var refresh = _jwt.CreateRefreshToken(user.Id, tenant.Id, tenant.Code, sid, refreshJti, refreshExpires);
        SetRefreshCookie(refresh.Token, refreshExpires);
        user.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return ApiResult.Ok(new
        {
            accessToken = access.Token,
            expires = access.Expires.ToString("yyyy/MM/dd HH:mm:ss"),
            tenantId = tenant.Id,
            tenantCode = tenant.Code,
            username = user.Username,
            nickname = user.Nickname ?? user.Username,
            avatar = user.Avatar ?? "",
            roles,
            permissions
        });
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<ApiResult> Refresh([FromBody] RefreshRequest? request)
    {
        var refreshToken = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken)) Request.Cookies.TryGetValue("smes_refresh", out refreshToken);
        var principal = _jwt.Validate(refreshToken ?? "", true);
        if (principal?.FindFirst("type")?.Value != "refresh") return ApiResult.Fail("刷新令牌无效", 401);
        var idText = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        if (!long.TryParse(idText, out var userId)) return ApiResult.Fail("刷新令牌无效", 401);
        if (!long.TryParse(principal.FindFirst("tenantId")?.Value, out var tenantId)) return ApiResult.Fail("刷新令牌无效", 401);
        var tenantCode = principal.FindFirst("tenantCode")?.Value;
        var sid = principal.FindFirst("sid")?.Value;
        var oldJti = principal.FindFirst("jti")?.Value;
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(oldJti) || string.IsNullOrWhiteSpace(tenantCode)) return ApiResult.Fail("刷新令牌无效", 401);
        _tenant.TenantId = tenantId;
        _tenant.TenantCode = tenantCode;
        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId && x.Code == tenantCode && x.Status == 1);
        if (tenant is null) return ApiResult.Fail("租户不存在或已停用", 401);
        var user = await _db.Set<SysUser>().FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantId);
        if (user == null || user.Status != 1) return ApiResult.Fail("用户不存在或已停用", 401);
        var newJti = _jwt.CreateRefreshJti();
        var refreshExpires = _jwt.CreateRefreshExpiry();
        if (!await _sessions.RotateRefreshAsync(sid, userId, tenantId, oldJti, newJti, refreshExpires))
            return ApiResult.Fail("刷新令牌已失效或已使用", 401);
        var (roles, permissions) = await LoadRolesAndPermissionsAsync(user);
        var access = _jwt.CreateAccessToken(user, tenantId, tenantCode, sid, roles, permissions);
        var refresh = _jwt.CreateRefreshToken(userId, tenantId, tenantCode, sid, newJti, refreshExpires);
        SetRefreshCookie(refresh.Token, refreshExpires);
        return ApiResult.Ok(new
        {
            accessToken = access.Token,
            expires = access.Expires.ToString("yyyy/MM/dd HH:mm:ss"),
            tenantId,
            tenantCode
        });
    }

    [HttpGet("get-async-routes")]
    [Authorize]
    public async Task<ApiResult> GetAsyncRoutes()
    {
        var user = await CurrentUserAsync();
        if (user == null) return ApiResult.Fail("未登录", 401);

        var allMenus = await _db.Set<SysMenu>().AsNoTracking()
            .Where(x => x.Visible && x.Status == 1)
            .OrderBy(x => x.Sort).ToListAsync();

        HashSet<long> allowed;
        if (user.IsAdmin)
        {
            allowed = allMenus.Select(x => x.Id).ToHashSet();
        }
        else
        {
            var roleIds = await _db.Set<SysUserRole>().Where(x => x.UserId == user.Id).Select(x => x.RoleId).ToListAsync();
            allowed = (await _db.Set<SysRoleMenu>().Where(x => roleIds.Contains(x.RoleId)).Select(x => x.MenuId).ToListAsync()).ToHashSet();
            var byId = allMenus.ToDictionary(x => x.Id);
            foreach (var id in allowed.ToList())
            {
                var cur = byId.GetValueOrDefault(id);
                while (cur != null && cur.ParentId != 0)
                {
                    allowed.Add(cur.ParentId);
                    cur = byId.GetValueOrDefault(cur.ParentId);
                }
            }
        }

        return ApiResult.Ok(BuildRoutes(allMenus, allowed, 0));
    }

    [HttpGet("auth/info")]
    [Authorize]
    public async Task<ApiResult> Info()
    {
        var user = await CurrentUserAsync();
        if (user == null) return ApiResult.Fail("未登录", 401);
        var (roles, permissions) = await LoadRolesAndPermissionsAsync(user);
        return ApiResult.Ok(new
        {
            username = user.Username,
            nickname = user.Nickname ?? user.Username,
            avatar = user.Avatar ?? "",
            email = user.Email ?? "",
            phone = user.Phone ?? "",
            description = "SMEs 智能制造运营平台",
            roles,
            permissions
        });
    }

    [HttpPost("auth/logout")]
    [Authorize]
    public async Task<ApiResult> Logout()
    {
        var sid = User.FindFirst("sid")?.Value;
        if (!string.IsNullOrWhiteSpace(sid)) await _sessions.RevokeAsync(sid);
        Response.Cookies.Delete("smes_refresh", new CookieOptions { Path = "/api" });
        return ApiResult.Ok(null, "已退出登录");
    }

    private void SetRefreshCookie(string token, DateTime expires)
    {
        Response.Cookies.Append("smes_refresh", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps || _environment.IsProduction(),
            SameSite = SameSiteMode.Strict,
            Path = "/api",
            Expires = new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc))
        });
    }

    private async Task<SysUser?> CurrentUserAsync()
    {
        var idText = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(idText, out var id)
            ? await _db.Set<SysUser>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            : null;
    }

    private async Task<(List<string> Roles, List<string> Permissions)> LoadRolesAndPermissionsAsync(SysUser user)
    {
        var roleIds = await _db.Set<SysUserRole>().Where(x => x.UserId == user.Id).Select(x => x.RoleId).ToListAsync();
        var roles = await _db.Set<SysRole>().Where(x => roleIds.Contains(x.Id)).Select(x => x.Code).ToListAsync();
        var menus = await _db.Set<SysMenu>().AsNoTracking().Where(m => m.Permission != null && m.Permission != "").ToListAsync();
        List<string> permissions;
        if (user.IsAdmin)
        {
            permissions = menus.Select(x => x.Permission!).Distinct().ToList();
            if (!roles.Contains("admin")) roles.Add("admin");
        }
        else
        {
            var menuIds = await _db.Set<SysRoleMenu>().Where(x => roleIds.Contains(x.RoleId)).Select(x => x.MenuId).ToListAsync();
            permissions = menus.Where(x => menuIds.Contains(x.Id)).Select(x => x.Permission!).Distinct().ToList();
        }
        return (roles, permissions);
    }

    private static List<Dictionary<string, object?>> BuildRoutes(List<SysMenu> all, HashSet<long> allowed, long parentId)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var menu in all.Where(x => x.ParentId == parentId && x.Type != 3).OrderBy(x => x.Sort))
        {
            if (!allowed.Contains(menu.Id)) continue;
            var children = BuildRoutes(all, allowed, menu.Id);

            if (menu.Type == 2 || menu.Component != null)
            {
                var buttons = all.Where(x => x.ParentId == menu.Id && x.Type == 3)
                    .Select(x => x.Permission).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                result.Add(new Dictionary<string, object?>
                {
                    ["path"] = menu.Path,
                    ["name"] = RouteName(menu.Path ?? menu.Title),
                    ["component"] = menu.Component,
                    ["meta"] = new
                    {
                        title = menu.Title,
                        icon = menu.Icon,
                        keepAlive = menu.KeepAlive,
                        showLink = true,
                        rank = menu.Sort,
                        auths = buttons
                    }
                });
            }
            else
            {
                if (children.Count == 0) continue;
                result.Add(new Dictionary<string, object?>
                {
                    ["path"] = menu.Path,
                    ["name"] = "Dir" + menu.Id,
                    ["component"] = null,
                    ["redirect"] = children[0].GetValueOrDefault("path"),
                    ["meta"] = new
                    {
                        title = menu.Title,
                        icon = menu.Icon,
                        showLink = true,
                        rank = menu.Sort
                    },
                    ["children"] = children
                });
            }
        }
        return result;
    }

    private static string RouteName(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
