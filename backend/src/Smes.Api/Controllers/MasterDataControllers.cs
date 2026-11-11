using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Domain.MasterData;
using Smes.Domain.Production;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

[ApiController]
[Route("api/product")]
public class ProductController : CrudController<Product>
{
    public ProductController(SmesDbContext db, CurrentUser me) : base(db, me) { }

    public override async Task<ApiResult> Options([FromQuery] string? keyword, [FromQuery] int limit = 200)
    {
        var query = Db.Set<Product>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x => x.Code!.Contains(keyword) || x.Name!.Contains(keyword) || x.Spec!.Contains(keyword));
        }
        var list = await query.OrderBy(x => x.Id).Take(limit)
            .Select(x => new OptionItem { Id = x.Id, Code = x.Code, Name = x.Name, Extra = x.Spec })
            .ToListAsync();
        return ApiResult.Ok(list);
    }
}

[ApiController]
[Route("api/customer")]
public class CustomerController : CrudController<Customer>
{
    public CustomerController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/supplier")]
public class SupplierController : CrudController<Supplier>
{
    public SupplierController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/warehouse")]
public class WarehouseController : CrudController<Warehouse>
{
    public WarehouseController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/unit")]
public class UnitController : CrudController<Unit>
{
    public UnitController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/workshop")]
public class WorkshopController : CrudController<Workshop>
{
    public WorkshopController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/process")]
public class ProcessController : CrudController<Process>
{
    public ProcessController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/defect-item")]
public class DefectItemController : CrudController<DefectItem>
{
    public DefectItemController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/product-category")]
public class ProductCategoryController : CrudController<ProductCategory>
{
    public ProductCategoryController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/product-brand")]
public class ProductBrandController : CrudController<ProductBrand>
{
    public ProductBrandController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/work-calendar")]
public class WorkCalendarController : CrudController<WorkCalendar>
{
    public WorkCalendarController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

/// <summary>BOM 主从表接口</summary>
[ApiController]
[Route("api/bom")]
public class BomController : CrudController<Bom>
{
    public BomController(SmesDbContext db, CurrentUser me) : base(db, me) { }

    [HttpGet("{id:long}")]
    public override async Task<ApiResult> Get(long id)
    {
        var entity = await Db.Set<Bom>().AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id);
        return entity == null ? ApiResult.Fail("记录不存在", 404) : ApiResult.Ok(entity);
    }

    [HttpPost]
    public override async Task<ApiResult> Create([FromBody] Bom entity)
    {
        entity.Id = 0;
        entity.CreatedAt = DateTime.Now;
        entity.CreatedBy = Me.Id;
        if (string.IsNullOrWhiteSpace(entity.Code))
        {
            entity.Code = await NumberGenerator.NextAsync(Db, "BOM", Me.Id);
        }
        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            var product = await Db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == entity.ProductId);
            entity.Name = $"{product?.Name} BOM";
        }
        foreach (var line in entity.Lines) { line.Id = 0; line.BomId = 0; line.CreatedAt = DateTime.Now; line.CreatedBy = Me.Id; }
        Db.Add(entity);
        await Db.SaveChangesAsync();
        return ApiResult.Ok(entity, "新增成功");
    }

    [HttpPut("{id:long}")]
    public override async Task<ApiResult> Update(long id, [FromBody] Bom entity)
    {
        var existing = await Db.Set<Bom>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("记录不存在", 404);
        existing.Name = entity.Name;
        existing.ProductId = entity.ProductId;
        existing.Version = entity.Version;
        existing.EffectiveDate = entity.EffectiveDate;
        existing.IsDefault = entity.IsDefault;
        existing.Remark = entity.Remark;
        existing.UpdatedAt = DateTime.Now;
        existing.UpdatedBy = Me.Id;
        Db.RemoveRange(existing.Lines);
        existing.Lines = entity.Lines.Select((l, i) => new BomLine
        {
            BomId = id, Seq = i + 1, MaterialId = l.MaterialId, Qty = l.Qty, UnitId = l.UnitId,
            LossRate = l.LossRate, Remark2 = l.Remark2, CreatedAt = DateTime.Now, CreatedBy = Me.Id
        }).ToList();
        await Db.SaveChangesAsync();
        return ApiResult.Ok(existing, "修改成功");
    }
}

/// <summary>工艺路线主从表接口</summary>
[ApiController]
[Route("api/routing")]
public class RoutingController : CrudController<Routing>
{
    public RoutingController(SmesDbContext db, CurrentUser me) : base(db, me) { }

    [HttpGet("{id:long}")]
    public override async Task<ApiResult> Get(long id)
    {
        var entity = await Db.Set<Routing>().AsNoTracking().Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == id);
        return entity == null ? ApiResult.Fail("记录不存在", 404) : ApiResult.Ok(entity);
    }

    [HttpPost]
    public override async Task<ApiResult> Create([FromBody] Routing entity)
    {
        entity.Id = 0;
        entity.CreatedAt = DateTime.Now;
        entity.CreatedBy = Me.Id;
        if (string.IsNullOrWhiteSpace(entity.Code))
        {
            entity.Code = await NumberGenerator.NextAsync(Db, "ROUTING", Me.Id);
        }
        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            var product = await Db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == entity.ProductId);
            entity.Name = $"{product?.Name} 工艺路线";
        }
        foreach (var step in entity.Steps) { step.Id = 0; step.RoutingId = 0; }
        Db.Add(entity);
        await Db.SaveChangesAsync();
        // 重新查询以返回自增后的步骤 Id
        var saved = await Db.Set<Routing>().AsNoTracking().Include(x => x.Steps).FirstAsync(x => x.Id == entity.Id);
        return ApiResult.Ok(saved, "新增成功");
    }

    [HttpPut("{id:long}")]
    public override async Task<ApiResult> Update(long id, [FromBody] Routing entity)
    {
        var existing = await Db.Set<Routing>().Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("记录不存在", 404);
        existing.Name = entity.Name;
        existing.ProductId = entity.ProductId;
        existing.Version = entity.Version;
        existing.EffectiveDate = entity.EffectiveDate;
        existing.IsDefault = entity.IsDefault;
        existing.Remark = entity.Remark;
        existing.UpdatedAt = DateTime.Now;
        existing.UpdatedBy = Me.Id;
        Db.RemoveRange(existing.Steps);
        existing.Steps = entity.Steps.Select((s, i) => new RoutingStep
        {
            RoutingId = id, Seq = i + 1, ProcessId = s.ProcessId, WorkshopId = s.WorkshopId,
            StandardHours = s.StandardHours, Description = s.Description, IsKey = s.IsKey
        }).ToList();
        await Db.SaveChangesAsync();
        return ApiResult.Ok(existing, "修改成功");
    }
}
