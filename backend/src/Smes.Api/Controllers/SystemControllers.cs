using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Api.Infrastructure.Auth;
using Smes.Domain.Common;
using Smes.Domain.Identity;
using Smes.Domain.MasterData;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

public record SaveUserRequest(
    long Id, string Username, string? Nickname, string? Password, string? Phone, string? Email,
    long? DeptId, int Status, List<long>? RoleIds, string? Remark);

[ApiController]
[Route("api/user")]
[Authorize(Policy = "AdminOnly")]
public class UserController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;
    private readonly ISessionStore _sessions;

    public UserController(SmesDbContext db, CurrentUser me, ISessionStore sessions)
    {
        _db = db;
        _me = me;
        _sessions = sessions;
    }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] long? deptId)
    {
        var query = _db.Set<SysUser>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword))
        {
            query = query.Where(x => x.Username.Contains(page.Keyword) || x.Nickname!.Contains(page.Keyword) || x.Phone!.Contains(page.Keyword));
        }
        if (page.Status.HasValue) query = query.Where(x => x.Status == page.Status);
        if (deptId.HasValue) query = query.Where(x => x.DeptId == deptId);
        var total = await query.CountAsync();
        var users = await query.OrderBy(x => x.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        var ids = users.Select(x => x.Id).ToList();
        var roleMap = await (from ur in _db.Set<SysUserRole>()
                             join r in _db.Set<SysRole>() on ur.RoleId equals r.Id
                             where ids.Contains(ur.UserId)
                             select new { ur.UserId, RoleId = r.Id, RoleName = r.Name }).ToListAsync();
        var list = users.Select(u => new
        {
            u.Id, u.Username, u.Nickname, u.Phone, u.Email, u.DeptId, u.Status, u.Remark, u.LastLoginAt, u.CreatedAt,
            roleIds = roleMap.Where(x => x.UserId == u.Id).Select(x => x.RoleId).ToList(),
            roleNames = roleMap.Where(x => x.UserId == u.Id).Select(x => x.RoleName).ToList()
        }).ToList();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("options")]
    public async Task<ApiResult> Options([FromQuery] string? keyword)
    {
        var query = _db.Set<SysUser>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword)) query = query.Where(x => x.Username.Contains(keyword) || x.Nickname!.Contains(keyword));
        var list = await query.OrderBy(x => x.Id).Select(x => new OptionItem { Id = x.Id, Code = x.Username, Name = x.Nickname }).ToListAsync();
        return ApiResult.Ok(list);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] SaveUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return ApiResult.Fail("账号和密码不能为空");
        if (request.Password.Length < 12 || !request.Password.Any(char.IsUpper) ||
            !request.Password.Any(char.IsLower) || !request.Password.Any(char.IsDigit))
            return ApiResult.Fail("密码至少 12 位，并包含大写字母、小写字母和数字");
        if (await _db.Set<SysUser>().AnyAsync(x => x.Username == request.Username))
            return ApiResult.Fail("账号已存在");
        var user = new SysUser
        {
            Username = request.Username, Nickname = request.Nickname, Phone = request.Phone, Email = request.Email,
            DeptId = request.DeptId, Status = request.Status, Remark = request.Remark,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password), CreatedBy = _me.Id
        };
        _db.Add(user);
        await _db.SaveChangesAsync();
        if (request.RoleIds != null)
        {
            _db.AddRange(request.RoleIds.Select(rid => new SysUserRole { UserId = user.Id, RoleId = rid }));
            await _db.SaveChangesAsync();
        }
        return ApiResult.Ok(user, "新增成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] SaveUserRequest request)
    {
        var user = await _db.Set<SysUser>().FirstOrDefaultAsync(x => x.Id == id);
        if (user == null) return ApiResult.Fail("用户不存在", 404);
        user.Nickname = request.Nickname;
        user.Phone = request.Phone;
        user.Email = request.Email;
        user.DeptId = request.DeptId;
        user.Status = request.Status;
        user.Remark = request.Remark;
        user.UpdatedAt = DateTime.Now;
        user.UpdatedBy = _me.Id;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password.Length < 12 || !request.Password.Any(char.IsUpper) ||
                !request.Password.Any(char.IsLower) || !request.Password.Any(char.IsDigit))
                return ApiResult.Fail("密码至少 12 位，并包含大写字母、小写字母和数字");
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }
        var oldRoles = await _db.Set<SysUserRole>().Where(x => x.UserId == id).ToListAsync();
        _db.RemoveRange(oldRoles);
        if (request.RoleIds != null)
        {
            _db.AddRange(request.RoleIds.Distinct().Select(rid => new SysUserRole { UserId = id, RoleId = rid }));
        }
        await _db.SaveChangesAsync();
        await _sessions.RevokeUserAsync(user.Id, _me.TenantId);
        return ApiResult.Ok(null, "修改成功，原登录会话已撤销");
    }

    [HttpPost("{id:long}/reset-password")]
    public async Task<ApiResult> ResetPassword(long id, [FromBody] Dictionary<string, string> body)
    {
        var user = await _db.Set<SysUser>().FirstOrDefaultAsync(x => x.Id == id);
        if (user == null) return ApiResult.Fail("用户不存在", 404);
        var password = body.GetValueOrDefault("password");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            return ApiResult.Fail("新密码至少 12 位，并包含大写字母、小写字母和数字");
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        await _db.SaveChangesAsync();
        await _sessions.RevokeUserAsync(user.Id, _me.TenantId);
        return ApiResult.Ok(null, "密码已重置，原登录会话已撤销");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        var user = await _db.Set<SysUser>().FirstOrDefaultAsync(x => x.Id == id);
        if (user == null) return ApiResult.Fail("用户不存在", 404);
        if (user.IsAdmin) return ApiResult.Fail("不能删除管理员账号");
        user.IsDeleted = true;
        await _db.SaveChangesAsync();
        await _sessions.RevokeUserAsync(user.Id, _me.TenantId);
        return ApiResult.Ok(null, "删除成功，原登录会话已撤销");
    }
}

