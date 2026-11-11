using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Api.Messaging;
using Smes.Api.Services;
using Smes.Domain.MasterData;
using Smes.Domain.Production;
using Smes.Domain.Quality;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

public record SubcontractOrderInput(string? Name, long SupplierId, long InputProductId, long OutputProductId,
    decimal OrderQty, long SourceWarehouseId, long TargetWarehouseId, DateTime OrderDate, DateTime? DueDate,
    string? InputBatchNo, string? InputLocation, string? OutputBatchNo, string? OutputLocation, string? Remark);
public record SubcontractMovementInput(decimal Qty, string? BatchNo, string? Location, string? Remark);

[ApiController]
[Route("api/subcontract")]
public class SubcontractController : ControllerBase
{
    private const string SourceType = SubcontractQualitySource.StockBillSourceType;
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;
    private readonly StockService _stock;

    public SubcontractController(SmesDbContext db, CurrentUser me, StockService stock)
    { _db = db; _me = me; _stock = stock; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? orderStatus, [FromQuery] long? supplierId)
    {
        var query = _db.Set<SubcontractOrder>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword))
            query = query.Where(x => (x.Code ?? "").Contains(page.Keyword) || (x.Name ?? "").Contains(page.Keyword));
        if (orderStatus.HasValue) query = query.Where(x => x.OrderStatus == orderStatus.Value);
        if (supplierId.HasValue) query = query.Where(x => x.SupplierId == supplierId.Value);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var order = await _db.Set<SubcontractOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("委外订单不存在", 404);
        var bills = await _db.Set<StockBill>().AsNoTracking()
            .Where(x => x.SourceType == SourceType && x.SourceId == order.Id)
            .OrderBy(x => x.BillDate).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Code, x.BillType, x.BillDate, x.BillStatus, x.WarehouseId, x.TotalQty, x.SourceCode })
            .ToListAsync();
        return ApiResult.Ok(new { order, bills });
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] SubcontractOrderInput input)
    {
        var validation = await ValidateInput(input);
        if (validation != null) return ApiResult.Fail(validation);
        var order = new SubcontractOrder
        {
            TenantId = _me.TenantId, Code = "SW" + Guid.NewGuid().ToString("N")[..20],
            Name = string.IsNullOrWhiteSpace(input.Name) ? "委外加工订单" : input.Name.Trim(),
            SupplierId = input.SupplierId, InputProductId = input.InputProductId, OutputProductId = input.OutputProductId,
            OrderQty = input.OrderQty, SourceWarehouseId = input.SourceWarehouseId, TargetWarehouseId = input.TargetWarehouseId,
            OrderDate = input.OrderDate, DueDate = input.DueDate, InputBatchNo = input.InputBatchNo,
            InputLocation = input.InputLocation, OutputBatchNo = input.OutputBatchNo, OutputLocation = input.OutputLocation,
            Remark = input.Remark, OrderStatus = 0, CreatedBy = _me.Id
        };
        _db.Add(order);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(order, "委外订单创建成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] SubcontractOrderInput input)
    {
        var order = await _db.Set<SubcontractOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("委外订单不存在", 404);
        if (order.OrderStatus != 0 || order.IssuedQty != 0 || order.ReceivedQty != 0)
            return ApiResult.Fail("仅未发料的草稿订单可修改");
        var validation = await ValidateInput(input);
        if (validation != null) return ApiResult.Fail(validation);
        order.Name = string.IsNullOrWhiteSpace(input.Name) ? "委外加工订单" : input.Name.Trim();
        order.SupplierId = input.SupplierId; order.InputProductId = input.InputProductId;
        order.OutputProductId = input.OutputProductId; order.OrderQty = input.OrderQty;
        order.SourceWarehouseId = input.SourceWarehouseId; order.TargetWarehouseId = input.TargetWarehouseId;
        order.OrderDate = input.OrderDate; order.DueDate = input.DueDate;
        order.InputBatchNo = input.InputBatchNo; order.InputLocation = input.InputLocation;
        order.OutputBatchNo = input.OutputBatchNo; order.OutputLocation = input.OutputLocation;
        order.Remark = input.Remark; order.UpdatedAt = DateTime.Now; order.UpdatedBy = _me.Id;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(order, "委外订单修改成功");
    }

    /// <summary>发出投入物料（其他出库 6）；允许分批发料，累计不得超过订单数。</summary>
    [HttpPost("{id:long}/dispatch")]
    public async Task<ApiResult> Dispatch(long id, [FromBody] SubcontractMovementInput input)
    {
        if (input.Qty <= 0) return ApiResult.Fail("发料数量必须大于 0");
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await _db.Set<SubcontractOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("委外订单不存在", 404);
        if (!HasValidQuantities(order)) return ApiResult.Fail("订单数量台账异常，已停止发料");
        if (order.OrderStatus is 3 or 4) return ApiResult.Fail("已完成或已关闭订单不能发料");
        if (input.Qty > order.OrderQty - order.IssuedQty) return ApiResult.Fail("发料数量超过订单剩余数量");
        var batchNo = input.BatchNo ?? order.InputBatchNo ?? "";
        var location = input.Location ?? order.InputLocation ?? "";
        var available = await _db.Set<InventoryStock>().Where(x => x.ProductId == order.InputProductId &&
            x.WarehouseId == order.SourceWarehouseId && x.BatchNo == batchNo && x.Location == location)
            .Select(x => (decimal?)(x.Qty - x.LockedQty)).FirstOrDefaultAsync() ?? 0;
        if (available < input.Qty) return ApiResult.Fail($"投入物料可用库存不足（批次/库位库存 {available}）");
        var product = await _db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == order.InputProductId);
        var bill = new StockBill
        {
            TenantId = _me.TenantId, Code = "CKSW" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant(),
            BillType = 6, WarehouseId = order.SourceWarehouseId, BillDate = DateTime.Today,
            BillStatus = 0, SourceType = SourceType, SourceId = order.Id, SourceCode = order.Code,
            Name = "委外加工发料", Remark = input.Remark ?? order.Remark, OperatorId = _me.Id, CreatedBy = _me.Id,
            TotalQty = input.Qty
        };
        bill.Lines.Add(new StockBillLine
        {
            TenantId = _me.TenantId, Seq = 1, ProductId = order.InputProductId, Qty = input.Qty,
            UnitId = product?.UnitId, BatchNo = batchNo, Location = location
        });
        _db.Add(bill);
        await _db.SaveChangesAsync();
        try { await _stock.ApplyAsync(bill, _me.Id); }
        catch (InvalidOperationException ex) { return ApiResult.Fail(ex.Message); }
        order.IssuedQty += input.Qty;
        if (!HasValidQuantities(order)) return ApiResult.Fail("发料后订单数量不守恒，操作已撤销");
        order.OrderStatus = order.ReceivedQty > 0 ? 2 : 1;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult.Ok(new { order.Id, billId = bill.Id, bill.Code, order.IssuedQty, order.ReceivedQty }, "委外发料已过账");
    }

    /// <summary>收回加工产出物至待检锁定库存；仅异步合格判定可以放行。</summary>
    [HttpPost("{id:long}/receipt")]
    public async Task<ApiResult> Receipt(long id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] SubcontractMovementInput input, CancellationToken ct)
    {
        if (input.Qty <= 0) return ApiResult.Fail("收货数量必须大于 0");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            return ApiResult.Fail("必须提供有效的 Idempotency-Key（最多 128 个字符）", 400);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var order = await _db.Set<SubcontractOrder>()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == _me.TenantId, ct);
        if (order == null) return ApiResult.Fail("委外订单不存在", 404);

        var batchNo = input.BatchNo ?? order.OutputBatchNo ?? "";
        var location = input.Location ?? order.OutputLocation ?? "";
        if (batchNo.Length > 100 || location.Length > 100)
            return ApiResult.Fail("批次号和库位长度不能超过 100 个字符", 400);

        // A deterministic, tenant/order-scoped bill code persists the idempotency key
        // without adding schema. The unique (TenantId, Code) index is the final race guard.
        var billCode = ReceiptBillCode(_me.TenantId, order.Id, idempotencyKey);
        var previousBill = await _db.Set<StockBill>().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.TenantId == _me.TenantId && x.Code == billCode, ct);
        if (previousBill != null)
        {
            var previousInspection = await _db.Set<QualityInspection>()
                .FirstOrDefaultAsync(x => x.TenantId == _me.TenantId &&
                    x.SourceType == SubcontractQualitySource.InspectionSourceType &&
                    x.SourceId == previousBill.Id && x.SourceCode == previousBill.Code, ct);
            if (!MatchesReceipt(previousBill, order, input, batchNo, location) || previousInspection == null ||
                previousInspection.Qty != input.Qty || previousInspection.ProductId != order.OutputProductId ||
                (previousInspection.BatchNo ?? "") != batchNo)
                return ApiResult.Fail("Idempotency-Key 已用于不同或不完整的收货请求", 409);

            await tx.CommitAsync(ct);
            return ApiResult.Ok(new
            {
                order.Id, billId = previousBill.Id, previousBill.Code, inspectionId = previousInspection.Id,
                order.IssuedQty, order.ReceivedQty, quarantinedQty = previousInspection.Qty,
                qualityStatus = previousInspection.Result == 0 ? "pending" : previousInspection.Result == 1 ? "passed" : "failed"
            }, "重复收货请求已安全返回原结果");
        }

        if (!HasValidQuantities(order)) return ApiResult.Fail("订单数量台账异常，已停止收货");
        if (order.OrderStatus is 0 or 3 or 4) return ApiResult.Fail("当前订单状态不允许收货");
        if (input.Qty > order.IssuedQty - order.ReceivedQty) return ApiResult.Fail("收货数量超过已发料未收货数量");
        var product = await _db.Set<Product>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == order.OutputProductId && x.TenantId == _me.TenantId, ct);
        if (product == null) return ApiResult.Fail("产出物料不存在", 404);
        var price = product?.CostPrice ?? 0;
        var bill = new StockBill
        {
            TenantId = _me.TenantId, Code = billCode,
            BillType = 5, WarehouseId = order.TargetWarehouseId, BillDate = DateTime.Today,
            BillStatus = 0, SourceType = SourceType, SourceId = order.Id, SourceCode = order.Code,
            Name = "委外加工收货", Remark = input.Remark ?? order.Remark, OperatorId = _me.Id, CreatedBy = _me.Id,
            TotalQty = input.Qty, TotalAmount = price * input.Qty
        };
        bill.Lines.Add(new StockBillLine
        {
            TenantId = _me.TenantId, Seq = 1, ProductId = order.OutputProductId, Qty = input.Qty,
            UnitId = product?.UnitId, Price = price, Amount = price * input.Qty,
            BatchNo = batchNo, Location = location
        });
        _db.Add(bill);
        await _db.SaveChangesAsync(ct);
        try { await _stock.ApplyAsync(bill, _me.Id); }
        catch (InvalidOperationException ex) { return ApiResult.Fail(ex.Message); }

        var stock = await _db.Set<InventoryStock>().FirstOrDefaultAsync(x =>
            x.TenantId == _me.TenantId && x.ProductId == order.OutputProductId &&
            x.WarehouseId == order.TargetWarehouseId && x.BatchNo == batchNo && x.Location == location, ct);
        if (stock == null || stock.Qty < input.Qty || stock.LockedQty < 0 || stock.LockedQty > stock.Qty)
            return ApiResult.Fail("收货后库存台账异常，操作已撤销", 409);
        stock.LockedQty += input.Qty;

        var inspection = new QualityInspection
        {
            TenantId = _me.TenantId, Code = "QC" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant(),
            Name = "委外收货待检", InspectionType = 1,
            SourceType = SubcontractQualitySource.InspectionSourceType, SourceId = bill.Id, SourceCode = bill.Code,
            ProductId = order.OutputProductId, BatchNo = batchNo, Qty = input.Qty, SampleQty = input.Qty,
            Result = 0, QualifiedQty = 0, DefectQty = 0, Remark = $"委外订单 {order.Code} 收货待检",
            CreatedBy = _me.Id
        };
        _db.Add(inspection);
        order.ReceivedQty += input.Qty;
        if (!HasValidQuantities(order)) return ApiResult.Fail("收货后订单数量不守恒，操作已撤销");
        order.OrderStatus = order.ReceivedQty == order.OrderQty ? 3 : 2;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ApiResult.Ok(new
        {
            order.Id, billId = bill.Id, bill.Code, inspectionId = inspection.Id,
            order.IssuedQty, order.ReceivedQty, quarantinedQty = input.Qty, qualityStatus = "pending"
        }, "委外收货已隔离入库，等待异步质检放行");
    }

    [HttpPost("{id:long}/close")]
    public async Task<ApiResult> Close(long id)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await _db.Set<SubcontractOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("委外订单不存在", 404);
        if (order.OrderStatus == 3) return ApiResult.Fail("已完成订单不能关闭");
        if (order.OrderStatus == 4) return ApiResult.Ok(order, "订单已关闭");
        if (order.IssuedQty != order.ReceivedQty) return ApiResult.Fail("仍有已发出未收货数量，不能关闭");
        order.OrderStatus = 4;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult.Ok(order, "委外订单已关闭");
    }

    private async Task<string?> ValidateInput(SubcontractOrderInput input)
    {
        if (input.SupplierId <= 0 || input.InputProductId <= 0 || input.OutputProductId <= 0 ||
            input.SourceWarehouseId <= 0 || input.TargetWarehouseId <= 0 || input.OrderQty <= 0)
            return "供应商、物料、仓库必须有效，订单数量必须大于 0";
        if (input.SourceWarehouseId == input.TargetWarehouseId)
            return "发料仓与收货仓必须分别指定";
        if (input.OrderDate == default) return "订单日期无效";
        if (input.DueDate.HasValue && input.DueDate.Value.Date < input.OrderDate.Date)
            return "交期不能早于订单日期";
        if (!await _db.Set<Supplier>().AnyAsync(x => x.Id == input.SupplierId)) return "供应商不存在";
        if (!await _db.Set<Product>().AnyAsync(x => x.Id == input.InputProductId)) return "投入物料不存在";
        if (!await _db.Set<Product>().AnyAsync(x => x.Id == input.OutputProductId)) return "产出物料不存在";
        if (!await _db.Set<Warehouse>().AnyAsync(x => x.Id == input.SourceWarehouseId)) return "发料仓不存在";
        if (!await _db.Set<Warehouse>().AnyAsync(x => x.Id == input.TargetWarehouseId)) return "收货仓不存在";
        return null;
    }

    private static bool HasValidQuantities(SubcontractOrder order) =>
        order.OrderQty > 0 && order.IssuedQty >= 0 && order.ReceivedQty >= 0 &&
        order.ReceivedQty <= order.IssuedQty && order.IssuedQty <= order.OrderQty;

    private static string ReceiptBillCode(long tenantId, long orderId, string idempotencyKey)
    {
        var identity = string.Join(":", tenantId.ToString(CultureInfo.InvariantCulture),
            orderId.ToString(CultureInfo.InvariantCulture), idempotencyKey);
        return "SWQR" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static bool MatchesReceipt(StockBill bill, SubcontractOrder order, SubcontractMovementInput input,
        string batchNo, string location) =>
        bill.BillType == 5 && bill.BillStatus == 1 && bill.WarehouseId == order.TargetWarehouseId &&
        bill.SourceType == SourceType && bill.SourceId == order.Id && bill.SourceCode == order.Code &&
        bill.TotalQty == input.Qty &&
        bill.Lines.Count == 1 && bill.Lines[0].ProductId == order.OutputProductId &&
        bill.Lines[0].Qty == input.Qty && (bill.Lines[0].BatchNo ?? "") == batchNo &&
        (bill.Lines[0].Location ?? "") == location && bill.Remark == (input.Remark ?? order.Remark);
}
