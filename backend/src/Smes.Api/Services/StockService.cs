using System.Data;
using Microsoft.EntityFrameworkCore;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;

namespace Smes.Api.Services;

/// <summary>库存过账：严格校验单据后，在单一事务内更新余额和流水。</summary>
public class StockService
{
    private readonly SmesDbContext _db;
    public StockService(SmesDbContext db) => _db = db;

    public static bool IsInbound(int billType) => billType is 1 or 3 or 5 or 8 or 11;
    private static bool IsOutbound(int billType) => billType is 2 or 4 or 6 or 9 or 10;

    public async Task ApplyAsync(StockBill bill, long operatorId)
    {
        if (bill.BillStatus != 0) throw new InvalidOperationException("只有草稿单据可以过账");
        if (bill.BillType != 7 && !IsInbound(bill.BillType) && !IsOutbound(bill.BillType))
            throw new InvalidOperationException("不支持的库存单据类型");
        if (bill.Lines == null || bill.Lines.Count == 0) throw new InvalidOperationException("单据必须包含明细");
        if (bill.BillType == 7 && (!bill.ToWarehouseId.HasValue || bill.ToWarehouseId.Value <= 0 || bill.ToWarehouseId == bill.WarehouseId))
            throw new InvalidOperationException("调拨必须指定不同的目标仓库");
        if (bill.WarehouseId <= 0 || bill.Lines.Any(x => x.ProductId <= 0 || x.Qty <= 0 || x.Price < 0))
            throw new InvalidOperationException("仓库、产品必须有效，数量必须大于零且单价不能为负数");

        await using var tx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        if (bill.Id > 0 && await _db.Set<InventoryTransaction>().AnyAsync(x => x.BillId == bill.Id))
            throw new InvalidOperationException("该单据已有库存流水，拒绝重复过账");
        foreach (var line in bill.Lines)
        {
            if (bill.BillType == 7)
            {
                await MoveAsync(bill.WarehouseId, -1, bill, line, operatorId);
                await MoveAsync(bill.ToWarehouseId!.Value, 1, bill, line, operatorId);
            }
            else await MoveAsync(bill.WarehouseId, IsInbound(bill.BillType) ? 1 : -1, bill, line, operatorId);
        }
        bill.BillStatus = 1;
        bill.AuditAt = DateTime.Now;
        bill.AuditBy = operatorId;
        await _db.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
    }

    private async Task MoveAsync(long warehouseId, int direction, StockBill bill, StockBillLine line, long operatorId)
    {
        var batchNo = line.BatchNo ?? "";
        var location = line.Location ?? "";
        line.BatchNo = batchNo;
        line.Location = location;
        var stock = await _db.Set<InventoryStock>().FirstOrDefaultAsync(x => x.ProductId == line.ProductId &&
            x.WarehouseId == warehouseId && x.BatchNo == batchNo && x.Location == location);
        if (stock == null)
        {
            if (direction < 0) throw new InvalidOperationException($"产品 {line.ProductId} 在仓库 {warehouseId} 指定批次/库位库存不足");
            stock = new InventoryStock { ProductId = line.ProductId, WarehouseId = warehouseId, BatchNo = batchNo, Location = location };
            _db.Add(stock);
        }
        if (direction < 0 && stock.Qty - stock.LockedQty < line.Qty)
            throw new InvalidOperationException($"产品 {line.ProductId} 可用库存不足，禁止负库存");
        var before = stock.Qty;
        var after = before + direction * line.Qty;
        if (after < 0) throw new InvalidOperationException($"产品 {line.ProductId} 库存不足，禁止负库存");
        stock.Qty = after;
        if (direction > 0)
        {
            stock.LastInAt = DateTime.Now;
            if (line.Price > 0 && after > 0) stock.AvgCost = ((before * stock.AvgCost) + (line.Qty * line.Price)) / after;
        }
        var idem = $"{bill.Id}:{line.Id}:{warehouseId}:{direction}";
        if (await _db.Set<InventoryTransaction>().AnyAsync(x => x.IdempotencyKey == idem))
            throw new InvalidOperationException("库存流水幂等键冲突，拒绝重复过账");
        _db.Add(new InventoryTransaction
        {
            BillId = bill.Id, BillCode = bill.Code, BillType = bill.BillType, ProductId = line.ProductId,
            WarehouseId = warehouseId, BatchNo = batchNo, Location = location, IdempotencyKey = idem,
            Direction = direction, Qty = line.Qty, BeforeQty = before, AfterQty = after, Price = line.Price,
            OccurredAt = DateTime.Now, OperatorId = operatorId
        });
    }
}
