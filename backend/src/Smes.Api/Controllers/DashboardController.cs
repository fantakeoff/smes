using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Domain.Equipment;
using Smes.Domain.MasterData;
using Smes.Domain.Production;
using Smes.Domain.Quality;
using Smes.Domain.Sales;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly SmesDbContext _db;
    public DashboardController(SmesDbContext db) => _db = db;

    [HttpGet("overview")]
    public async Task<ApiResult> Overview()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var todayWorkOrders = await _db.Set<WorkOrder>().CountAsync(x => x.CreatedAt >= today);
        var inProgress = await _db.Set<WorkOrder>().CountAsync(x => x.WorkOrderStatus == 2);
        var totalWorkOrders = await _db.Set<WorkOrder>().CountAsync();
        var reportsToday = await _db.Set<WorkReport>().Where(x => x.ReportTime >= today).ToListAsync();
        var plannedToday = await _db.Set<WorkOrder>().Where(x => x.StartDate == today).SumAsync(x => (decimal?)x.Qty) ?? 0;
        var completedToday = reportsToday.Sum(x => x.Qty);
        var completionRate = plannedToday > 0 ? Math.Round(completedToday / plannedToday * 100, 1) : 0;

        var equipments = await _db.Set<Equipment>().ToListAsync();
        var running = equipments.Count(x => x.EquipmentStatus == 1);
        var utilization = equipments.Count > 0 ? Math.Round((decimal)running / equipments.Count * 100, 1) : 0;

        var todayOrders = await _db.Set<SalesOrder>().Where(x => x.OrderDate == today).ToListAsync();
        var monthOrders = await _db.Set<SalesOrder>().Where(x => x.OrderDate >= monthStart).ToListAsync();
        var stockValue = await _db.Set<InventoryStock>().SumAsync(x => (decimal?)(x.Qty * x.AvgCost)) ?? 0;

        var qualified = reportsToday.Sum(x => x.QualifiedQty);
        var defect = reportsToday.Sum(x => x.DefectQty);

        return ApiResult.Ok(new
        {
            todayWorkOrders,
            inProgress,
            totalWorkOrders,
            completionRate,
            equipmentUtilization = utilization,
            equipmentRunning = running,
            equipmentTotal = equipments.Count,
            todayOrderAmount = todayOrders.Sum(x => x.TotalAmount),
            todayOrderQty = todayOrders.Sum(x => x.TotalQty),
            monthOrderAmount = monthOrders.Sum(x => x.TotalAmount),
            monthOrderQty = monthOrders.Sum(x => x.TotalQty),
            todayOutput = completedToday,
            todayQualified = qualified,
            todayDefect = defect,
            todayYield = completedToday > 0 ? Math.Round(qualified / completedToday * 100, 1) : 0,
            stockValue,
            productCount = await _db.Set<Product>().CountAsync(),
            customerCount = await _db.Set<Customer>().CountAsync()
        });
    }

    [HttpGet("recent-work-orders")]
    public async Task<ApiResult> RecentWorkOrders([FromQuery] int limit = 5)
    {
        var list = await (from wo in _db.Set<WorkOrder>().AsNoTracking()
                          join p in _db.Set<Product>().AsNoTracking() on wo.ProductId equals p.Id
                          orderby wo.CreatedAt descending
                          select new
                          {
                              wo.Id, wo.Code, wo.Name, ProductCode = p.Code, ProductName = p.Name, p.Spec,
                              wo.Qty, wo.CompletedQty, wo.WorkOrderStatus, wo.StartDate, wo.EndDate, wo.Priority
                          }).Take(limit).ToListAsync();
        return ApiResult.Ok(list);
    }

    [HttpGet("defect-rank")]
    public async Task<ApiResult> DefectRank([FromQuery] int days = 30, [FromQuery] int limit = 5)
    {
        var start = DateTime.Today.AddDays(-days);
        var groups = await _db.Set<WorkReport>().AsNoTracking()
            .Where(x => x.ReportTime >= start && x.DefectQty > 0 && x.DefectItemId != null)
            .GroupBy(x => x.DefectItemId)
            .Select(g => new { DefectItemId = g.Key, Qty = g.Sum(x => x.DefectQty) })
            .OrderByDescending(x => x.Qty).Take(limit).ToListAsync();
        var items = await _db.Set<DefectItem>().AsNoTracking().ToListAsync();
        var total = groups.Sum(x => x.Qty);
        var list = groups.Select(g => new
        {
            Id = g.DefectItemId,
            Name = items.FirstOrDefault(i => i.Id == g.DefectItemId)?.Name ?? "其他",
            Code = items.FirstOrDefault(i => i.Id == g.DefectItemId)?.Code,
            Qty = g.Qty,
            Percent = total > 0 ? Math.Round(g.Qty / total * 100, 1) : 0
        });
        return ApiResult.Ok(list);
    }

    [HttpGet("equipment-alarm")]
    public async Task<ApiResult> EquipmentAlarm()
    {
        var alarms = await _db.Set<EquipmentAlarm>().AsNoTracking().ToListAsync();
        var equipments = await _db.Set<Equipment>().AsNoTracking().ToListAsync();
        var top = alarms.Where(x => x.OccurredAt >= DateTime.Today.AddDays(-7))
            .GroupBy(x => x.EquipmentId)
            .Select(g => new
            {
                EquipmentId = g.Key,
                Name = equipments.FirstOrDefault(e => e.Id == g.Key)?.Name,
                Count = g.Count()
            }).OrderByDescending(x => x.Count).Take(5).ToList();
        return ApiResult.Ok(new
        {
            total = alarms.Count,
            byType = new { over = alarms.Count(x => x.AlarmType == 1), under = alarms.Count(x => x.AlarmType == 2), offline = alarms.Count(x => x.AlarmType == 3), fault = alarms.Count(x => x.AlarmType == 4) },
            byStatus = new { active = alarms.Count(x => x.AlarmStatus == 0), recovered = alarms.Count(x => x.AlarmStatus == 1), confirmed = alarms.Count(x => x.AlarmStatus == 2), closed = alarms.Count(x => x.AlarmStatus == 3) },
            top
        });
    }

    [HttpGet("production-trend")]
    public async Task<ApiResult> ProductionTrend([FromQuery] int days = 7)
    {
        var start = DateTime.Today.AddDays(-days + 1);
        var reports = await _db.Set<WorkReport>().AsNoTracking().Where(x => x.ReportTime >= start).ToListAsync();
        var list = Enumerable.Range(0, days).Select(i =>
        {
            var day = start.AddDays(i);
            var dayReports = reports.Where(x => x.ReportTime.Date == day).ToList();
            return new
            {
                date = day.ToString("MM-dd"),
                qty = dayReports.Sum(x => x.Qty),
                qualified = dayReports.Sum(x => x.QualifiedQty),
                defect = dayReports.Sum(x => x.DefectQty)
            };
        });
        return ApiResult.Ok(list);
    }

    [HttpGet("work-order-status")]
    public async Task<ApiResult> WorkOrderStatus()
    {
        var list = await _db.Set<WorkOrder>().AsNoTracking().GroupBy(x => x.WorkOrderStatus)
            .Select(g => new { status = g.Key, count = g.Count() }).ToListAsync();
        return ApiResult.Ok(list);
    }

    [HttpGet("order-trend")]
    public async Task<ApiResult> OrderTrend([FromQuery] int days = 14)
    {
        var start = DateTime.Today.AddDays(-days + 1);
        var orders = await _db.Set<SalesOrder>().AsNoTracking().Where(x => x.OrderDate >= start).ToListAsync();
        var list = Enumerable.Range(0, days).Select(i =>
        {
            var day = start.AddDays(i);
            var dayOrders = orders.Where(x => x.OrderDate.Date == day).ToList();
            return new { date = day.ToString("MM-dd"), amount = dayOrders.Sum(x => x.TotalAmount), count = dayOrders.Count };
        });
        return ApiResult.Ok(list);
    }

    [HttpGet("quality-summary")]
    public async Task<ApiResult> QualitySummary()
    {
        var inspects = await _db.Set<QualityInspection>().AsNoTracking().ToListAsync();
        var total = inspects.Count;
        var passed = inspects.Count(x => x.Result == 1);
        return ApiResult.Ok(new
        {
            total,
            pending = inspects.Count(x => x.Result == 0),
            passed,
            failed = inspects.Count(x => x.Result == 2),
            concession = inspects.Count(x => x.Result == 3),
            passRate = total > 0 ? Math.Round((decimal)passed / total * 100, 1) : 0,
            byType = new[]
            {
                new { type = "来料检验", count = inspects.Count(x => x.InspectionType == 1), passed = inspects.Count(x => x.InspectionType == 1 && x.Result == 1) },
                new { type = "过程检验", count = inspects.Count(x => x.InspectionType == 2), passed = inspects.Count(x => x.InspectionType == 2 && x.Result == 1) },
                new { type = "出货检验", count = inspects.Count(x => x.InspectionType == 3), passed = inspects.Count(x => x.InspectionType == 3 && x.Result == 1) }
            }
        });
    }
}
