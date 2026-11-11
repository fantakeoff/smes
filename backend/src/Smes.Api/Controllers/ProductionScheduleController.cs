using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Domain.Production;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

/// <summary>小时级排程：拖动计划整体平移；拖动工单同步更新所属计划的起止范围。</summary>
[ApiController]
[Authorize]
[Route("api/production-schedule")]
public class ProductionScheduleController(SmesDbContext db, CurrentUser me) : ControllerBase
{
    /// <summary>时间均为工厂本地时间，无时区后缀；原日期数据视为当天 00:00。</summary>
    public record MoveRequest(DateTime ExpectedStart, DateTime ExpectedEnd, DateTime NewStart);

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] long? workshopId)
    {
        // 排程与计划保持一致，不对两张表独立分页导致计划下的工单缺失。
        var plans = await db.Set<ProductionPlan>().AsNoTracking()
            .OrderBy(p => p.StartDate).ThenBy(p => p.Id).ToListAsync();
        var orders = await db.Set<WorkOrder>().AsNoTracking()
            .OrderBy(w => w.StartDate).ThenBy(w => w.Id).ToListAsync();
        if (workshopId.HasValue)
        {
            orders = orders.Where(w => w.WorkshopId == workshopId).ToList();
            plans = plans.Where(p => p.WorkshopId == workshopId || orders.Any(w => w.PlanId == p.Id)).ToList();
        }
        var visiblePlanIds = plans.Select(p => p.Id).ToHashSet();
        var groups = plans.Select(p => new
        {
            p.Id, p.Code, p.Name, p.StartDate, p.EndDate, p.PlanStatus, p.WorkshopId, p.SourceType,
            Orders = orders.Where(w => w.PlanId == p.Id).Select(w => OrderProjection(w, p.SourceType)).ToList()
        }).ToList();
        var unplanned = orders.Where(w => w.PlanId == null || !visiblePlanIds.Contains(w.PlanId.Value))
            .Select(w => OrderProjection(w, null)).ToList();
        return ApiResult.Ok(new { plans = groups, unplannedOrders = unplanned });
    }

    /// <summary>将整个生产计划及其下全部工单、明细交期按相同小时数平移。</summary>
    [HttpPut("plans/{id:long}/move")]
    public async Task<ApiResult> MovePlan(long id, [FromBody] MoveRequest request)
    {
        var invalid = ValidateRequest(request);
        if (invalid != null) return invalid;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var plan = await db.Set<ProductionPlan>().FirstOrDefaultAsync(p => p.Id == id);
        if (plan == null) return ApiResult.Fail("生产计划不存在", 404);
        if (plan.PlanStatus >= 3) return ApiResult.Fail("已完成或已关闭的计划不能调整排程");
        if (plan.StartDate != request.ExpectedStart || plan.EndDate != request.ExpectedEnd)
            return ApiResult.Fail("计划时间已发生变化，请刷新排程后重试", 409);
        if (plan.EndDate < plan.StartDate) return ApiResult.Fail("计划起止时间异常，无法平移");

        var orders = await db.Set<WorkOrder>().Where(w => w.PlanId == id)
            .OrderBy(w => w.Id).ToListAsync();
        if (orders.Any(w => w.WorkOrderStatus is 3 or 4))
            return ApiResult.Fail("计划下存在已完工或已关闭工单，不能整体平移");

        var delta = request.NewStart - plan.StartDate;
        if (delta == TimeSpan.Zero)
            return ApiResult.Ok(new { planId = plan.Id, plan.StartDate, plan.EndDate, orderCount = orders.Count }, "排程未变化");
        if (!WithinScheduleRange(plan.EndDate, delta) || orders.Any(w => !WithinScheduleRange(w.StartDate, delta) || !WithinScheduleRange(w.EndDate, delta)))
            return ApiResult.Fail("目标时间超出允许排程范围（2000—2100 年）");

        var lines = await db.Set<ProductionPlanLine>().Where(l => l.PlanId == id).ToListAsync();
        if (lines.Any(l => l.DeliveryDate.HasValue && !WithinScheduleRange(l.DeliveryDate.Value, delta)))
            return ApiResult.Fail("计划明细交期超出允许排程范围（2000—2100 年）");

        var now = DateTime.Now;
        plan.StartDate = plan.StartDate.Add(delta);
        plan.EndDate = plan.EndDate.Add(delta);
        plan.UpdatedAt = now;
        plan.UpdatedBy = me.Id;
        foreach (var order in orders)
        {
            order.StartDate = order.StartDate.Add(delta);
            order.EndDate = order.EndDate.Add(delta);
            order.UpdatedAt = now;
            order.UpdatedBy = me.Id;
        }
        foreach (var line in lines)
        {
            if (line.DeliveryDate.HasValue) line.DeliveryDate = line.DeliveryDate.Value.Add(delta);
            line.UpdatedAt = now;
            line.UpdatedBy = me.Id;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ApiResult.Ok(new { planId = plan.Id, plan.StartDate, plan.EndDate, orderCount = orders.Count }, "生产计划及工单已按整小时平移");
    }

    /// <summary>平移单张工单；所属计划的起止时间按旗下工单实际范围重新计算。</summary>
    [HttpPut("orders/{id:long}/move")]
    public async Task<ApiResult> MoveOrder(long id, [FromBody] MoveRequest request)
    {
        var invalid = ValidateRequest(request);
        if (invalid != null) return invalid;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await db.Set<WorkOrder>().FirstOrDefaultAsync(w => w.Id == id);
        if (order == null) return ApiResult.Fail("生产工单不存在", 404);
        if (order.WorkOrderStatus is 3 or 4) return ApiResult.Fail("已完工或已关闭的工单不能调整排程");
        if (order.StartDate != request.ExpectedStart || order.EndDate != request.ExpectedEnd)
            return ApiResult.Fail("工单时间已发生变化，请刷新排程后重试", 409);
        if (order.EndDate < order.StartDate) return ApiResult.Fail("工单起止时间异常，无法平移");

        ProductionPlan? plan = null;
        if (order.PlanId.HasValue)
        {
            plan = await db.Set<ProductionPlan>().FirstOrDefaultAsync(p => p.Id == order.PlanId);
            if (plan == null) return ApiResult.Fail("所属生产计划不存在，请先核对工单关联", 409);
            if (plan.PlanStatus >= 3) return ApiResult.Fail("所属计划已完成或已关闭，不能调整排程");
        }

        var delta = request.NewStart - order.StartDate;
        if (delta == TimeSpan.Zero)
            return ApiResult.Ok(new { workOrderId = order.Id, order.StartDate, order.EndDate, planId = plan?.Id,
                planStart = plan?.StartDate, planEnd = plan?.EndDate }, "排程未变化");
        if (!WithinScheduleRange(order.EndDate, delta))
            return ApiResult.Fail("目标时间超出允许排程范围（2000—2100 年）");

        var now = DateTime.Now;
        order.StartDate = order.StartDate.Add(delta);
        order.EndDate = order.EndDate.Add(delta);
        order.UpdatedAt = now;
        order.UpdatedBy = me.Id;

        if (plan != null)
        {
            var siblings = await db.Set<WorkOrder>().Where(w => w.PlanId == plan.Id).ToListAsync();
            plan.StartDate = siblings.Min(w => w.StartDate);
            plan.EndDate = siblings.Max(w => w.EndDate);
            plan.UpdatedAt = now;
            plan.UpdatedBy = me.Id;

            if (order.PlanLineId.HasValue)
            {
                var line = await db.Set<ProductionPlanLine>().FirstOrDefaultAsync(l => l.Id == order.PlanLineId && l.PlanId == plan.Id);
                if (line != null)
                {
                    line.DeliveryDate = siblings.Where(w => w.PlanLineId == line.Id).Max(w => w.EndDate);
                    line.UpdatedAt = now;
                    line.UpdatedBy = me.Id;
                }
            }
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ApiResult.Ok(new { workOrderId = order.Id, order.StartDate, order.EndDate,
            planId = plan?.Id, planStart = plan?.StartDate, planEnd = plan?.EndDate }, "工单和所属生产计划时间已同步更新");
    }

    /// <summary>调整单张工单时长（拖动甘特条左右边缘）；所属计划范围和交期同步重算。</summary>
    public record RangeRequest(DateTime ExpectedStart, DateTime ExpectedEnd, DateTime NewStart, DateTime NewEnd);

    [HttpPut("orders/{id:long}/range")]
    public async Task<ApiResult> ResizeOrder(long id, [FromBody] RangeRequest request)
    {
        if (request.ExpectedStart == default || request.ExpectedEnd == default || request.NewStart == default || request.NewEnd == default)
            return ApiResult.Fail("请提供原始和新的起止时间");
        if (request.ExpectedEnd < request.ExpectedStart)
            return ApiResult.Fail("原工单结束时间不能早于开始时间");
        if (!IsExactHour(request.ExpectedStart) || !IsExactHour(request.ExpectedEnd) || !IsExactHour(request.NewStart) || !IsExactHour(request.NewEnd))
            return ApiResult.Fail("排程只能调整到整小时（分钟、秒须为 00）");
        if (request.NewEnd <= request.NewStart)
            return ApiResult.Fail("工单结束时间必须晚于开始时间");
        if (request.NewStart.Year is < 2000 or > 2100 || request.NewEnd.Year is < 2000 or > 2100)
            return ApiResult.Fail("排程时间必须在 2000—2100 年之间");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await db.Set<WorkOrder>().FirstOrDefaultAsync(w => w.Id == id);
        if (order == null) return ApiResult.Fail("生产工单不存在", 404);
        if (order.WorkOrderStatus is 3 or 4) return ApiResult.Fail("已完工或已关闭的工单不能调整排程");
        if (order.StartDate != request.ExpectedStart || order.EndDate != request.ExpectedEnd)
            return ApiResult.Fail("工单时间已发生变化，请刷新排程后重试", 409);

        ProductionPlan? plan = null;
        if (order.PlanId.HasValue)
        {
            plan = await db.Set<ProductionPlan>().FirstOrDefaultAsync(p => p.Id == order.PlanId);
            if (plan == null) return ApiResult.Fail("所属生产计划不存在，请先核对工单关联", 409);
            if (plan.PlanStatus >= 3) return ApiResult.Fail("所属计划已完成或已关闭，不能调整排程");
        }

        if (order.StartDate == request.NewStart && order.EndDate == request.NewEnd)
            return ApiResult.Ok(new { workOrderId = order.Id, order.StartDate, order.EndDate,
                planId = plan?.Id, planStart = plan?.StartDate, planEnd = plan?.EndDate }, "排程未变化");

        var now = DateTime.Now;
        order.StartDate = request.NewStart;
        order.EndDate = request.NewEnd;
        order.UpdatedAt = now;
        order.UpdatedBy = me.Id;

        if (plan != null)
        {
            var siblings = await db.Set<WorkOrder>().Where(w => w.PlanId == plan.Id).ToListAsync();
            plan.StartDate = siblings.Min(w => w.StartDate);
            plan.EndDate = siblings.Max(w => w.EndDate);
            plan.UpdatedAt = now;
            plan.UpdatedBy = me.Id;

            if (order.PlanLineId.HasValue)
            {
                var line = await db.Set<ProductionPlanLine>().FirstOrDefaultAsync(l => l.Id == order.PlanLineId && l.PlanId == plan.Id);
                if (line != null)
                {
                    line.DeliveryDate = siblings.Where(w => w.PlanLineId == line.Id).Max(w => w.EndDate);
                    line.UpdatedAt = now;
                    line.UpdatedBy = me.Id;
                }
            }
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ApiResult.Ok(new { workOrderId = order.Id, order.StartDate, order.EndDate,
            planId = plan?.Id, planStart = plan?.StartDate, planEnd = plan?.EndDate }, "工单时长及所属计划时间已更新");
    }

    private static bool IsExactHour(DateTime value)
        => value.Minute == 0 && value.Second == 0 && value.Millisecond == 0 && value.Ticks % TimeSpan.TicksPerSecond == 0;

    private static object OrderProjection(WorkOrder w, int? sourceType) => new
    {
        w.Id, w.Code, w.Name, w.PlanId, w.PlanLineId, w.ProductId, sourceType,
        productTypeName = w.WorkOrderType == 2 ? "零组件" : "产品",
        w.StartDate, w.EndDate, w.WorkOrderStatus, w.Qty, w.CompletedQty, w.WorkshopId
    };

    private static ApiResult? ValidateRequest(MoveRequest request)
    {
        if (request.ExpectedStart == default || request.ExpectedEnd == default || request.NewStart == default)
            return ApiResult.Fail("请提供原起止时间和新的开始时间");
        if (request.NewStart.Year is < 2000 or > 2100)
            return ApiResult.Fail("排程时间必须在 2000—2100 年之间");
        if (!IsExactHour(request.ExpectedStart) || !IsExactHour(request.ExpectedEnd) || !IsExactHour(request.NewStart))
            return ApiResult.Fail("计划和工单时间只能精确到整小时（分钟、秒须为 00）");
        return null;
    }

    private static bool WithinScheduleRange(DateTime original, TimeSpan delta)
    {
        try
        {
            var shifted = original.Add(delta);
            return shifted.Year is >= 2000 and <= 2100;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
