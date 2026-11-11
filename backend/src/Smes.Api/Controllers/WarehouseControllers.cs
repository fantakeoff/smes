using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Api.Services;
using Smes.Domain.MasterData;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

[ApiController]
[Route("api/stock-bill")]
public class StockBillController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;
    private readonly StockService _stock;

    public StockBillController(SmesDbContext db, CurrentUser me, StockService stock)
    {
        _db = db; _me = me; _stock = stock;
    }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? billType,
        [FromQuery] long? warehouseId, [FromQuery] int? billStatus)
    {
        var query = _db.Set<StockBill>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Name!.Contains(page.Keyword) || x.SourceCode!.Contains(page.Keyword));
        if (billType.HasValue) query = query.Where(x => x.BillType == billType);
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId);
        if (billStatus.HasValue) query = query.Where(x => x.BillStatus == billStatus);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.BillDate).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var bill = await _db.Set<StockBill>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        return bill == null ? ApiResult.Fail("单据不存在", 404) : ApiResult.Ok(bill);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] StockBill bill)
    {
        if (bill.BillType is 2 or 3 || bill.SourceType == "生产工单" || bill.SourceType == "委外加工")
            return ApiResult.Fail("生产领料、工单入库与委外单据必须从对应业务模块创建");
        if (bill.Lines == null || bill.Lines.Count == 0) return ApiResult.Fail("请添加单据明细");
        bill.Id = 0;
        bill.Code = await NumberGenerator.NextAsync(_db, "BILL", _me.Id);
        bill.BillStatus = 0;
        bill.CreatedAt = DateTime.Now;
        bill.CreatedBy = _me.Id;
        bill.OperatorId = _me.Id;
        foreach (var line in bill.Lines)
        {
            line.Id = 0;
            line.BillId = 0;
            line.Amount = line.Price * line.Qty;
        }
        bill.TotalQty = bill.Lines.Sum(x => x.Qty);
        bill.TotalAmount = bill.Lines.Sum(x => x.Amount);
        _db.Add(bill);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(bill, "创建成功");
    }

    [HttpPost("{id:long}/audit")]
    public async Task<ApiResult> Audit(long id)
    {
        var bill = await _db.Set<StockBill>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (bill == null) return ApiResult.Fail("单据不存在", 404);
        if (bill.BillType == 2) return ApiResult.Fail("生产领料必须通过领料任务提交，禁止通用审核");
        if (bill.BillStatus != 0) return ApiResult.Fail("单据已审核或已作废");
        await _stock.ApplyAsync(bill, _me.Id);
        return ApiResult.Ok(new { bill.Id, bill.Code }, "审核成功，库存已更新");
    }

    [HttpPost("{id:long}/void")]
    public async Task<ApiResult> Void(long id)
    {
        var bill = await _db.Set<StockBill>().FirstOrDefaultAsync(x => x.Id == id);
        if (bill == null) return ApiResult.Fail("单据不存在", 404);
        if (bill.BillType == 2) return ApiResult.Fail("生产领料单须在领料模块处理，禁止通用作废");
        if (bill.BillStatus == 1) return ApiResult.Fail("已审核单据不能作废");
        bill.BillStatus = 2;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "已作废");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        var bill = await _db.Set<StockBill>().FirstOrDefaultAsync(x => x.Id == id);
        if (bill == null) return ApiResult.Fail("单据不存在", 404);
        if (bill.BillType == 2) return ApiResult.Fail("生产领料单须在领料模块处理，禁止通用删除");
        if (bill.BillStatus == 1) return ApiResult.Fail("已审核单据不能删除");
        bill.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}