public record SaveRoleRequest(long Id, string Name, string Code, int Sort, int DataScope, int Status, string? Remark, List<long>? MenuIds);

[ApiController]
[Route("api/role")]
[Authorize(Policy = "AdminOnly")]
public class RoleController : ControllerBase
{
    private readonly SmesDbContext _db;
    public RoleController(SmesDbContext db) => _db = db;

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page)
    {
        var query = _db.Set<SysRole>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.Name.Contains(page.Keyword) || x.Code.Contains(page.Keyword));
        var total = await query.CountAsync();
        var list = await query.OrderBy(x => x.Sort).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var role = await _db.Set<SysRole>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (role == null) return ApiResult.Fail("角色不存在", 404);
        var menuIds = await _db.Set<SysRoleMenu>().Where(x => x.RoleId == id).Select(x => x.MenuId).ToListAsync();
        return ApiResult.Ok(new { role.Id, role.Name, role.Code, role.Sort, role.DataScope, role.Status, role.Remark, menuIds });
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] SaveRoleRequest request)
    {
        var role = new SysRole { Name = request.Name, Code = request.Code, Sort = request.Sort, DataScope = request.DataScope, Status = request.Status, Remark = request.Remark };
        _db.Add(role);
        await _db.SaveChangesAsync();
        if (request.MenuIds != null)
        {
            _db.AddRange(request.MenuIds.Distinct().Select(mid => new SysRoleMenu { RoleId = role.Id, MenuId = mid }));
            await _db.SaveChangesAsync();
        }
        return ApiResult.Ok(role, "新增成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] SaveRoleRequest request)
    {
        var role = await _db.Set<SysRole>().FirstOrDefaultAsync(x => x.Id == id);
        if (role == null) return ApiResult.Fail("角色不存在", 404);
        role.Name = request.Name;
        role.Code = request.Code;
        role.Sort = request.Sort;
        role.DataScope = request.DataScope;
        role.Status = request.Status;
        role.Remark = request.Remark;
        _db.RemoveRange(await _db.Set<SysRoleMenu>().Where(x => x.RoleId == id).ToListAsync());
        if (request.MenuIds != null)
        {
            _db.AddRange(request.MenuIds.Distinct().Select(mid => new SysRoleMenu { RoleId = id, MenuId = mid }));
        }
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        var role = await _db.Set<SysRole>().FirstOrDefaultAsync(x => x.Id == id);
        if (role == null) return ApiResult.Fail("角色不存在", 404);
        if (role.Code == "admin") return ApiResult.Fail("不能删除超级管理员角色");
        role.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}

[ApiController]
[Route("api/menu")]
[Authorize(Policy = "AdminOnly")]
public class MenuController : ControllerBase
{
    private readonly SmesDbContext _db;
    public MenuController(SmesDbContext db) => _db = db;

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] string? keyword)
    {
        var query = _db.Set<SysMenu>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword)) query = query.Where(x => x.Title.Contains(keyword) || x.Path!.Contains(keyword));
        var list = await query.OrderBy(x => x.ParentId).ThenBy(x => x.Sort).ToListAsync();
        return ApiResult.Ok(list);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] SysMenu menu)
    {
        menu.Id = 0;
        menu.CreatedAt = DateTime.Now;
        _db.Add(menu);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(menu, "新增成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] SysMenu menu)
    {
        var existing = await _db.Set<SysMenu>().FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("菜单不存在", 404);
        existing.ParentId = menu.ParentId;
        existing.Title = menu.Title;
        existing.Path = menu.Path;
        existing.Component = menu.Component;
        existing.Icon = menu.Icon;
        existing.Type = menu.Type;
        existing.Permission = menu.Permission;
        existing.Sort = menu.Sort;
        existing.Visible = menu.Visible;
        existing.KeepAlive = menu.KeepAlive;
        existing.Status = menu.Status;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        if (await _db.Set<SysMenu>().AnyAsync(x => x.ParentId == id)) return ApiResult.Fail("请先删除子菜单");
        var menu = await _db.Set<SysMenu>().FirstOrDefaultAsync(x => x.Id == id);
        if (menu == null) return ApiResult.Fail("菜单不存在", 404);
        menu.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}

[ApiController]
[Route("api/dept")]
[Authorize(Policy = "AdminOnly")]
public class DeptController : ControllerBase
{
    private readonly SmesDbContext _db;
    public DeptController(SmesDbContext db) => _db = db;

    [HttpGet]
    public async Task<ApiResult> List()
    {
        var list = await _db.Set<SysDept>().AsNoTracking().OrderBy(x => x.ParentId).ThenBy(x => x.Sort).ToListAsync();
        return ApiResult.Ok(list);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] SysDept dept)
    {
        dept.Id = 0;
        dept.CreatedAt = DateTime.Now;
        _db.Add(dept);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(dept, "新增成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] SysDept dept)
    {
        var existing = await _db.Set<SysDept>().FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("部门不存在", 404);
        existing.ParentId = dept.ParentId;
        existing.Name = dept.Name;
        existing.Code = dept.Code;
        existing.Leader = dept.Leader;
        existing.Phone = dept.Phone;
        existing.Sort = dept.Sort;
        existing.Status = dept.Status;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        if (await _db.Set<SysDept>().AnyAsync(x => x.ParentId == id)) return ApiResult.Fail("请先删除下级部门");
        var dept = await _db.Set<SysDept>().FirstOrDefaultAsync(x => x.Id == id);
        if (dept == null) return ApiResult.Fail("部门不存在", 404);
        dept.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}

[ApiController]
[Route("api/dict")]
[Authorize(Policy = "AdminOnly")]
public class DictController : ControllerBase
{
    private readonly SmesDbContext _db;
    public DictController(SmesDbContext db) => _db = db;

    [HttpGet("type")]
    public async Task<ApiResult> Types()
    {
        var list = await _db.Set<SysDictType>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        return ApiResult.Ok(list);
    }

    [HttpPost("type")]
    public async Task<ApiResult> CreateType([FromBody] SysDictType type)
    {
        type.Id = 0;
        _db.Add(type);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(type, "新增成功");
    }

    [HttpPut("type/{id:long}")]
    public async Task<ApiResult> UpdateType(long id, [FromBody] SysDictType type)
    {
        var existing = await _db.Set<SysDictType>().FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("字典类型不存在", 404);
        existing.Name = type.Name;
        existing.Code = type.Code;
        existing.Description = type.Description;
        existing.Status = type.Status;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    [HttpDelete("type/{id:long}")]
    public async Task<ApiResult> DeleteType(long id)
    {
        var type = await _db.Set<SysDictType>().FirstOrDefaultAsync(x => x.Id == id);
        if (type == null) return ApiResult.Fail("字典类型不存在", 404);
        type.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }

    [HttpGet("item")]
    public async Task<ApiResult> Items([FromQuery] long dictTypeId)
    {
        var list = await _db.Set<SysDictItem>().AsNoTracking().Where(x => x.DictTypeId == dictTypeId).OrderBy(x => x.Sort).ToListAsync();
        return ApiResult.Ok(list);
    }

    [HttpPost("item")]
    public async Task<ApiResult> CreateItem([FromBody] SysDictItem item)
    {
        item.Id = 0;
        _db.Add(item);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(item, "新增成功");
    }

    [HttpPut("item/{id:long}")]
    public async Task<ApiResult> UpdateItem(long id, [FromBody] SysDictItem item)
    {
        var existing = await _db.Set<SysDictItem>().FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("字典项不存在", 404);
        existing.Label = item.Label;
        existing.Value = item.Value;
        existing.Sort = item.Sort;
        existing.TagType = item.TagType;
        existing.Status = item.Status;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    [HttpDelete("item/{id:long}")]
    public async Task<ApiResult> DeleteItem(long id)
    {
        var item = await _db.Set<SysDictItem>().FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return ApiResult.Fail("字典项不存在", 404);
        item.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }

    /// <summary>前端启动时一次性拉取全部字典</summary>
    [HttpGet("all")]
    public async Task<ApiResult> All()
    {
        var types = await _db.Set<SysDictType>().AsNoTracking().ToListAsync();
        var items = await _db.Set<SysDictItem>().AsNoTracking().OrderBy(x => x.Sort).ToListAsync();
        return ApiResult.Ok(types.ToDictionary(t => t.Code,
            t => items.Where(i => i.DictTypeId == t.Id).Select(i => new { label = i.Label, value = i.Value, tagType = i.TagType }).ToList()));
    }
}

[ApiController]
[Route("api/config")]
[Authorize(Policy = "AdminOnly")]
public class ConfigController : CrudController<SysConfig>
{
    public ConfigController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/log")]
[Authorize(Policy = "AdminOnly")]
public class LogController : ControllerBase
{
    private readonly SmesDbContext _db;
    public LogController(SmesDbContext db) => _db = db;

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page)
    {
        var query = _db.Set<SysLog>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.Username!.Contains(page.Keyword) || x.Module!.Contains(page.Keyword));
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }
}

/// <summary>字典/下拉选项聚合接口，前端登录后缓存一次。</summary>
[ApiController]
[Route("api/lookup")]
public class LookupController : ControllerBase
{
    private readonly SmesDbContext _db;
    public LookupController(SmesDbContext db) => _db = db;

    [HttpGet]
    public async Task<ApiResult> All()
    {
        var products = await _db.Set<Product>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name, Extra = x.Spec }).ToListAsync();
        var customers = await _db.Set<Customer>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var suppliers = await _db.Set<Supplier>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var units = await _db.Set<Unit>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var workshops = await _db.Set<Workshop>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var processes = await _db.Set<Process>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var users = await _db.Set<SysUser>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Username, Name = x.Nickname }).ToListAsync();
        var depts = await _db.Set<SysDept>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var categories = await _db.Set<ProductCategory>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var brands = await _db.Set<ProductBrand>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var defects = await _db.Set<DefectItem>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        var equipments = await _db.Set<Smes.Domain.Equipment.Equipment>().AsNoTracking().Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name }).ToListAsync();
        return ApiResult.Ok(new { products, customers, suppliers, warehouses, units, workshops, processes, users, depts, categories, brands, defects, equipments });
    }
}
