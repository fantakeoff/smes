using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Api.Services;
using Smes.Domain.Purchase;
using Smes.Domain.Sales;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

public record OrderLineInput(long? Id, long ProductId, decimal Qty, decimal Price, DateTime? DeliveryDate);

public record OutboundItem(long LineId, decimal Qty, string? BatchNo = null, string? Location = null);

public record SalesOrderInput(long Id, long CustomerId, DateTime OrderDate, DateTime? DeliveryDate, string? PaymentMethod,
    string? Contact, string? Phone, string? Address, string? Remark, List<OrderLineInput> Lines);

[ApiController]
[Route("api/sales-order")]
public class SalesOrderController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;
    private readonly StockService _stock;

    public SalesOrderController(SmesDbContext db, CurrentUser me, StockService stock)
    {
        _db = db; _me = me; _stock = stock;
    }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] long? customerId, [FromQuery] int? orderStatus)
    {
        var query = _db.Set<SalesOrder>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Name!.Contains(page.Keyword));
        if (customerId.HasValue) query = query.Where(x => x.CustomerId == customerId);
        if (orderStatus.HasValue) query = query.Where(x => x.OrderStatus == orderStatus);
        if (page.Status.HasValue) query = query.Where(x => x.Status == page.Status);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        var ids = list.Select(x => x.Id).ToList();
        var lineCounts = await _db.Set<SalesOrderLine>().Where(l => ids.Contains(l.OrderId))
            .GroupBy(l => l.OrderId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        return ApiResult.Ok(new
        {
            list = list.Select(o => new
            {
                o.Id, o.Code, o.Name, o.CustomerId, o.OrderDate, o.DeliveryDate, o.OrderStatus, o.TotalQty,
                o.TotalAmount, o.SalesmanId, o.PaymentMethod, o.Contact, o.Phone, o.Address, o.Remark, o.CreatedAt,
                lineCount = lineCounts.FirstOrDefault(c => c.Key == o.Id)?.Count ?? 0
            }),
            total, pageSize = page.PageSize, currentPage = page.Page
        });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var order = await _db.Set<SalesOrder>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        return order == null ? ApiResult.Fail("订单不存在", 404) : ApiResult.Ok(order);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] SalesOrderInput input)
    {
        if (input.Lines == null || input.Lines.Count == 0) return ApiResult.Fail("请至少添加一行明细");
        var order = new SalesOrder
        {
            Code = await NumberGenerator.NextAsync(_db, "SO", _me.Id),
            CustomerId = input.CustomerId, OrderDate = input.OrderDate, DeliveryDate = input.DeliveryDate,
            PaymentMethod = input.PaymentMethod, Contact = input.Contact, Phone = input.Phone,
            Address = input.Address, Remark = input.Remark, OrderStatus = 0, CreatedBy = _me.Id
        };
        order.Lines = input.Lines.Select((l, i) => new SalesOrderLine
        {
            Seq = i + 1, ProductId = l.ProductId, Qty = l.Qty, Price = l.Price,
            Amount = Decimal.Round(l.Qty * l.Price, 2), DeliveryDate = l.DeliveryDate
        }).ToList();
        order.TotalQty = order.Lines.Sum(x => x.Qty);
        order.TotalAmount = order.Lines.Sum(x => x.Amount);
        var customer = await _db.Set<Smes.Domain.MasterData.Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.CustomerId);
        order.Name = customer?.ShortName ?? customer?.Name;
        _db.Add(order);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(order, "创建成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] SalesOrderInput input)
    {
        var order = await _db.Set<SalesOrder>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("订单不存在", 404);
        if (order.OrderStatus >= 2) return ApiResult.Fail("订单已出库，不能修改");
        order.CustomerId = input.CustomerId;
        order.OrderDate = input.OrderDate;
        order.DeliveryDate = input.DeliveryDate;
        order.PaymentMethod = input.PaymentMethod;
        order.Contact = input.Contact;
        order.Phone = input.Phone;
        order.Address = input.Address;
        order.Remark = input.Remark;
        order.UpdatedAt = DateTime.Now;
        order.UpdatedBy = _me.Id;
        _db.RemoveRange(order.Lines);
        order.Lines = (input.Lines ?? new List<OrderLineInput>()).Select((l, i) => new SalesOrderLine
        {
            OrderId = id, Seq = i + 1, ProductId = l.ProductId, Qty = l.Qty, Price = l.Price,
            Amount = Decimal.Round(l.Qty * l.Price, 2), DeliveryDate = l.DeliveryDate
        }).ToList();
        order.TotalQty = order.Lines.Sum(x => x.Qty);
        order.TotalAmount = order.Lines.Sum(x => x.Amount);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    [HttpPost("{id:long}/confirm")]
    public async Task<ApiResult> Confirm(long id)
    {
        var order = await _db.Set<SalesOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("订单不存在", 404);
        if (order.OrderStatus != 0) return ApiResult.Fail("只有草稿订单可以确认");
        order.OrderStatus = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "订单已确认");
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<ApiResult> Cancel(long id)
    {
        var order = await _db.Set<SalesOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("订单不存在", 404);
        if (order.OrderStatus == 3) return ApiResult.Fail("已完成订单不能取消");
        order.OrderStatus = 4;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "订单已取消");
    }

    public record OutboundRequest(long WarehouseId, string? Remark, List<OutboundItem> Items);

    [HttpPost("{id:long}/outbound")]
    public async Task<ApiResult> Outbound(long id, [FromBody] OutboundRequest request)
    {
        var order = await _db.Set<SalesOrder>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("订单不存在", 404);
        if (order.OrderStatus == 0) return ApiResult.Fail("请先确认订单");
        if (request.Items == null || request.Items.Count == 0) return ApiResult.Fail("请选择出库明细");

        var bill = new StockBill
        {
            Code = await NumberGenerator.NextAsync(_db, "BILL", _me.Id),
            BillType = 4, WarehouseId = request.WarehouseId, BillDate = DateTime.Today,
            BillStatus = 0, SourceType = "销售订单", SourceId = order.Id, SourceCode = order.Code,
            Name = "销售出库", Remark = request.Remark, OperatorId = _me.Id, CreatedBy = _me.Id
        };
        foreach (var item in request.Items)
        {
            var line = order.Lines.FirstOrDefault(x => x.Id == item.LineId);
            if (line == null) continue;
            var product = await _db.Set<Smes.Domain.MasterData.Product>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == line.ProductId);
            bill.Lines.Add(new StockBillLine
            {
                Seq = bill.Lines.Count + 1, ProductId = line.ProductId, Qty = item.Qty,
                UnitId = product?.UnitId, Price = line.Price, Amount = line.Price * item.Qty
            });
            line.DeliveredQty += item.Qty;
        }
        bill.TotalQty = bill.Lines.Sum(x => x.Qty);
        bill.TotalAmount = bill.Lines.Sum(x => x.Amount);
        _db.Add(bill);
        await _db.SaveChangesAsync();
        await _stock.ApplyAsync(bill, _me.Id);

        order.OrderStatus = order.Lines.All(x => x.DeliveredQty >= x.Qty) ? 3 : 2;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(new { billId = bill.Id, bill.Code }, "出库成功");
    }
}

