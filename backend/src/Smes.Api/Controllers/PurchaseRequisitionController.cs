using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Domain.Purchase;
using Smes.Domain.Production;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;
using Smes.Api.Services;

namespace Smes.Api.Controllers;

public record RequisitionLineInput(long ProductId, decimal RequiredQty, decimal RequestedQty, long? WorkOrderId, long? DemandLineId, string? Remark);
public record RequisitionInput(DateTime RequestDate, string? Remark, List<RequisitionLineInput> Lines);
public record ConvertRequisitionInput(long SupplierId, DateTime? ExpectedDate, decimal? Price);

[ApiController]
[Route("api/purchase-requisition")]
public class PurchaseRequisitionController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;
    public PurchaseRequisitionController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? approvalStatus)
    {
        var q = _db.Set<PurchaseRequisition>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword)) q = q.Where(x => x.Code!.Contains(page.Keyword) || x.Name!.Contains(page.Keyword));
        if (approvalStatus.HasValue) q = q.Where(x => x.ApprovalStatus == approvalStatus);
        var total = await q.CountAsync();
        var list = await q.OrderByDescending(x => x.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var row = await _db.Set<PurchaseRequisition>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        return row == null ? ApiResult.Fail("采购申请不存在", 404) : ApiResult.Ok(row);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] RequisitionInput input)
    {
        if (input.Lines == null || input.Lines.Count == 0 || input.Lines.Any(x => x.ProductId <= 0 || x.RequiredQty < 0 || x.RequestedQty <= 0 || x.RequestedQty > x.RequiredQty)) return ApiResult.Fail("申请明细无效：申请数量需大于零且不超过需求数量");
        var row = new PurchaseRequisition { Code = await NumberGenerator.NextAsync(_db, "PR", _me.Id), Name = "采购申请", RequestDate = input.RequestDate, Remark = input.Remark, ApprovalStatus = 0, CreatedBy = _me.Id };
        row.Lines = input.Lines.Select((x, i) => new PurchaseRequisitionLine { Seq = i + 1, ProductId = x.ProductId, RequiredQty = x.RequiredQty, RequestedQty = x.RequestedQty, WorkOrderId = x.WorkOrderId, DemandLineId = x.DemandLineId, Remark = x.Remark }).ToList();
        row.TotalQty = row.Lines.Sum(x => x.RequestedQty);
        _db.Add(row); await _db.SaveChangesAsync(); return ApiResult.Ok(row, "采购申请已创建");
    }

    [HttpPost("shortages")]
    public async Task<ApiResult> CreateShortages()
    {
        var materials = await _db.Set<WorkOrderMaterial>().AsNoTracking().Where(x => x.RequiredQty > x.IssuedQty).ToListAsync();
        if (materials.Count == 0) return ApiResult.Fail("当前没有待采购物料需求");
        var stocks = await _db.Set<InventoryStock>().AsNoTracking().GroupBy(x => x.ProductId).Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Qty) }).ToListAsync();
        var openLines = await (from line in _db.Set<PurchaseRequisitionLine>().AsNoTracking()
                               join head in _db.Set<PurchaseRequisition>().AsNoTracking() on line.RequisitionId equals head.Id
                               where head.ApprovalStatus == 0 || head.ApprovalStatus == 1 || head.ApprovalStatus == 2
                               select new { line.ProductId, line.WorkOrderId, line.RequestedQty, line.ConvertedQty }).ToListAsync();
        var lines = new List<PurchaseRequisitionLine>();
        foreach (var m in materials)
        {
            var pending = openLines.Where(x => x.ProductId == m.ProductId && x.WorkOrderId == m.WorkOrderId).Sum(x => Math.Max(0, x.RequestedQty - x.ConvertedQty));
            var available = stocks.FirstOrDefault(x => x.ProductId == m.ProductId)?.Qty ?? 0;
            var shortQty = m.RequiredQty - m.IssuedQty - available - pending;
            if (shortQty > 0) lines.Add(new PurchaseRequisitionLine { ProductId = m.ProductId, RequiredQty = shortQty, RequestedQty = shortQty, WorkOrderId = m.WorkOrderId, DemandLineId = m.Id, Remark = "按工单物料短缺生成" });
        }
        if (lines.Count == 0) return ApiResult.Fail("没有未覆盖的库存短缺");
        var row = new PurchaseRequisition { Code = await NumberGenerator.NextAsync(_db, "PR", _me.Id), Name = "工单短缺采购申请", RequestDate = DateTime.Today, ApprovalStatus = 0, CreatedBy = _me.Id, Lines = lines };
        for (var i = 0; i < lines.Count; i++) lines[i].Seq = i + 1;
        row.TotalQty = lines.Sum(x => x.RequestedQty);
        _db.Add(row); await _db.SaveChangesAsync(); return ApiResult.Ok(row, "已根据工单短缺生成采购申请");
    }

    [HttpPost("{id:long}/submit")]
    public async Task<ApiResult> Submit(long id)
    {
        var row = await _db.Set<PurchaseRequisition>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (row == null) return ApiResult.Fail("采购申请不存在", 404);
        if (row.ApprovalStatus != 0 || row.Lines.Count == 0) return ApiResult.Fail("仅含明细的草稿申请可提交");
        row.ApprovalStatus = 1; row.SubmittedAt = DateTime.Now; await _db.SaveChangesAsync(); return ApiResult.Ok(null, "已提交审批");
    }

    [HttpPost("{id:long}/approve")]
    public async Task<ApiResult> Approve(long id, [FromBody] string? remark)
    {
        var row = await _db.Set<PurchaseRequisition>().FirstOrDefaultAsync(x => x.Id == id);
        if (row == null) return ApiResult.Fail("采购申请不存在", 404);
        if (row.ApprovalStatus != 1) return ApiResult.Fail("只有待审批申请可以批准");
        row.ApprovalStatus = 2; row.ApprovedAt = DateTime.Now; row.ApprovedBy = _me.Id; row.ApprovalRemark = remark; await _db.SaveChangesAsync(); return ApiResult.Ok(null, "采购申请已批准");
    }

    [HttpPost("{id:long}/reject")]
    public async Task<ApiResult> Reject(long id, [FromBody] string? remark)
    {
        var row = await _db.Set<PurchaseRequisition>().FirstOrDefaultAsync(x => x.Id == id);
        if (row == null) return ApiResult.Fail("采购申请不存在", 404);
        if (row.ApprovalStatus != 1) return ApiResult.Fail("只有待审批申请可以驳回");
        row.ApprovalStatus = 3; row.ApprovedAt = DateTime.Now; row.ApprovedBy = _me.Id; row.ApprovalRemark = remark; await _db.SaveChangesAsync(); return ApiResult.Ok(null, "采购申请已驳回");
    }

    [HttpPost("{id:long}/convert")]
    public async Task<ApiResult> Convert(long id, [FromBody] ConvertRequisitionInput input)
    {
        if (input.SupplierId <= 0 || input.Price < 0) return ApiResult.Fail("供应商或单价无效");
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var row = await _db.Set<PurchaseRequisition>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (row == null) return ApiResult.Fail("采购申请不存在", 404);
        if (row.ApprovalStatus != 2) return ApiResult.Fail("只有已批准申请可以转采购订单");
        var open = row.Lines.Where(x => x.RequestedQty > x.ConvertedQty).ToList();
        if (open.Count == 0)
        {
            var existingLineId = row.Lines.Select(x => x.PurchaseOrderLineId).FirstOrDefault(x => x.HasValue);
            var existingLine = existingLineId.HasValue ? await _db.Set<PurchaseOrderLine>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == existingLineId.Value) : null;
            var existingOrder = existingLine == null ? null : await _db.Set<PurchaseOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == existingLine.OrderId);
            return existingOrder == null ? ApiResult.Fail("申请数量已全部转采购订单") : ApiResult.Ok(new { existingOrder.Id, existingOrder.Code }, "已转采购订单");
        }
        var po = new PurchaseOrder { Code = await NumberGenerator.NextAsync(_db, "PO", _me.Id), Name = "采购申请转单", SupplierId = input.SupplierId, OrderDate = DateTime.Today, ExpectedDate = input.ExpectedDate, OrderStatus = 0, CreatedBy = _me.Id };
        po.Lines = open.Select((x, i) => new PurchaseOrderLine { Seq = i + 1, ProductId = x.ProductId, Qty = x.RequestedQty - x.ConvertedQty, Price = input.Price ?? 0, Amount = (x.RequestedQty - x.ConvertedQty) * (input.Price ?? 0), ExpectedDate = input.ExpectedDate, RequisitionLineId = x.Id }).ToList();
        po.TotalQty = po.Lines.Sum(x => x.Qty); po.TotalAmount = po.Lines.Sum(x => x.Amount);
        var supplier = await _db.Set<Smes.Domain.MasterData.Supplier>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.SupplierId);
        if (supplier == null) return ApiResult.Fail("供应商不存在");
        po.Name = supplier.ShortName ?? supplier.Name;
        _db.Add(po); await _db.SaveChangesAsync();
        foreach (var line in open) { line.ConvertedQty = line.RequestedQty; line.PurchaseOrderLineId = po.Lines.First(x => x.RequisitionLineId == line.Id).Id; }
        await _db.SaveChangesAsync(); await tx.CommitAsync(); return ApiResult.Ok(new { po.Id, po.Code }, "已幂等转为采购订单");
    }
}
