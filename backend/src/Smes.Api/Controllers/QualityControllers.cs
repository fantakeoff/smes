using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Smes.Api.Infrastructure;
using Smes.Api.Messaging;
using Smes.Domain.Production;
using Smes.Domain.Quality;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

[ApiController]
[Route("api/quality-item")]
public class QualityItemController : CrudController<InspectionItem>
{
    public QualityItemController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}

/// <summary>检验方案（主从表）</summary>
[ApiController]
[Route("api/quality-scheme")]
public class QualitySchemeController : CrudController<InspectionScheme>
{
    public QualitySchemeController(SmesDbContext db, CurrentUser me) : base(db, me) { }

    [HttpGet("{id:long}")]
    public override async Task<ApiResult> Get(long id)
    {
        var scheme = await Db.Set<InspectionScheme>().AsNoTracking().Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);
        return scheme == null ? ApiResult.Fail("方案不存在", 404) : ApiResult.Ok(scheme);
    }

    [HttpPost]
    public override async Task<ApiResult> Create([FromBody] InspectionScheme entity)
    {
        entity.Id = 0;
        entity.CreatedAt = DateTime.Now;
        entity.CreatedBy = Me.Id;
        if (string.IsNullOrWhiteSpace(entity.Code))
        {
            entity.Code = await NumberGenerator.NextAsync(Db, "QS", Me.Id);
        }
        foreach (var item in entity.Items) { item.Id = 0; item.SchemeId = 0; }
        Db.Add(entity);
        await Db.SaveChangesAsync();
        var saved = await Db.Set<InspectionScheme>().AsNoTracking().Include(x => x.Items).FirstAsync(x => x.Id == entity.Id);
        return ApiResult.Ok(saved, "新增成功");
    }

    [HttpPut("{id:long}")]
    public override async Task<ApiResult> Update(long id, [FromBody] InspectionScheme entity)
    {
        var scheme = await Db.Set<InspectionScheme>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (scheme == null) return ApiResult.Fail("方案不存在", 404);
        scheme.Name = entity.Name;
        scheme.InspectionType = entity.InspectionType;
        scheme.ProductId = entity.ProductId;
        scheme.AqlLevel = entity.AqlLevel;
        scheme.SamplePlan = entity.SamplePlan;
        scheme.IsDefault = entity.IsDefault;
        scheme.Remark = entity.Remark;
        Db.RemoveRange(scheme.Items);
        scheme.Items = (entity.Items ?? new List<InspectionSchemeItem>()).Select((s, i) => new InspectionSchemeItem
        {
            SchemeId = id, Seq = i + 1, ItemId = s.ItemId, Standard = s.Standard, MinValue = s.MinValue, MaxValue = s.MaxValue
        }).ToList();
        await Db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }
}

