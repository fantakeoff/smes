using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Smes.Domain.Common;
using Smes.Domain.Identity;
using Smes.Infrastructure;

namespace Smes.Api.Infrastructure;

public class CurrentUser
{
    public long Id { get; init; }
    public long TenantId { get; init; }
    public string TenantCode { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string Username { get; init; } = "system";
    public string Nickname { get; init; } = "";
    public bool IsAdmin { get; init; }
    public List<string> Roles { get; init; } = new();
    public List<string> Permissions { get; init; } = new();
}

public static class QueryExtensions
{
    private static readonly Dictionary<Type, PropertyInfo[]> Cache = new();

    private static PropertyInfo[] Props(Type t)
    {
        if (Cache.TryGetValue(t, out var p)) return p;
        p = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
             .Where(x => IsScalar(x.PropertyType)).ToArray();
        Cache[t] = p;
        return p;
    }

    private static bool IsScalar(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u.IsPrimitive || u.IsEnum || u == typeof(string) || u == typeof(decimal)
               || u == typeof(DateTime) || u == typeof(Guid);
    }

    /// <summary>按查询字符串里与实体属性同名的字段做等值过滤（字符串模糊），并处理 keyword/status/日期区间/排序。</summary>
    public static IQueryable<T> ApplyQuery<T>(this IQueryable<T> query, PageQuery page, IQueryCollection raw)
    {
        var type = typeof(T);
        var props = Props(type);

        if (!string.IsNullOrWhiteSpace(page.Keyword))
        {
            var kw = page.Keyword.Trim();
            var codeProp = props.FirstOrDefault(p => p.Name == "Code");
            var nameProp = props.FirstOrDefault(p => p.Name == "Name");
            if (codeProp != null || nameProp != null)
            {
                var param = Expression.Parameter(type, "e");
                Expression? body = null;
                foreach (var p in new[] { codeProp, nameProp }.Where(x => x != null))
                {
                    var call = Expression.Call(
                        Expression.Property(param, p!),
                        typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!,
                        Expression.Constant(kw));
                    body = body == null ? call : Expression.OrElse(body, call);
                }
                if (body != null)
                {
                    query = query.Where(Expression.Lambda<Func<T, bool>>(body, param));
                }
            }
        }

        if (page.Status.HasValue && props.Any(p => p.Name == "Status"))
        {
            query = query.Where(e => EF.Property<int>(e, "Status") == page.Status.Value);
        }

        foreach (var kv in raw)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            var name = kv.Key;
            if (name is "page" or "pageSize" or "keyword" or "sortBy" or "sortOrder" or "startDate" or "endDate" or "status") continue;
            var prop = props.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (prop == null) continue;
            var value = kv.Value.ToString();
            var param = Expression.Parameter(type, "e");
            var member = Expression.Property(param, prop);
            try
            {
                if (prop.PropertyType == typeof(string))
                {
                    var call = Expression.Call(member, typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!, Expression.Constant(value));
                    query = query.Where(Expression.Lambda<Func<T, bool>>(call, param));
                }
                else
                {
                    var target = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    var converted = target == typeof(DateTime)
                        ? DateTime.Parse(value, CultureInfo.InvariantCulture)
                        : Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
                    var body = Expression.Equal(member, Expression.Constant(converted, prop.PropertyType));
                    query = query.Where(Expression.Lambda<Func<T, bool>>(body, param));
                }
            }
            catch
            {
                // 忽略无法转换的过滤条件
            }
        }

        var dateProp = props.FirstOrDefault(p => p.Name == "CreatedAt" || p.Name == "BillDate" || p.Name == "OrderDate" || p.Name == "ReportTime");
        if (dateProp != null && (page.StartDate != null || page.EndDate != null))
        {
            var param = Expression.Parameter(type, "e");
            Expression? body = null;
            if (DateTime.TryParse(page.StartDate, out var start))
            {
                body = Expression.GreaterThanOrEqual(Expression.Property(param, dateProp), Expression.Constant(start, dateProp.PropertyType));
            }
            if (DateTime.TryParse(page.EndDate, out var end))
            {
                var endExpr = Expression.LessThanOrEqual(Expression.Property(param, dateProp), Expression.Constant(end.Date.AddDays(1), dateProp.PropertyType));
                body = body == null ? endExpr : Expression.AndAlso(body, endExpr);
            }
            if (body != null) query = query.Where(Expression.Lambda<Func<T, bool>>(body, param));
        }

        if (!string.IsNullOrWhiteSpace(page.SortBy))
        {
            var sortProp = props.FirstOrDefault(p => string.Equals(p.Name, page.SortBy, StringComparison.OrdinalIgnoreCase));
            if (sortProp != null)
            {
                var param = Expression.Parameter(type, "e");
                var lambda = Expression.Lambda(Expression.Property(param, sortProp), param);
                var method = page.SortOrder?.ToLower() == "asc" ? "OrderBy" : "OrderByDescending";
                query = (IQueryable<T>)typeof(Queryable).GetMethods()
                    .First(m => m.Name == method && m.GetParameters().Length == 2)
                    .MakeGenericMethod(type, sortProp.PropertyType)
                    .Invoke(null, new object[] { query, lambda })!;
                return query;
            }
        }

