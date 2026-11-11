using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Smes.Api.Infrastructure;
using Smes.Domain.Equipment;
using Smes.Domain.Tooling;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

[ApiController]
[Route("api/equipment-category")]
public class EquipmentCategoryController : CrudController<EquipmentCategory>
{
    public EquipmentCategoryController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/equipment")]
public class EquipmentController : CrudController<Equipment>
{
    public EquipmentController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/maintenance-plan")]
public class MaintenancePlanController : CrudController<MaintenancePlan>
{
    public MaintenancePlanController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/maintenance-task")]
public class MaintenanceTaskController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;

    public MaintenanceTaskController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? taskStatus,
        [FromQuery] int? maintenanceType, [FromQuery] long? equipmentId)
    {
        var query = _db.Set<MaintenanceTask>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword))
            query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Name!.Contains(page.Keyword));
        if (taskStatus.HasValue) query = query.Where(x => x.TaskStatus == taskStatus);
        if (maintenanceType.HasValue) query = query.Where(x => x.MaintenanceType == maintenanceType);
        if (equipmentId.HasValue) query = query.Where(x => x.EquipmentId == equipmentId);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.TaskDate).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var task = await _db.Set<MaintenanceTask>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == _me.TenantId);
        return task == null ? ApiResult.Fail("任务不存在", 404) : ApiResult.Ok(task);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] MaintenanceTask task)
    {
        task.Id = 0;
        task.Code = string.IsNullOrWhiteSpace(task.Code) ? $"DJ{DateTime.Now:yyyyMMddHHmmss}" : task.Code;
        task.Name = string.IsNullOrWhiteSpace(task.Name) ? (task.MaintenanceType == 1 ? "点检任务" : "保养任务") : task.Name;
        task.CreatedAt = DateTime.Now;
        _db.Add(task);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(task, "创建成功");
    }

    public record ExecuteRequest(string? Result, decimal Cost, string? Remark);

    [HttpPost("{id:long}/execute")]
    public async Task<ApiResult> Execute(long id, [FromBody] ExecuteRequest request)
    {
        if (request.Cost < 0) return ApiResult.Fail("维护费用不能为负数");
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var task = await _db.Set<MaintenanceTask>().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == _me.TenantId);
        if (task == null) return ApiResult.Fail("任务不存在", 404);
        var desiredStatus = request.Result != null && request.Result.Contains("异常") ? 2 : 1;
        if (task.TaskStatus != 0)
        {
            var sameExecution = task.TaskStatus == desiredStatus && task.ExecutorId == _me.Id &&
                task.Cost == request.Cost && string.Equals(task.Result, request.Result, StringComparison.Ordinal) &&
                string.Equals(task.Remark, request.Remark, StringComparison.Ordinal);
            if (sameExecution)
            {
                await tx.CommitAsync();
                return ApiResult.Ok(new { task.Id, task.TaskStatus, task.FinishAt }, "维护任务已执行，返回既有结果");
            }
            return ApiResult.Fail("任务已由本次之外的操作执行，禁止覆盖；请先核对任务状态", 409);
        }
        task.TaskStatus = desiredStatus;
        task.Result = request.Result;
        task.Cost = request.Cost;
        task.Remark = request.Remark;
        task.ExecutorId = _me.Id;
        task.FinishAt = DateTime.Now;
        if (task.PlanId.HasValue)
        {
            var plan = await _db.Set<MaintenancePlan>().FirstOrDefaultAsync(x => x.Id == task.PlanId);
            if (plan != null)
            {
                plan.NextDate = DateTime.Today.AddDays(plan.CycleDays);
            }
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult.Ok(null, "执行完成");
    }
}

