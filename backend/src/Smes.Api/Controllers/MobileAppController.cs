using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Domain.Identity;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

/// <summary>
/// Mobile navigation bootstrap. The returned pages are only a client-side menu; API authorization
/// remains enforced by the existing authenticated, tenant-scoped endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("api/mobile")]
public sealed class MobileAppController(SmesDbContext db, CurrentUser me) : ControllerBase
{
    // Only these existing desktop menu entries have a corresponding mobile page. A page is returned
    // only when its exact SysMenu row is directly assigned through this user's SysRoleMenu grants.
    private static readonly IReadOnlyDictionary<string, string> MobilePages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["/production/workorder"] = "work-orders",
        ["/production/report"] = "operation-report",
        ["/production/issue"] = "material-issue",
        ["/quality/inspection"] = "quality-inspection",
        ["/equipment/task"] = "maintenance-tasks",
        ["/equipment/repair"] = "repair-orders"
    };

    private sealed record MobileUser(long Id, string Username, string Nickname);
    private sealed record MobileMenuEntry(string Key, string Title, string Path, string Permission, int Sort);
    private sealed record BootstrapData(MobileUser User, string[] Roles, MobileMenuEntry[] Menus);

    [HttpGet("bootstrap")]
    public async Task<ApiResult> Bootstrap(CancellationToken cancellationToken)
    {
        if (me.Id <= 0 || me.TenantId <= 0)
            return ApiResult.Fail("登录上下文无效", 401);

        var user = await db.Set<SysUser>().AsNoTracking()
            .Where(x => x.Id == me.Id && x.TenantId == me.TenantId && x.Status == 1)
            .Select(x => new MobileUser(x.Id, x.Username, x.Nickname ?? x.Username))
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
            return ApiResult.Fail("用户不存在或已停用", 401);

        // All four tables are explicitly tenant-filtered here in addition to SmesDbContext filters.
        // IsAdmin is deliberately not consulted: even administrators receive only assigned grants.
        var roles = await (from userRole in db.Set<SysUserRole>().AsNoTracking()
                           join role in db.Set<SysRole>().AsNoTracking() on userRole.RoleId equals role.Id
                           where userRole.TenantId == me.TenantId
                                 && userRole.UserId == me.Id
                                 && role.TenantId == me.TenantId
                                 && role.Status == 1
                           orderby role.Sort, role.Code
                           select role.Code)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var grantedMenus = await (from userRole in db.Set<SysUserRole>().AsNoTracking()
                                  join role in db.Set<SysRole>().AsNoTracking() on userRole.RoleId equals role.Id
                                  join roleMenu in db.Set<SysRoleMenu>().AsNoTracking() on role.Id equals roleMenu.RoleId
                                  join menu in db.Set<SysMenu>().AsNoTracking() on roleMenu.MenuId equals menu.Id
                                  where userRole.TenantId == me.TenantId
                                        && userRole.UserId == me.Id
                                        && role.TenantId == me.TenantId
                                        && role.Status == 1
                                        && roleMenu.TenantId == me.TenantId
                                        && menu.TenantId == me.TenantId
                                        && menu.Status == 1
                                        && menu.Visible
                                        && menu.Type == 2
                                  select new { menu.Id, menu.Title, menu.Path, menu.Permission, menu.Sort })
            .Distinct()
            .ToListAsync(cancellationToken);

        var menus = grantedMenus
            .Where(x => x.Path != null && MobilePages.ContainsKey(x.Path))
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .OrderBy(x => x.Sort)
            .ThenBy(x => x.Id)
            .Select(x => new MobileMenuEntry(
                MobilePages[x.Path!], x.Title, x.Path!, x.Permission ?? string.Empty, x.Sort))
            .ToArray();

        return ApiResult.Ok(new BootstrapData(user, roles, menus));
    }
}