        if (props.Any(p => p.Name == "CreatedAt"))
        {
            query = query.OrderByDescending(e => EF.Property<DateTime>(e, "CreatedAt"));
        }
        else
        {
            query = query.OrderByDescending(e => EF.Property<long>(e, "Id"));
        }
        return query;
    }
}

/// <summary>通用 CRUD 控制器：单表主数据页面直接继承，只需声明路由与可选过滤条件。</summary>
[Microsoft.AspNetCore.Authorization.Authorize]
public abstract class CrudController<T> : Microsoft.AspNetCore.Mvc.ControllerBase where T : BaseEntity, new()
{
    protected readonly SmesDbContext Db;
    protected readonly CurrentUser Me;
    protected virtual string? CodeRule => null;

    protected CrudController(SmesDbContext db, CurrentUser me)
    {
        Db = db;
        Me = me;
    }

    protected virtual IQueryable<T> Filter(IQueryable<T> query) => query;

    [Microsoft.AspNetCore.Mvc.HttpGet]
    public virtual async Task<ApiResult> List([Microsoft.AspNetCore.Mvc.FromQuery] PageQuery page)
    {
        var query = Filter(Db.Set<T>().AsNoTracking()).ApplyQuery(page, Request.Query);
        var total = await query.CountAsync();
        var list = await query.Skip((Math.Max(page.Page, 1) - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new PagedResult<T> { List = list, Total = total, PageSize = page.PageSize, CurrentPage = page.Page });
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id:long}")]
    public virtual async Task<ApiResult> Get(long id)
    {
        var entity = await Filter(Db.Set<T>().AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id);
        return entity == null ? ApiResult.Fail("记录不存在", 404) : ApiResult.Ok(entity);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost]
    public virtual async Task<ApiResult> Create([Microsoft.AspNetCore.Mvc.FromBody] T entity)
    {
        entity.Id = 0;
        entity.CreatedAt = DateTime.Now;
        entity.CreatedBy = Me.Id;
        if (string.IsNullOrWhiteSpace(entity.Code) && !string.IsNullOrWhiteSpace(CodeRule))
        {
            entity.Code = await NumberGenerator.NextAsync(Db, CodeRule, Me.Id);
        }
        Db.Add(entity);
        await Db.SaveChangesAsync();
        return ApiResult.Ok(entity, "新增成功");
    }

    [Microsoft.AspNetCore.Mvc.HttpPut("{id:long}")]
    public virtual async Task<ApiResult> Update(long id, [Microsoft.AspNetCore.Mvc.FromBody] T entity)
    {
        var existing = await Db.Set<T>().FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("记录不存在", 404);
        var createdAt = existing.CreatedAt;
        var createdBy = existing.CreatedBy;
        Db.Entry(existing).CurrentValues.SetValues(entity);
        existing.Id = id;
        existing.CreatedAt = createdAt;
        existing.CreatedBy = createdBy;
        existing.UpdatedAt = DateTime.Now;
        existing.UpdatedBy = Me.Id;
        await Db.SaveChangesAsync();
        return ApiResult.Ok(existing, "修改成功");
    }

    [Microsoft.AspNetCore.Mvc.HttpDelete("{id:long}")]
    public virtual async Task<ApiResult> Delete(long id)
    {
        var existing = await Db.Set<T>().FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("记录不存在", 404);
        existing.IsDeleted = true;
        existing.UpdatedAt = DateTime.Now;
        existing.UpdatedBy = Me.Id;
        await Db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }

    [Microsoft.AspNetCore.Mvc.HttpPost("batch-delete")]
    public virtual async Task<ApiResult> BatchDelete([Microsoft.AspNetCore.Mvc.FromBody] List<long> ids)
    {
        var list = await Db.Set<T>().Where(x => ids.Contains(x.Id)).ToListAsync();
        foreach (var item in list)
        {
            item.IsDeleted = true;
            item.UpdatedAt = DateTime.Now;
            item.UpdatedBy = Me.Id;
        }
        await Db.SaveChangesAsync();
        return ApiResult.Ok(null, $"已删除 {list.Count} 条");
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("options")]
    public virtual async Task<ApiResult> Options([Microsoft.AspNetCore.Mvc.FromQuery] string? keyword, [Microsoft.AspNetCore.Mvc.FromQuery] int limit = 200)
    {
        var query = Db.Set<T>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x => x.Code!.Contains(keyword) || x.Name!.Contains(keyword));
        }
        var list = await query.OrderBy(x => x.Id).Take(limit)
            .Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name })
            .ToListAsync();
        return ApiResult.Ok(list);
    }
}