[ApiController]
[Route("api/repair-order")]
public class RepairOrderController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;

    public RepairOrderController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? repairStatus,
        [FromQuery] long? equipmentId)
    {
        var query = _db.Set<RepairOrder>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword))
            query = query.Where(x => x.Code!.Contains(page.Keyword) || x.FaultDesc!.Contains(page.Keyword));
        if (repairStatus.HasValue) query = query.Where(x => x.RepairStatus == repairStatus);
        if (equipmentId.HasValue) query = query.Where(x => x.EquipmentId == equipmentId);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.ReportTime).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var order = await _db.Set<RepairOrder>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == _me.TenantId);
        return order == null ? ApiResult.Fail("维修单不存在", 404) : ApiResult.Ok(order);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] RepairOrder order,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (order.EquipmentId <= 0 || string.IsNullOrWhiteSpace(order.FaultDesc))
            return ApiResult.Fail("请选择设备并填写故障描述");
        if (!string.IsNullOrWhiteSpace(idempotencyKey) && (idempotencyKey.Length > 128 || idempotencyKey.Any(char.IsControl)))
            return ApiResult.Fail("Idempotency-Key 无效（最多 128 个字符）");
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var equipment = await _db.Set<Equipment>().FirstOrDefaultAsync(x => x.Id == order.EquipmentId && x.TenantId == _me.TenantId);
        if (equipment == null) return ApiResult.Fail("设备不存在", 404);
        var code = string.IsNullOrWhiteSpace(idempotencyKey)
            ? await NumberGenerator.NextAsync(_db, "REP", _me.Id)
            : "REP" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{_me.TenantId}:{idempotencyKey}")))[..20];
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.Set<RepairOrder>().FirstOrDefaultAsync(x => x.TenantId == _me.TenantId && x.Code == code);
            if (existing != null)
            {
                var samePayload = existing.EquipmentId == order.EquipmentId && existing.Level == order.Level &&
                    string.Equals(existing.FaultDesc, order.FaultDesc, StringComparison.Ordinal) &&
                    string.Equals(existing.Remark, order.Remark, StringComparison.Ordinal);
                if (!samePayload) return ApiResult.Fail("Idempotency-Key 已用于不同的报修内容", 409);
                await tx.CommitAsync();
                return ApiResult.Ok(existing, "已返回该幂等键对应的报修单");
            }
        }
        order.Id = 0;
        order.Code = code;
        order.Name = "设备报修";
        order.ReportTime = DateTime.Now;
        order.ReporterId = _me.Id;
        order.RepairStatus = 0;
        order.CreatedAt = DateTime.Now;
        _db.Add(order);
        equipment.EquipmentStatus = 3;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult.Ok(order, "报修成功");
    }

    public record HandleRequest(string? Solution, int RepairStatus, decimal Cost, int DowntimeMinutes);

    [HttpPost("{id:long}/handle")]
    public async Task<ApiResult> Handle(long id, [FromBody] HandleRequest request)
    {
        if (request.RepairStatus is < 1 or > 3 || request.Cost < 0 || request.DowntimeMinutes < 0)
            return ApiResult.Fail("维修状态、费用或停机时间无效");
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var order = await _db.Set<RepairOrder>().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == _me.TenantId);
        if (order == null) return ApiResult.Fail("维修单不存在", 404);
        var sameExecution = order.RepairStatus == request.RepairStatus && order.HandlerId == _me.Id &&
            order.Cost == request.Cost && order.DowntimeMinutes == request.DowntimeMinutes &&
            string.Equals(order.Solution, request.Solution, StringComparison.Ordinal);
        if (sameExecution)
        {
            await tx.CommitAsync();
            return ApiResult.Ok(new { order.Id, order.RepairStatus, order.FinishTime }, "已返回既有维修处理结果");
        }
        if (order.RepairStatus >= 2 || request.RepairStatus <= order.RepairStatus)
            return ApiResult.Fail("维修单已进入其他状态，禁止覆盖；请先核对维修记录", 409);
        order.Solution = request.Solution;
        order.RepairStatus = request.RepairStatus;
        order.Cost = request.Cost;
        order.DowntimeMinutes = request.DowntimeMinutes;
        order.HandlerId = _me.Id;
        if (request.RepairStatus >= 2)
        {
            order.FinishTime = DateTime.Now;
            var equipment = await _db.Set<Equipment>().FirstOrDefaultAsync(x => x.Id == order.EquipmentId && x.TenantId == _me.TenantId);
            if (equipment != null) equipment.EquipmentStatus = 1;
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult.Ok(new { order.Id, order.RepairStatus, order.FinishTime }, "维修状态已更新");
    }
}