[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private readonly SmesDbContext _db;
    public InventoryController(SmesDbContext db) => _db = db;

    [HttpGet("stock")]
    public async Task<ApiResult> Stock([FromQuery] PageQuery page, [FromQuery] long? warehouseId, [FromQuery] long? productId)
    {
        var query = from s in _db.Set<InventoryStock>().AsNoTracking()
                    join p in _db.Set<Product>().AsNoTracking() on s.ProductId equals p.Id
                    join w in _db.Set<Warehouse>().AsNoTracking() on s.WarehouseId equals w.Id
                    select new
                    {
                        s.Id, s.ProductId, ProductCode = p.Code, ProductName = p.Name, Spec = p.Spec,
                        UnitId = p.UnitId, s.WarehouseId, WarehouseName = w.Name, s.Qty, s.LockedQty,
                        AvailableQty = s.Qty - s.LockedQty, s.AvgCost, Amount = s.Qty * s.AvgCost,
                        s.Location, s.BatchNo, s.LastInAt, SafetyStock = p.SafetyStock
                    };
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.ProductCode!.Contains(page.Keyword) || x.ProductName!.Contains(page.Keyword));
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId);
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId);
        var total = await query.CountAsync();
        var list = await query.OrderBy(x => x.ProductCode).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        var summary = await query.GroupBy(x => 1).Select(g => new { TotalQty = g.Sum(x => x.Qty), TotalAmount = g.Sum(x => x.Amount) }).FirstOrDefaultAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page, summary });
    }

    [HttpGet("transactions")]
    public async Task<ApiResult> Transactions([FromQuery] PageQuery page, [FromQuery] long? productId,
        [FromQuery] long? warehouseId, [FromQuery] int? billType)
    {
        var query = from t in _db.Set<InventoryTransaction>().AsNoTracking()
                    join p in _db.Set<Product>().AsNoTracking() on t.ProductId equals p.Id
                    join w in _db.Set<Warehouse>().AsNoTracking() on t.WarehouseId equals w.Id
                    select new
                    {
                        t.Id, t.BillId, t.BillCode, t.BillType, t.ProductId, ProductCode = p.Code, ProductName = p.Name,
                        t.WarehouseId, WarehouseName = w.Name, t.Direction, t.Qty, t.BeforeQty, t.AfterQty,
                        t.Price, t.OccurredAt, t.OperatorId
                    };
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.ProductCode!.Contains(page.Keyword) || x.ProductName!.Contains(page.Keyword) || x.BillCode!.Contains(page.Keyword));
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId);
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId);
        if (billType.HasValue) query = query.Where(x => x.BillType == billType);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    /// <summary>库存预警：低于安全库存的产品</summary>
    [HttpGet("alert")]
    public async Task<ApiResult> Alert()
    {
        var products = await _db.Set<Product>().AsNoTracking().Where(p => p.SafetyStock > 0).ToListAsync();
        var stockTotals = await _db.Set<InventoryStock>().AsNoTracking()
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Qty) })
            .ToListAsync();
        var list = products.Select(p =>
        {
            var qty = stockTotals.FirstOrDefault(s => s.ProductId == p.Id)?.Qty ?? 0;
            return new
            {
                p.Id, p.Code, p.Name, p.Spec, p.SafetyStock,
                CurrentQty = qty,
                ShortageQty = p.SafetyStock - qty
            };
        }).Where(x => x.CurrentQty < x.SafetyStock)
          .OrderByDescending(x => x.ShortageQty)
          .ToList();
        return ApiResult.Ok(list);
    }
}

