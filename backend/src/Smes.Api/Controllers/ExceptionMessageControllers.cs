using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smes.Api.Infrastructure;
using Smes.Domain.Exceptions;
using Smes.Domain.Identity;
using Smes.Infrastructure;

namespace Smes.Api.Controllers;

[ApiController]
[Route("api/exception")]
public class ExceptionController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;

    public ExceptionController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] int? exceptionStatus,
        [FromQuery] int? exceptionType, [FromQuery] int? level)
    {
        var query = _db.Set<ExceptionRecord>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword))
            query = query.Where(x => x.Code!.Contains(page.Keyword) || x.Title.Contains(page.Keyword));
        if (exceptionStatus.HasValue) query = query.Where(x => x.ExceptionStatus == exceptionStatus);
        if (exceptionType.HasValue) query = query.Where(x => x.ExceptionType == exceptionType);
        if (level.HasValue) query = query.Where(x => x.Level == level);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.ReportTime).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("{id:long}")]
    public async Task<ApiResult> Get(long id)
    {
        var record = await _db.Set<ExceptionRecord>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return record == null ? ApiResult.Fail("异常不存在", 404) : ApiResult.Ok(record);
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] ExceptionRecord record)
    {
        record.Id = 0;
        record.Code = await NumberGenerator.NextAsync(_db, "EX", _me.Id);
        record.ReportTime = DateTime.Now;
        record.ReporterId = _me.Id;
        record.ExceptionStatus = 0;
        record.CreatedAt = DateTime.Now;
        _db.Add(record);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(record, "提报成功");
    }

    public record HandleRequest(long? HandlerId, int ExceptionStatus, string? Solution);

    [HttpPost("{id:long}/handle")]
    public async Task<ApiResult> Handle(long id, [FromBody] HandleRequest request)
    {
        var record = await _db.Set<ExceptionRecord>().FirstOrDefaultAsync(x => x.Id == id);
        if (record == null) return ApiResult.Fail("异常不存在", 404);
        record.HandlerId = request.HandlerId ?? _me.Id;
        record.ExceptionStatus = request.ExceptionStatus;
        record.Solution = request.Solution;
        if (request.ExceptionStatus == 2) record.CloseTime = DateTime.Now;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "处理已保存");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        var record = await _db.Set<ExceptionRecord>().FirstOrDefaultAsync(x => x.Id == id);
        if (record == null) return ApiResult.Fail("异常不存在", 404);
        record.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}

[ApiController]
[Route("api/notice")]
public class NoticeController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;

    public NoticeController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page)
    {
        var query = _db.Set<SysNotice>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Keyword))
            query = query.Where(x => x.Title.Contains(page.Keyword) || x.Content.Contains(page.Keyword));
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.PublishTime).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpPost]
    public async Task<ApiResult> Create([FromBody] SysNotice notice)
    {
        notice.Id = 0;
        notice.PublisherId = _me.Id;
        notice.PublishTime = DateTime.Now;
        notice.CreatedAt = DateTime.Now;
        _db.Add(notice);
        await _db.SaveChangesAsync();
        return ApiResult.Ok(notice, "发布成功");
    }

    [HttpPut("{id:long}")]
    public async Task<ApiResult> Update(long id, [FromBody] SysNotice notice)
    {
        var existing = await _db.Set<SysNotice>().FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("通知不存在", 404);
        existing.Title = notice.Title;
        existing.Content = notice.Content;
        existing.Type = notice.Type;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "修改成功");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        var existing = await _db.Set<SysNotice>().FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return ApiResult.Fail("通知不存在", 404);
        existing.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}

[ApiController]
[Route("api/message")]
public class MessageController : ControllerBase
{
    private readonly SmesDbContext _db;
    private readonly CurrentUser _me;

    public MessageController(SmesDbContext db, CurrentUser me) { _db = db; _me = me; }

    [HttpGet]
    public async Task<ApiResult> List([FromQuery] PageQuery page, [FromQuery] bool? isRead)
    {
        var query = _db.Set<SysMessage>().AsNoTracking().Where(x => x.ReceiverId == _me.Id);
        if (isRead.HasValue) query = query.Where(x => x.IsRead == isRead);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return ApiResult.Ok(new { list, total, pageSize = page.PageSize, currentPage = page.Page });
    }

    [HttpGet("unread-count")]
    public async Task<ApiResult> UnreadCount()
    {
        var count = await _db.Set<SysMessage>().CountAsync(x => x.ReceiverId == _me.Id && !x.IsRead);
        return ApiResult.Ok(count);
    }

    [HttpPost("{id:long}/read")]
    public async Task<ApiResult> Read(long id)
    {
        var message = await _db.Set<SysMessage>().FirstOrDefaultAsync(x => x.Id == id && x.ReceiverId == _me.Id);
        if (message == null) return ApiResult.Fail("消息不存在", 404);
        message.IsRead = true;
        message.ReadAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "已读");
    }

    [HttpPost("read-all")]
    public async Task<ApiResult> ReadAll()
    {
        var messages = await _db.Set<SysMessage>().Where(x => x.ReceiverId == _me.Id && !x.IsRead).ToListAsync();
        foreach (var m in messages) { m.IsRead = true; m.ReadAt = DateTime.Now; }
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "全部已读");
    }

    [HttpDelete("{id:long}")]
    public async Task<ApiResult> Delete(long id)
    {
        var message = await _db.Set<SysMessage>().FirstOrDefaultAsync(x => x.Id == id && x.ReceiverId == _me.Id);
        if (message == null) return ApiResult.Fail("消息不存在", 404);
        message.IsDeleted = true;
        await _db.SaveChangesAsync();
        return ApiResult.Ok(null, "删除成功");
    }
}
