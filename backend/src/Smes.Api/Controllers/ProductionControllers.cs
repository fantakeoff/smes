using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Api.Services;
using Smes.Domain.MasterData;
using Smes.Domain.Production;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

public record PlanLineInput(long? Id, long ProductId, decimal Qty, DateTime? DeliveryDate);

public record PlanInput(long Id, string? Name, DateTime PlanDate, DateTime StartDate, DateTime EndDate,
    long? WorkshopId, int SourceType, string? Remark, List<PlanLineInput> Lines);

[ApiController]
[Route("api/production-plan")]
public class ProductionPlanController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;

    public ProductionPlanController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? planStatus)
    {
        var query = _db.Set<ProductionPlan>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Name!.Contains(page.Keyword));
        if (planStatus.HasValue) query = query.Where(x => x.PlanStatus == planStatus);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.PlanDate).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var plan = await _db.Set<ProductionPlan>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        return plan == null ? ApiResult.Fail("计划不存在", 404) : ApiResult.Ok(plan);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] PlanInput input)
    {
        if (input.Lines == null || input.Lines.Count == 0) return ApiResult.Fail("请至少添加一行计划明细");
        var plan = new ProductionPlan
        {
            Code = await NumberGenerator.NextAsync(_db, "PLAN", _me.Id), Name = input.Name,
            PlanDate = input.PlanDate, StartDate = input.StartDate, EndDate = input.EndDate,
            WorkshopId = input.WorkshopId, SourceType = input.SourceType, Remark = input.Remark,
            PlanStatus = 0, CreatedBy = _me.Id
        };
        plan.Lines = input.Lines.Select((l, i) => new ProductionPlanLine
        {
            Seq = i + 1, ProductId = l.ProductId, Qty = l.Qty, DeliveryDate = l.DeliveryDate
        }).ToList();
        plan.TotalQty = plan.Lines.Sum(x => x.Qty);
        _db.Add(plan);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(plan, "创建成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] PlanInput input)
    {
        var plan = await _db.Set<ProductionPlan>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (plan == null) return ApiResult.Fail("计划不存在", 404);
        if (plan.PlanStatus >= 2) return ApiResult.Fail("计划已下达，不能修改");
        plan.Name = input.Name;
        plan.PlanDate = input.PlanDate;
        plan.StartDate = input.StartDate;
        plan.EndDate = input.EndDate;
        plan.WorkshopId = input.WorkshopId;
        plan.SourceType = input.SourceType;
        plan.Remark = input.Remark;
        _db.RemoveRange(plan.Lines);
        plan.Lines = (input.Lines ?? new List<PlanLineInput>()).Select((l, i) => new ProductionPlanLine
        {
            PlanId = id, Seq = i + 1, ProductId = l.ProductId, Qty = l.Qty, DeliveryDate = l.DeliveryDate
        }).ToList();
        plan.TotalQty = plan.Lines.Sum(x => x.Qty);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    /// <summary>下达计划：按明细生成生产工单，并按默认 BOM 展开领料需求。</summary>
    [HttpPost("{id:long}/release")]
    public async Task<ApiResult> Release(long id)
    {
        var plan = await _db.Set<ProductionPlan>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (plan == null) return ApiResult.Fail("计划不存在", 404);
        if (plan.PlanStatus >= 2) return ApiResult.Fail("计划已下达");

        var created = new List<WorkOrder>();
        foreach (var line in plan.Lines)
        {
            var wo = new WorkOrder
            {
                Code = await NumberGenerator.NextAsync(_db, "WO", _me.Id),
                Name = "工单-" + (await _db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == line.ProductId))?.Name,
                PlanId = plan.Id, PlanLineId = line.Id, ProductId = line.ProductId, Qty = line.Qty,
                StartDate = plan.StartDate, EndDate = line.DeliveryDate ?? plan.EndDate,
                WorkshopId = plan.WorkshopId, WorkOrderStatus = 1, SourceCode = plan.Code, CreatedBy = _me.Id,
                BomId = await _db.Set<Bom>().Where(b => b.ProductId == line.ProductId && b.IsDefault).Select(b => (long?)b.Id).FirstOrDefaultAsync()
            };
            created.Add(wo);
        }
        _db.AddRange(created);
        await _db.SaveChangesAsync();

        foreach (var wo in created)
        {
            var bomId = wo.BomId;
            if (bomId == null) continue;
            var lines = await _db.Set<BomLine>().AsNoTracking().Where(x => x.BomId == bomId).ToListAsync();
            foreach (var bl in lines)
            {
                _db.Add(new WorkOrderMaterial
                {
                    WorkOrderId = wo.Id, ProductId = bl.MaterialId, UnitId = bl.UnitId,
                    RequiredQty = Decimal.Round(wo.Qty * bl.Qty * (1 + bl.LossRate / 100), 4),
                    IssuedQty = 0
                });
            }
        }
        plan.PlanStatus = 2;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(new { count = created.Count }, $"已下达并生成 {created.Count} 张工单");
    }

    [HttpPost("{id:long}/close")]
    public async Task<ApiResult> Close(long id)
    {
        var plan = await _db.Set<ProductionPlan>().FirstOrDefaultAsync(x => x.Id == id);
        if (plan == null) return ApiResult.Fail("计划不存在", 404);
        plan.PlanStatus = 4;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "计划已关闭");
    }
}