[ApiController]
[Route("api/stock-take")]
public class StockTakeController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;
    private readonly StockService _stock;

    public StockTakeController(SmesDbContext db, CurrentUser me, StockService stock)
    {
        _db = db; _me = me; _stock = stock;
    }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page)
    {
        var query = _db.Set<StockTake>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.Code!.Contains(page.Keyword));
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        var ids = list.Select(x => x.Id).ToList();
        var counts = await _db.Set<StockTakeLine>().Where(l => ids.Contains(l.TakeId)).GroupBy(l => l.TakeId)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        return ApiResult.Ok(new
        {
            list = list.Select(t => new
            {
                t.Id, t.Code, t.Name, t.WarehouseId, t.TakeDate, t.TakeStatus, t.TotalDiffQty, t.OperatorId, t.Remark,
                lineCount = counts.FirstOrDefault(c => c.Key == t.Id)?.Count ?? 0
            }),
            total, pageSize = page.PageSize, currentPage = page.Page
        });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var take = await _db.Set<StockTake>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        return take == null ? ApiResult.Fail("盘点单不存在", 404) : ApiResult.Ok(take);
    }

    /// <summary>按仓库当前库存生成盘点单</summary>
    [HttpPost]
    public async Task<ApiResult> Create([FromBody] StockTake input)
    {
        var take = new StockTake
        {
            Code = await NumberGenerator.NextAsync(_db, "TAKE", _me.Id), Name = input.Name ?? "库存盘点",
            WarehouseId = input.WarehouseId, TakeDate = DateTime.Today, TakeStatus = 1,
            Remark = input.Remark, OperatorId = _me.Id, CreatedBy = _me.Id
        };
        var stocks = await _db.Set<InventoryStock>().AsNoTracking()
            .Where(x => x.WarehouseId == input.WarehouseId && x.Qty != 0).ToListAsync();
        if (stocks.Count == 0) return ApiResult.Fail("该仓库没有库存，无法生成盘点单");
        take.Lines = stocks.Select(s => new StockTakeLine
        {
            ProductId = s.ProductId, BookQty = s.Qty, ActualQty = s.Qty, DiffQty = 0, Location = s.Location
        }).ToList();
        _db.Add(take);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(take, "盘点单已生成");
    }

    [HttpPut("{id:long}/lines")]
    public async Task<ApiResult> SaveLines(long id, [FromBody] List<StockTakeLine> lines)
    {
        var take = await _db.Set<StockTake>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (take == null) return ApiResult.Fail("盘点单不存在", 404);
        if (take.TakeStatus == 2) return ApiResult.Fail("盘点已完成");
        foreach (var line in take.Lines)
        {
            var input = lines.FirstOrDefault(x => x.Id == line.Id);
            if (input == null) continue;
            line.ActualQty = input.ActualQty;
            line.DiffQty = input.ActualQty - line.BookQty;
        }
        take.TotalDiffQty = take.Lines.Sum(x => x.DiffQty);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "保存成功");
    }

    [HttpPost("{id:long}/complete")]
    public async Task<ApiResult> Complete(long id)
    {
        var take = await _db.Set<StockTake>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (take == null) return ApiResult.Fail("盘点单不存在", 404);
        if (take.TakeStatus == 2) return ApiResult.Fail("盘点已完成");
        foreach (var line in take.Lines.Where(x => x.DiffQty != 0))
        {
            var stock = await _db.Set<InventoryStock>().FirstOrDefaultAsync(x => x.ProductId == line.ProductId && x.WarehouseId == take.WarehouseId);
            if (stock == null)
            {
                stock = new InventoryStock { ProductId = line.ProductId, WarehouseId = take.WarehouseId };
                _db.Add(stock);
            }
            var before = stock.Qty;
            stock.Qty += line.DiffQty;
            _db.Add(new InventoryTransaction
            {
                BillCode = take.Code, BillType = line.DiffQty > 0 ? 8 : 9, ProductId = line.ProductId,
                WarehouseId = take.WarehouseId, Direction = line.DiffQty > 0 ? 1 : -1,
                Qty = Math.Abs(line.DiffQty), BeforeQty = before, AfterQty = stock.Qty,
                OccurredAt = DateTime.Now, OperatorId = _me.Id
            });
        }
        take.TakeStatus = 2;
        take.TotalDiffQty = take.Lines.Sum(x => x.DiffQty);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "盘点完成，库存已调整");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        var take = await _db.Set<StockTake>().FirstOrDefaultAsync(x => x.Id == id);
        if (take == null) return ApiResult.Fail("盘点单不存在", 404);
        if (take.TakeStatus == 2) return ApiResult.Fail("已完成盘点不能删除");
        take.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}
