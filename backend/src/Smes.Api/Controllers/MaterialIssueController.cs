using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Api.Messaging;
using Smes.Domain.Production;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

public sealed record MaterialIssueItem(long MaterialId, decimal Qty, string? BatchNo, string? Location);
public sealed record MaterialIssueDraftRequest(long WarehouseId, List<MaterialIssueItem> Items, string? Remark);

[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
[Route("api")]
public sealed class MaterialIssueController(SmesDbContext db, CurrentUser me) : ControllerBase
{
    [HttpPost("work-order/{id:long}/issue")]
    public async Task<ApiResult> CreateDraft(long id, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] MaterialIssueDraftRequest request)
    {
        if (request.WarehouseId <= 0 || request.Items == null || request.Items.Count == 0)
            return ApiResult.Fail("请选择仓库并至少选择一项领料数量");
        if (request.Items.Any(x => x.MaterialId <= 0 || x.Qty <= 0)) return ApiResult.Fail("物料及领料数量必须有效且大于 0");
        if (request.Items.GroupBy(x => x.MaterialId).Any(g => g.Count() > 1)) return ApiResult.Fail("同一工单物料请合并为一行提交");
        if (!string.IsNullOrWhiteSpace(idempotencyKey) && (idempotencyKey.Length > 128 || idempotencyKey.Any(char.IsControl)))
            return ApiResult.Fail("Idempotency-Key 无效（最多 128 个字符）");
        if (!await db.Set<Smes.Domain.MasterData.Warehouse>().AnyAsync(x => x.Id == request.WarehouseId))
            return ApiResult.Fail("领料仓库不存在", 404);

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var wo = await db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == me.TenantId);
        if (wo == null) return ApiResult.Fail("工单不存在", 404);
        if (wo.WorkOrderStatus is 0 or 3 or 4) return ApiResult.Fail("当前工单状态不允许领料");
        var materials = await db.Set<WorkOrderMaterial>().Where(x => x.WorkOrderId == id && x.TenantId == me.TenantId).ToListAsync();
        if (materials.Count == 0) return ApiResult.Fail("该工单没有用料清单，请先维护 BOM");
        var materialIds = request.Items.Select(x => x.MaterialId).ToHashSet();
        if (materials.Any(m => materialIds.Contains(m.Id)) == false || request.Items.Any(i => materials.All(m => m.Id != i.MaterialId)))
            return ApiResult.Fail("所选物料不属于该工单");

        var billCode = string.IsNullOrWhiteSpace(idempotencyKey)
            ? "LL" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant()
            : "LL" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{me.TenantId}:{idempotencyKey}")))[..20];
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingBill = await db.Set<StockBill>().Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.TenantId == me.TenantId && x.BillType == 2 && x.Code == billCode);
            if (existingBill != null)
            {
                var lineIds = existingBill.Lines.Select(x => x.Id).ToList();
                var existingLinks = await (from link in db.Set<MaterialIssueLineLink>()
                    join line in db.Set<StockBillLine>() on link.BillLineId equals line.Id
                    where lineIds.Contains(line.Id) && link.TenantId == me.TenantId
                    select new { link.WorkOrderMaterialId, LinkQty = link.Qty, line.Id, line.ProductId,
                        LineQty = line.Qty, line.BatchNo, line.Location }).ToListAsync();
                var samePayload = existingBill.SourceId == id && existingBill.WarehouseId == request.WarehouseId &&
                    string.Equals(existingBill.Remark, request.Remark, StringComparison.Ordinal) &&
                    existingBill.Lines.Count == request.Items.Count && existingLinks.Count == request.Items.Count &&
                    request.Items.All(item =>
                    {
                        var material = materials.SingleOrDefault(x => x.Id == item.MaterialId);
                        var link = existingLinks.SingleOrDefault(x => x.WorkOrderMaterialId == item.MaterialId);
                        return material != null && link != null && link.ProductId == material.ProductId &&
                            link.LinkQty == item.Qty && link.LineQty == item.Qty &&
                            string.Equals(link.BatchNo ?? "", item.BatchNo ?? "", StringComparison.Ordinal) &&
                            string.Equals(link.Location ?? "", item.Location ?? "", StringComparison.Ordinal);
                    });
                if (!samePayload) return ApiResult.Fail("Idempotency-Key 已用于不同的领料草稿数据", 409);

                var existingJob = await db.Set<MaterialIssueJob>()
                    .FirstOrDefaultAsync(x => x.TenantId == me.TenantId && x.BillId == existingBill.Id);
                var existingStatus = existingJob != null ? Status(existingJob.JobStatus) :
                    existingBill.BillStatus == 2 ? "voided" : existingBill.BillStatus == 1 ? "completed" : "draft";
                var hints = existingJob == null && existingBill.BillStatus == 0
                    ? await GetShortageHintsAsync(existingBill.WarehouseId, existingBill.Lines)
                    : new List<MaterialIssueShortageHint>();
                await tx.CommitAsync();
                return ApiResult.Ok(new { billId = existingBill.Id, existingBill.Code, status = existingStatus,
                    jobId = existingJob?.Id, shortageHints = hints }, "已返回该幂等键对应的领料单");
            }
        }

        if (wo.WorkOrderStatus is 0 or 3 or 4) return ApiResult.Fail("当前工单状态不允许领料");
        // Draft and queued bills reserve quantities but never mutate IssuedQty.
        var activeReservations = await (from link in db.Set<MaterialIssueLineLink>()
            join line in db.Set<StockBillLine>() on link.BillLineId equals line.Id
            join reservedBill in db.Set<StockBill>() on line.BillId equals reservedBill.Id
            where reservedBill.SourceId == id && reservedBill.BillType == 2 && reservedBill.BillStatus == 0 && reservedBill.TenantId == me.TenantId
            select new { link.WorkOrderMaterialId, link.Qty }).ToListAsync();
        var reserved = activeReservations.GroupBy(x => x.WorkOrderMaterialId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        foreach (var item in request.Items)
        {
            var material = materials.Single(x => x.Id == item.MaterialId);
            var pending = Math.Max(0, material.RequiredQty - material.IssuedQty - reserved.GetValueOrDefault(material.Id));
            if (item.Qty > pending) return ApiResult.Fail($"物料 {material.ProductId} 本次数量超过可领数量 {pending}");
        }

        var bill = new StockBill
        {
            TenantId = me.TenantId, Code = billCode,
            BillType = 2, WarehouseId = request.WarehouseId, BillDate = DateTime.Today, BillStatus = 0,
            SourceType = "生产工单", SourceId = wo.Id, SourceCode = wo.Code, Name = "生产领料草稿",
            Remark = request.Remark, OperatorId = me.Id, CreatedBy = me.Id
        };
        foreach (var item in request.Items)
        {
            var material = materials.Single(x => x.Id == item.MaterialId);
            bill.Lines.Add(new StockBillLine { Seq = bill.Lines.Count + 1, ProductId = material.ProductId,
                Qty = item.Qty, UnitId = material.UnitId, BatchNo = item.BatchNo, Location = item.Location });
        }
        bill.TotalQty = bill.Lines.Sum(x => x.Qty);
        db.Add(bill);
        await db.SaveChangesAsync();
        foreach (var pair in request.Items.Select((item, index) => (item, index)))
            db.Add(new MaterialIssueLineLink { TenantId = me.TenantId, BillLineId = bill.Lines[pair.index].Id,
                WorkOrderMaterialId = pair.item.MaterialId, Qty = pair.item.Qty, CreatedBy = me.Id });
        await db.SaveChangesAsync();

        var shortage = await GetShortageHintsAsync(request.WarehouseId, bill.Lines);
        await tx.CommitAsync();
        return ApiResult.Ok(new { billId = bill.Id, bill.Code, status = "draft", shortageHints = shortage }, "领料草稿已生成，尚未扣减库存");
    }

    [HttpPost("material-issue/{billId:long}/submit")]
    public async Task<ApiResult> Submit(long billId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var bill = await db.Set<StockBill>().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == billId && x.TenantId == me.TenantId && x.BillType == 2);
        if (bill == null) return ApiResult.Fail("领料单不存在", 404);
        var payload = JsonSerializer.Serialize(new { bill.Id, bill.WarehouseId, lines = bill.Lines.OrderBy(x => x.Seq).Select(x => new { x.Id, x.ProductId, x.Qty, x.BatchNo, x.Location }) });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var key = $"material-issue:{me.TenantId}:{bill.Id}";
        var job = await db.Set<MaterialIssueJob>().FirstOrDefaultAsync(x => x.TenantId == me.TenantId && x.IdempotencyKey == key);
        if (job != null)
        {
            if (job.PayloadHash != hash) return ApiResult.Fail("幂等键对应的领料单内容已变化，拒绝重复提交");
            await tx.CommitAsync();
            return ApiResult.Ok(new { jobId = job.Id, status = Status(job.JobStatus), billId }, "领料任务已存在");
        }
        if (bill.BillStatus != 0) return ApiResult.Fail("只有未过账的领料草稿可以提交");
        if (bill.Lines.Count == 0) return ApiResult.Fail("领料草稿没有明细");
        var requirements = bill.Lines.GroupBy(x => new { x.ProductId, BatchNo = x.BatchNo ?? "", Location = x.Location ?? "" })
            .Select(g => new { g.Key.ProductId, g.Key.BatchNo, g.Key.Location, Qty = g.Sum(x => x.Qty) }).ToList();
        foreach (var required in requirements)
        {
            var available = await db.Set<InventoryStock>().Where(x => x.WarehouseId == bill.WarehouseId &&
                x.ProductId == required.ProductId && x.BatchNo == required.BatchNo && x.Location == required.Location)
                .Select(x => (decimal?)(x.Qty - x.LockedQty)).FirstOrDefaultAsync() ?? 0;
            if (available < required.Qty)
                return ApiResult.Fail($"产品 {required.ProductId} 指定批次/库位库存不足（可用 {available}，需 {required.Qty}），请先采购、调拨或委外补货", 409);
        }
        job = new MaterialIssueJob { TenantId = me.TenantId, BillId = bill.Id, WorkOrderId = bill.SourceId ?? 0,
            IdempotencyKey = key, PayloadHash = hash, JobStatus = 0, Name = "生产领料", CreatedBy = me.Id };
        db.Add(job);
        await db.SaveChangesAsync();
        Enqueue(db, job, me.TenantId, me.Id);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult.Ok(new { jobId = job.Id, status = "queued", billId }, "领料任务已提交，等待库存过账");
    }

    [HttpPost("material-issue/{billId:long}/void")]
    public async Task<ApiResult> VoidDraft(long billId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var bill = await db.Set<StockBill>().FirstOrDefaultAsync(x => x.Id == billId && x.TenantId == me.TenantId && x.BillType == 2);
        if (bill == null) return ApiResult.Fail("领料单不存在", 404);
        if (bill.BillStatus != 0) return ApiResult.Fail("只有未过账的草稿可以作废", 409);
        var job = await db.Set<MaterialIssueJob>().FirstOrDefaultAsync(x => x.BillId == billId && x.TenantId == me.TenantId);
        if (job != null && job.JobStatus != 3) return ApiResult.Fail("任务已提交或正在消费，不能作废", 409);
        bill.BillStatus = 2;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult.Ok(new { billId }, "领料草稿已作废，预留需求已释放");
    }

    [HttpGet("material-issue/jobs/{id:long}")]
    public async Task<ApiResult> GetJob(long id)
    {
        var job = await db.Set<MaterialIssueJob>().AsNoTracking().Where(x => x.Id == id && x.TenantId == me.TenantId)
            .Select(x => new { jobId = x.Id, x.BillId, status = Status(x.JobStatus), x.Attempts, x.Error, x.CompletedAt }).FirstOrDefaultAsync();
        return job == null ? ApiResult.Fail("领料任务不存在", 404) : ApiResult.Ok(job);
    }

    [HttpPost("material-issue/jobs/{id:long}/replay")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")]
    public async Task<ApiResult> Replay(long id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var job = await db.Set<MaterialIssueJob>().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == me.TenantId);
        if (job == null) return ApiResult.Fail("领料任务不存在", 404);
        if (job.JobStatus != 3 || job.Attempts < 3) return ApiResult.Fail("仅达到 3 次失败并进入 DLQ 的任务允许重放");
        var bill = await db.Set<StockBill>().FirstOrDefaultAsync(x => x.Id == job.BillId && x.TenantId == me.TenantId);
        if (bill == null || bill.BillStatus != 0) return ApiResult.Fail("领料草稿不存在或已过账，不能重放");
        job.JobStatus = 0; job.Attempts = 0; job.Error = null;
        Enqueue(db, job, me.TenantId, me.Id);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return ApiResult.Ok(new { jobId = job.Id, status = "queued" }, "领料任务已重新入队");
    }

    private sealed record MaterialIssueShortageHint(long ProductId, string BatchNo, string Location,
        decimal RequestedQty, decimal AvailableQty, decimal ShortageQty, string[] Suggestions);

    private async Task<List<MaterialIssueShortageHint>> GetShortageHintsAsync(long warehouseId, IEnumerable<StockBillLine> billLines)
    {
        var lines = billLines.ToList();
        var productIds = lines.Select(x => x.ProductId).Distinct().ToList();
        var stock = await db.Set<InventoryStock>().AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && productIds.Contains(s.ProductId))
            .Select(s => new { s.ProductId, s.BatchNo, s.Location, Available = s.Qty - s.LockedQty })
            .ToListAsync();
        return lines.GroupBy(x => new { x.ProductId, BatchNo = x.BatchNo ?? "", Location = x.Location ?? "" })
            .Select(g => new
            {
                g.Key.ProductId, g.Key.BatchNo, g.Key.Location, RequestedQty = g.Sum(x => x.Qty),
                AvailableQty = stock.Where(s => s.ProductId == g.Key.ProductId && (s.BatchNo ?? "") == g.Key.BatchNo &&
                    (s.Location ?? "") == g.Key.Location).Sum(s => s.Available)
            })
            .Where(x => x.AvailableQty < x.RequestedQty)
            .Select(x => new MaterialIssueShortageHint(x.ProductId, x.BatchNo, x.Location, x.RequestedQty,
                x.AvailableQty, x.RequestedQty - x.AvailableQty, new[] { "采购补货", "从其他仓库调拨", "委外加工/供料" }))
            .ToList();
    }

    internal static void Enqueue(SmesDbContext context, MaterialIssueJob job, long tenantId, long userId)
    {
        var id = Guid.NewGuid();
        var route = $"tenant.{tenantId}.production.material-issue.requested.v1";
        context.Add(new OutboxMessage { TenantId = tenantId, MessageId = id, RoutingKey = route,
            EventType = "material-issue.requested.v1", Payload = JsonSerializer.Serialize(new MaterialIssueRequested(id, job.Id, tenantId, job.BillId, userId)), CreatedBy = userId });
    }
    internal static string Status(int value) => value switch { 0 => "queued", 1 => "processing", 2 => "completed", 3 => "failed", _ => "unknown" };
}