[ApiController]
[Route("api/work-order")]
public class WorkOrderController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;
    private readonly StockService _stock;

    public WorkOrderController(SmesDbContext db, CurrentUser me, StockService stock)
    {
        _db = db; _me = me; _stock = stock;
    }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? workOrderStatus,
        [FromQuery] long? workshopId, [FromQuery] long? productId)
    {
        var query = _db.Set<WorkOrder>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Name!.Contains(page.Keyword));
        if (workOrderStatus.HasValue) query = query.Where(x => x.WorkOrderStatus == workOrderStatus);
        if (workshopId.HasValue) query = query.Where(x => x.WorkshopId == workshopId);
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var wo = await _db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (wo == null) return ApiResult.Fail("工单不存在", 404);
        var materials = await _db.Set<WorkOrderMaterial>().AsNoTracking().Where(x => x.WorkOrderId == id).ToListAsync();
        var reports = await _db.Set<WorkReport>().AsNoTracking().Where(x => x.WorkOrderId == id).OrderByDescending(x => x.ReportTime).ToListAsync();
        return ApiResult.Ok(new { workOrder = wo, materials, reports });
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] WorkOrder wo)
    {
        wo.Id = 0;
        wo.Code = await NumberGenerator.NextAsync(_db, "WO", _me.Id);
        wo.Name = string.IsNullOrWhiteSpace(wo.Name) ? "手动工单" : wo.Name;
        wo.CreatedAt = DateTime.Now;
        wo.CreatedBy = _me.Id;
        _db.Add(wo);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(wo, "创建成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] WorkOrder input)
    {
        var wo = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (wo == null) return ApiResult.Fail("工单不存在", 404);
        if (wo.WorkOrderStatus >= 3) return ApiResult.Fail("工单已完工，不能修改");
        wo.Name = input.Name;
        wo.ProductId = input.ProductId;
        wo.Qty = input.Qty;
        wo.StartDate = input.StartDate;
        wo.EndDate = input.EndDate;
        wo.WorkshopId = input.WorkshopId;
        wo.Priority = input.Priority;
        wo.Remark = input.Remark;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    [HttpPost("{id:long}/release")]
    public async Task<ApiResult> Release(long id)
    {
        var wo = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (wo == null) return ApiResult.Fail("工单不存在", 404);
        if (wo.WorkOrderStatus != 0) return ApiResult.Fail("只有未下达工单可以下达");
        wo.WorkOrderStatus = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "工单已下达");
    }

    [HttpPost("{id:long}/start")]
    public async Task<ApiResult> Start(long id)
    {
        var wo = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (wo == null) return ApiResult.Fail("工单不存在", 404);
        if (wo.WorkOrderStatus != 1) return ApiResult.Fail("只有已下达工单可以开工");
        wo.WorkOrderStatus = 2;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "已开工");
    }

    [HttpPost("{id:long}/close")]
    public async Task<ApiResult> Close(long id)
    {
        var wo = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (wo == null) return ApiResult.Fail("工单不存在", 404);
        wo.WorkOrderStatus = 4;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "工单已关闭");
    }


    public record ReceiptRequest(long WarehouseId, decimal Qty, string? BatchNo, string? Location, string? Remark);

    /// <summary>生产入库，同时回写工单完工数量。</summary>
    [HttpPost("{id:long}/receipt")]
    public async Task<ApiResult> Receipt(long id, [FromBody] ReceiptRequest request)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var wo = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (wo == null) return ApiResult.Fail("工单不存在", 404);
        if (request.Qty <= 0) return ApiResult.Fail("入库数量必须大于 0");
        if (wo.WorkOrderStatus is 0 or 4) return ApiResult.Fail("当前工单状态不允许入库");
        if (request.Qty > wo.CompletedQty - wo.ReceivedQty) return ApiResult.Fail("入库数量超过已报工合格未入库数量");
        var product = await _db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == wo.ProductId);
        var bill = new StockBill
        {
            Code = await NumberGenerator.NextAsync(_db, "BILL", _me.Id), BillType = 3, WarehouseId = request.WarehouseId,
            BillDate = DateTime.Today, BillStatus = 0, SourceType = "生产工单", SourceId = wo.Id, SourceCode = wo.Code,
            Name = "生产入库", Remark = request.Remark, OperatorId = _me.Id, CreatedBy = _me.Id
        };
        bill.Lines.Add(new StockBillLine
        {
            Seq = 1, ProductId = wo.ProductId, Qty = request.Qty, UnitId = product?.UnitId,
            Price = product?.CostPrice ?? 0, Amount = (product?.CostPrice ?? 0) * request.Qty,
            BatchNo = request.BatchNo, Location = request.Location
        });
        bill.TotalQty = request.Qty;
        bill.TotalAmount = bill.Lines[0].Amount;
        _db.Add(bill);
        await _db.SaveChangesAsync();
        await _stock.ApplyAsync(bill, _me.Id);

        wo.ReceivedQty += request.Qty;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ApiResult.Ok(new { billId = bill.Id, bill.Code }, "入库成功");
    }
}