public record PurchaseOrderInput(long Id, long SupplierId, DateTime OrderDate, DateTime? ExpectedDate,
    string? PaymentMethod, string? Remark, List<OrderLineInput> Lines);

[ApiController]
[Route("api/purchase-order")]
public class PurchaseOrderController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;
    private readonly StockService _stock;

    public PurchaseOrderController(SmesDbContext db, CurrentUser me, StockService stock)
    {
        _db = db; _me = me; _stock = stock;
    }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] long? supplierId, [FromQuery] int? orderStatus)
    {
        var query = _db.Set<PurchaseOrder>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Name!.Contains(page.Keyword));
        if (supplierId.HasValue) query = query.Where(x => x.SupplierId == supplierId);
        if (orderStatus.HasValue) query = query.Where(x => x.OrderStatus == orderStatus);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var order = await _db.Set<PurchaseOrder>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        return order == null ? ApiResult.Fail("订单不存在", 404) : ApiResult.Ok(order);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] PurchaseOrderInput input)
    {
        if (input.Lines == null || input.Lines.Count == 0 || input.Lines.Any(x => x.ProductId <= 0 || x.Qty <= 0 || x.Price < 0)) return ApiResult.Fail("请提交有效的产品、正数量和非负单价");
        var validSupplier = await _db.Set<Smes.Domain.MasterData.Supplier>().AnyAsync(x => x.Id == input.SupplierId);
        if (!validSupplier) return ApiResult.Fail("供应商不存在");
        var order = new PurchaseOrder
        {
            Code = await NumberGenerator.NextAsync(_db, "PO", _me.Id), SupplierId = input.SupplierId,
            OrderDate = input.OrderDate, ExpectedDate = input.ExpectedDate, PaymentMethod = input.PaymentMethod,
            Remark = input.Remark, OrderStatus = 0, CreatedBy = _me.Id
        };
        order.Lines = input.Lines.Select((l, i) => new PurchaseOrderLine
        {
            Seq = i + 1, ProductId = l.ProductId, Qty = l.Qty, Price = l.Price,
            Amount = Decimal.Round(l.Qty * l.Price, 2), ExpectedDate = l.DeliveryDate
        }).ToList();
        order.TotalQty = order.Lines.Sum(x => x.Qty);
        order.TotalAmount = order.Lines.Sum(x => x.Amount);
        var supplier = await _db.Set<Smes.Domain.MasterData.Supplier>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.SupplierId);
        order.Name = supplier?.ShortName ?? supplier?.Name;
        _db.Add(order);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(order, "创建成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] PurchaseOrderInput input)
    {
        var order = await _db.Set<PurchaseOrder>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("订单不存在", 404);
        if (order.OrderStatus != 0) return ApiResult.Fail("仅草稿采购订单可以修改");
        if (input.Lines == null || input.Lines.Count == 0 || input.Lines.Any(x => x.ProductId <= 0 || x.Qty <= 0 || x.Price < 0)) return ApiResult.Fail("请提交有效的产品、正数量和非负单价");
        if (order.Lines.Any(x => x.RequisitionLineId.HasValue)) return ApiResult.Fail("由采购申请转换的订单不可整体替换明细");
        if (!await _db.Set<Smes.Domain.MasterData.Supplier>().AnyAsync(x => x.Id == input.SupplierId)) return ApiResult.Fail("供应商不存在");
        order.SupplierId = input.SupplierId;
        order.OrderDate = input.OrderDate;
        order.ExpectedDate = input.ExpectedDate;
        order.PaymentMethod = input.PaymentMethod;
        order.Remark = input.Remark;
        _db.RemoveRange(order.Lines);
        order.Lines = (input.Lines ?? new List<OrderLineInput>()).Select((l, i) => new PurchaseOrderLine
        {
            OrderId = id, Seq = i + 1, ProductId = l.ProductId, Qty = l.Qty, Price = l.Price,
            Amount = Decimal.Round(l.Qty * l.Price, 2), ExpectedDate = l.DeliveryDate
        }).ToList();
        order.TotalQty = order.Lines.Sum(x => x.Qty);
        order.TotalAmount = order.Lines.Sum(x => x.Amount);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    [HttpPost("{id:long}/confirm")]
    public async Task<ApiResult> Confirm(long id)
    {
        var order = await _db.Set<PurchaseOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("订单不存在", 404);
        if (order.OrderStatus != 0) return ApiResult.Fail("只有草稿订单可以确认");
        order.OrderStatus = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "订单已确认");
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<ApiResult> Cancel(long id)
    {
        var order = await _db.Set<PurchaseOrder>().FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("订单不存在", 404);
        if (order.OrderStatus is not (0 or 1)) return ApiResult.Fail("仅草稿或已确认且未收货的采购订单可以取消");
        order.OrderStatus = 4;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "订单已取消");
    }

    public record ReceiveRequest(long WarehouseId, string? Remark, List<OutboundItem> Items);

    [HttpPost("{id:long}/receive")]
    public async Task<ApiResult> Receive(long id, [FromBody] ReceiveRequest request)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await _db.Set<PurchaseOrder>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (order == null) return ApiResult.Fail("订单不存在", 404);
        if (order.OrderStatus is not (1 or 2)) return ApiResult.Fail("只有已确认或部分收货订单可以收货");
        if (request.Items == null || request.Items.Count == 0) return ApiResult.Fail("请选择收货明细");
        if (request.WarehouseId <= 0 || request.Items.Any(x => x.Qty <= 0) || request.Items.GroupBy(x => x.LineId).Any(g => g.Count() > 1))
            return ApiResult.Fail("仓库无效，收货数量必须大于零且同一行不能重复提交");
        foreach (var item in request.Items)
        {
            var line = order.Lines.FirstOrDefault(x => x.Id == item.LineId);
            if (line == null) return ApiResult.Fail("收货明细不属于该采购订单");
            if (line.ReceivedQty + item.Qty > line.Qty) return ApiResult.Fail($"产品 {line.ProductId} 收货数量超出订单未收数量");
        }

        var bill = new StockBill
        {
            Code = await NumberGenerator.NextAsync(_db, "BILL", _me.Id), BillType = 1,
            WarehouseId = request.WarehouseId, BillDate = DateTime.Today, BillStatus = 0,
            SourceType = "采购订单", SourceId = order.Id, SourceCode = order.Code, Name = "采购入库",
            Remark = request.Remark, OperatorId = _me.Id, CreatedBy = _me.Id
        };
        foreach (var item in request.Items)
        {
            var line = order.Lines.First(x => x.Id == item.LineId);
            var product = await _db.Set<Smes.Domain.MasterData.Product>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == line.ProductId);
            bill.Lines.Add(new StockBillLine
            {
                Seq = bill.Lines.Count + 1, ProductId = line.ProductId, Qty = item.Qty, UnitId = product?.UnitId,
                Price = line.Price, Amount = line.Price * item.Qty, BatchNo = item.BatchNo, Location = item.Location
            });
            line.ReceivedQty += item.Qty;
        }
        bill.TotalQty = bill.Lines.Sum(x => x.Qty);
        bill.TotalAmount = bill.Lines.Sum(x => x.Amount);
        _db.Add(bill);
        await _db.SaveChangesAsync();
        try
        {
            await _stock.ApplyAsync(bill, _me.Id);
            order.OrderStatus = order.Lines.All(x => x.ReceivedQty >= x.Qty) ? 3 : 2;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return ApiResult.Ok(new { billId = bill.Id, bill.Code }, "收货成功");
        }
        catch (InvalidOperationException ex)
        {
            await tx.RollbackAsync();
            return ApiResult.Fail(ex.Message);
        }
    }
}