[ApiController]
[Route("api/equipment-alarm")]
public class EquipmentAlarmController : ControllerBase
{
    private readonly SmesDbContext _db;
    public EquipmentAlarmController(SmesDbContext db) => _db = db;

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? alarmStatus,
        [FromQuery] int? alarmType, [FromQuery] long? equipmentId)
    {
        var query = _db.Set<EquipmentAlarm>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword))
            query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Content!.Contains(page.Keyword));
        if (alarmStatus.HasValue) query = query.Where(x => x.AlarmStatus == alarmStatus);
        if (alarmType.HasValue) query = query.Where(x => x.AlarmType == alarmType);
        if (equipmentId.HasValue) query = query.Where(x => x.EquipmentId == equipmentId);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpPost("{id:long}/ack")]
    public async Task<ApiResult> Ack(long id)
    {
        var alarm = await _db.Set<EquipmentAlarm>().FirstOrDefaultAsync(x => x.Id == id);
        if (alarm == null) return ApiResult.Fail("报警不存在", 404);
        alarm.AlarmStatus = 2;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "已确认");
    }

    [HttpPost("{id:long}/close")]
    public async Task<ApiResult> Close(long id)
    {
        var alarm = await _db.Set<EquipmentAlarm>().FirstOrDefaultAsync(x => x.Id == id);
        if (alarm == null) return ApiResult.Fail("报警不存在", 404);
        alarm.AlarmStatus = 3;
        if (alarm.RecoveredAt == null) alarm.RecoveredAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "已关闭");
    }
}

[ApiController]
[Route("api/tool-category")]
public class ToolCategoryController : CrudController<ToolCategory>
{
    public ToolCategoryController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/tool")]
public class ToolController : CrudController<Tool>
{
    public ToolController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

[ApiController]
[Route("api/tool-transaction")]
public class ToolTransactionController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;

    public ToolTransactionController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? transactionType,
        [FromQuery] long? toolId)
    {
        var query = _db.Set<ToolTransaction>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword))
            query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Name!.Contains(page.Keyword));
        if (transactionType.HasValue) query = query.Where(x => x.TransactionType == transactionType);
        if (toolId.HasValue) query = query.Where(x => x.ToolId == toolId);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.OccurDate).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] ToolTransaction transaction)
    {
        var tool = await _db.Set<Tool>().FirstOrDefaultAsync(x => x.Id == transaction.ToolId);
        if (tool == null) return ApiResult.Fail("工装不存在", 404);
        if (transaction.TransactionType == 1 && tool.AvailableQty < transaction.Qty)
            return ApiResult.Fail("工装可用数量不足");
        transaction.Id = 0;
        transaction.Code = (transaction.TransactionType == 1 ? "LY" : "GH") + DateTime.Now.ToString("yyyyMMddHHmmss");
        transaction.Name = transaction.TransactionType == 1 ? "工装领用" : "工装归还";
        transaction.OccurDate = transaction.OccurDate == default ? DateTime.Today : transaction.OccurDate;
        transaction.CreatedAt = DateTime.Now;
        transaction.TransactionStatus = 1;
        transaction.UserId ??= _me.Id;
        if (transaction.TransactionType == 1)
        {
            tool.AvailableQty -= transaction.Qty;
            tool.ToolStatus = 2;
        }
        else
        {
            tool.AvailableQty += transaction.Qty;
            if (tool.AvailableQty >= tool.TotalQty) tool.ToolStatus = 1;
            transaction.ReturnDate = DateTime.Today;
        }
        _db.Add(transaction);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(transaction, transaction.TransactionType == 1 ? "领用成功" : "归还成功");
    }
}