public record WorkReportInput(long WorkOrderId, long? ProcessId, long? WorkerId, decimal Qty,
    decimal QualifiedQty, decimal DefectQty, decimal WorkHours, long? DefectItemId, string? Remark);

[ApiController]
[Route("api/work-report")]
public class WorkReportController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;

    public WorkReportController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] long? workOrderId, [FromQuery] long? processId, [FromQuery] long? workerId)
    {
        var query = _db.Set<WorkReport>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Name!.Contains(page.Keyword));
        if (workOrderId.HasValue) query = query.Where(x => x.WorkOrderId == workOrderId);
        if (processId.HasValue) query = query.Where(x => x.ProcessId == processId);
        if (workerId.HasValue) query = query.Where(x => x.WorkerId == workerId);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.ReportTime).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] WorkReportInput input, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (input.WorkOrderId <= 0 || input.Qty <= 0 || input.QualifiedQty < 0 || input.DefectQty < 0 ||
            input.WorkHours < 0 || input.QualifiedQty + input.DefectQty != input.Qty)
            return ApiResult.Fail("报工数据无效");
        if (idempotencyKey?.Length > 128) return ApiResult.Fail("Idempotency-Key 长度不能超过 128 个字符");
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var wo = await _db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.WorkOrderId);
        if (wo == null) return ApiResult.Fail("工单不存在", 404);

        WorkReportJob? job = null;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            job = await _db.Set<WorkReportJob>().FirstOrDefaultAsync(x => x.TenantId == _me.TenantId && x.IdempotencyKey == idempotencyKey);
        if (job != null)
        {
            var samePayload = job.WorkOrderId == wo.Id && job.ProcessId == input.ProcessId &&
                job.WorkerId == (input.WorkerId ?? _me.Id) && job.Qty == input.Qty &&
                job.QualifiedQty == input.QualifiedQty && job.DefectQty == input.DefectQty &&
                job.WorkHours == input.WorkHours && job.DefectItemId == input.DefectItemId &&
                string.Equals(job.Remark, input.Remark, StringComparison.Ordinal);
            if (!samePayload) return ApiResult.Fail("Idempotency-Key 已用于不同的报工数据", 409);
            await transaction.CommitAsync();
            return ApiResult.Ok(new { jobId = job.Id, status = JobStatusName(job.JobStatus), accepted = true }, "报工任务已存在");
        }
        if (wo.WorkOrderStatus is 0 or 3 or 4) return ApiResult.Fail("当前工单状态不允许报工");

        if (job == null)
        {
            job = new WorkReportJob
            {
                TenantId = _me.TenantId, WorkOrderId = wo.Id, ProcessId = input.ProcessId,
                WorkerId = input.WorkerId ?? _me.Id, Qty = input.Qty, QualifiedQty = input.QualifiedQty,
                DefectQty = input.DefectQty, WorkHours = input.WorkHours, DefectItemId = input.DefectItemId,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
                JobStatus = 0, CreatedBy = _me.Id, Name = "生产报工", Remark = input.Remark
            };
            _db.Add(job);
            await _db.SaveChangesAsync();
            var routingKey = $"tenant.{_me.TenantId}.production.work-report.requested.v1";
            var messageId = Guid.NewGuid();
            _db.Add(new OutboxMessage
            {
                TenantId = _me.TenantId, MessageId = messageId, RoutingKey = routingKey,
                EventType = "work-report.requested.v1",
                Payload = System.Text.Json.JsonSerializer.Serialize(new Smes.Api.Messaging.WorkReportRequested(
                    messageId, job.Id, _me.TenantId, wo.Id, job.ProcessId, job.WorkerId, job.Qty,
                    job.QualifiedQty, job.DefectQty, job.WorkHours, job.DefectItemId, _me.Id, input.Remark)),
                CreatedBy = _me.Id
            });
            await _db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return ApiResult.Ok(new { jobId = job.Id, status = JobStatusName(job.JobStatus), accepted = true }, "已进入报工队列");
    }

    [HttpGet("jobs/{id:long}")]
    public async Task<ApiResult> GetJob(long id)
    {
        var job = await _db.Set<WorkReportJob>().AsNoTracking()
            .Where(x => x.TenantId == _me.TenantId && x.Id == id)
            .Select(x => new { jobId = x.Id, status = JobStatusName(x.JobStatus), accepted = true, x.Error, x.WorkReportId })
            .FirstOrDefaultAsync();
        return job == null ? ApiResult.Fail("报工任务不存在", 404) : ApiResult.Ok(job);
    }

    private static string JobStatusName(int status) => status switch
    {
        0 => "queued", 1 => "processing", 2 => "completed", 3 => "failed", _ => "unknown"
    };

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        var report = await _db.Set<WorkReport>().FirstOrDefaultAsync(x => x.Id == id);
        if (report == null) return ApiResult.Fail("记录不存在", 404);
        report.IsDeleted = true;
        var wo = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == report.WorkOrderId);
        if (wo != null)
        {
            if (wo.ReceivedQty > wo.CompletedQty - report.QualifiedQty)
                return ApiResult.Fail("该报工的合格数量已经入库，不能直接删除，请走冲销流程");
            wo.CompletedQty = Math.Max(0, wo.CompletedQty - report.QualifiedQty);
            wo.DefectQty = Math.Max(0, wo.DefectQty - report.DefectQty);
            if (wo.WorkOrderStatus == 3 && wo.CompletedQty + wo.DefectQty < wo.Qty) wo.WorkOrderStatus = 2;
        }
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}