/// <summary>检验单</summary>
[ApiController]
[Route("api/quality-inspection")]
public class QualityInspectionController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;

    public QualityInspectionController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? inspectionType,
        [FromQuery] int? result, [FromQuery] long? productId)
    {
        var query = _db.Set<QualityInspection>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword))
            query = query.Where(x => x.Code!.Contains(page.Keyword) || x.SourceCode!.Contains(page.Keyword) || x.BatchNo!.Contains(page.Keyword));
        if (inspectionType.HasValue) query = query.Where(x => x.InspectionType == inspectionType);
        if (result.HasValue) query = query.Where(x => x.Result == result);
        if (productId.HasValue) query = query.Where(x => x.ProductId == productId);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var inspection = await _db.Set<QualityInspection>().AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id);
        return inspection == null ? ApiResult.Fail("检验单不存在", 404) : ApiResult.Ok(inspection);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] QualityInspection input)
    {
        if (input.Result != 0 || input.QualifiedQty != 0 || input.DefectQty != 0 || input.InspectorId != null || input.InspectTime != null)
            return ApiResult.Fail("检验单创建只能为待检状态，判定请走异步判定任务", 400);
        if (input.Qty <= 0 || input.SampleQty <= 0 || input.SampleQty > input.Qty)
            return ApiResult.Fail("送检量和抽检量必须有效", 400);
        input.Id = 0;
        input.Code = string.IsNullOrWhiteSpace(input.Code)
            ? await NumberGenerator.NextAsync(_db, "QC", _me.Id) : input.Code;
        input.CreatedAt = DateTime.Now;
        input.CreatedBy = _me.Id;
        if (string.IsNullOrWhiteSpace(input.Name)) input.Name = "检验单";
        if (input.Lines == null) input.Lines = new List<QualityInspectionLine>();
        foreach (var line in input.Lines) { line.Id = 0; line.InspectionId = 0; }
        input.DefectQty = 0;
        _db.Add(input);
        await _db.SaveChangesAsync();
        var saved = await _db.Set<QualityInspection>().AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.Id == input.Id);
        return ApiResult.Ok(saved, "创建成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] QualityInspection input)
    {
        var inspection = await _db.Set<QualityInspection>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (inspection == null) return ApiResult.Fail("检验单不存在", 404);
        if (inspection.Result != 0 || await _db.Set<QualityJudgeJob>().AnyAsync(x => x.InspectionId == id && (x.JobStatus == 0 || x.JobStatus == 1)))
            return ApiResult.Fail("检验单已判定或判定处理中，禁止直接修改", 409);
        if (input.Result != 0 || input.QualifiedQty != 0 || input.DefectQty != 0 || input.InspectorId != null || input.InspectTime != null)
            return ApiResult.Fail("待检单不能通过编辑接口判定，请提交异步判定任务", 400);
        if (input.Qty <= 0 || input.SampleQty <= 0 || input.SampleQty > input.Qty)
            return ApiResult.Fail("送检量和抽检量必须有效", 400);
        inspection.InspectionType = input.InspectionType;
        inspection.SourceType = input.SourceType;
        inspection.SourceCode = input.SourceCode;
        inspection.ProductId = input.ProductId;
        inspection.BatchNo = input.BatchNo;
        inspection.Qty = input.Qty;
        inspection.SampleQty = input.SampleQty;
        inspection.QualifiedQty = input.QualifiedQty;
        inspection.DefectQty = input.DefectQty;
        inspection.Result = input.Result;
        inspection.InspectorId = input.InspectorId;
        inspection.InspectTime = input.InspectTime;
        inspection.DefectItemId = input.DefectItemId;
        inspection.Remark = input.Remark;
        _db.RemoveRange(inspection.Lines);
        inspection.Lines = (input.Lines ?? new List<QualityInspectionLine>()).Select((l, i) => new QualityInspectionLine
        {
            InspectionId = id, Seq = i + 1, ItemId = l.ItemId, Standard = l.Standard,
            ActualValue = l.ActualValue, Result = l.Result, DefectDesc = l.DefectDesc
        }).ToList();
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    public record JudgeRequest(int Result, decimal QualifiedQty, decimal DefectQty, long? DefectItemId, string? Remark);

    [HttpPost("{id:long}/judge")]
    public async Task<ApiResult> Judge(long id, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] JudgeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            return ApiResult.Fail("必须提供有效的 Idempotency-Key（最多 128 个字符）", 400);
        if (request.Result is not (1 or 2 or 3) || request.QualifiedQty < 0 || request.DefectQty < 0 ||
            request.QualifiedQty + request.DefectQty <= 0)
            return ApiResult.Fail("判定结果或数量无效", 400);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var previous = await _db.Set<QualityJudgeJob>().FirstOrDefaultAsync(x => x.TenantId == _me.TenantId && x.IdempotencyKey == idempotencyKey, ct);
        if (previous != null)
        {
            if (previous.PayloadHash != payloadHash || previous.InspectionId != id)
                return ApiResult.Fail("Idempotency-Key 已用于不同的判定请求", 409);
            return ApiResult.Ok(new { jobId = previous.Id, status = JobStatusName(previous.JobStatus) }, "判定任务已排队");
        }
        var inspection = await _db.Set<QualityInspection>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (inspection == null) return ApiResult.Fail("检验单不存在", 404);
        if (inspection.Result != 0) return ApiResult.Fail("检验单已判定，不能重复提交", 409);
        if (await _db.Set<QualityJudgeJob>().AnyAsync(x => x.InspectionId == id && (x.JobStatus == 0 || x.JobStatus == 1), ct))
            return ApiResult.Fail("该检验单已有判定任务处理中", 409);
        if (request.QualifiedQty + request.DefectQty != inspection.SampleQty)
            return ApiResult.Fail("合格数量与不合格数量之和必须等于抽检数量", 400);
        var messageId = Guid.NewGuid();
        var job = new QualityJudgeJob
        {
            TenantId = _me.TenantId, InspectionId = id, IdempotencyKey = idempotencyKey,
            PayloadHash = payloadHash, Result = request.Result, QualifiedQty = request.QualifiedQty,
            DefectQty = request.DefectQty, DefectItemId = request.DefectItemId, JudgeRemark = request.Remark,
            RequestedBy = _me.Id, JobStatus = 0, MessageId = messageId
        };
        _db.Add(job);
        await _db.SaveChangesAsync(ct);
        var routingKey = $"tenant.{_me.TenantId}.quality.inspection.judge.requested.v1";
        var evt = new QualityJudgeRequested(messageId, job.Id, _me.TenantId, id);
        _db.Add(new OutboxMessage
        {
            TenantId = _me.TenantId, MessageId = messageId, EventType = "quality.inspection.judge.requested.v1",
            RoutingKey = routingKey, Payload = JsonSerializer.Serialize(evt)
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ApiResult.Ok(new { jobId = job.Id, status = "queued" }, "判定任务已排队");
    }

    [HttpGet("jobs/{id:long}")]
    public async Task<ApiResult> GetJudgeJob(long id, CancellationToken ct)
    {
        var job = await _db.Set<QualityJudgeJob>().AsNoTracking()
            .Where(x => x.Id == id && x.TenantId == _me.TenantId)
            .Select(x => new { jobId = x.Id, inspectionId = x.InspectionId, status = JobStatusName(x.JobStatus),
                attempts = x.Attempts, error = x.Error, completedAt = x.CompletedAt })
            .FirstOrDefaultAsync(ct);
        return job == null ? ApiResult.Fail("判定任务不存在", 404) : ApiResult.Ok(job);
    }

    private static string JobStatusName(int status) => status switch
    { 0 => "queued", 1 => "processing", 2 => "completed", 3 => "dead-lettered", _ => "unknown" };

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        var inspection = await _db.Set<QualityInspection>().FirstOrDefaultAsync(x => x.Id == id);
        if (inspection == null) return ApiResult.Fail("检验单不存在", 404);
        if (inspection.Result != 0 || await _db.Set<QualityJudgeJob>().AnyAsync(x => x.InspectionId == id && (x.JobStatus == 0 || x.JobStatus == 1)))
            return ApiResult.Fail("检验单已判定或判定处理中，禁止删除", 409);
        inspection.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}
