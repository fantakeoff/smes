namespace Smes.Api.Infrastructure;

/// <summary>统一响应结构，前端约定 code === 0 为成功。</summary>
public class ApiResult
{
    public int Code { get; set; }
    public string Message { get; set; } = "ok";
    public bool Success => Code == 0;
    public object? Data { get; set; }

    public static ApiResult Ok(object? data = null, string message = "ok")
        => new() { Code = 0, Message = message, Data = data };

    public static ApiResult Fail(string message, int code = 400)
        => new() { Code = code, Message = message };
}

public class PagedResult<T>
{
    public List<T> List { get; set; } = new();
    public int Total { get; set; }
    public int PageSize { get; set; }
    public int CurrentPage { get; set; }
}

public class PageQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Keyword { get; set; }
    public int? Status { get; set; }
    public string? SortBy { get; set; }
    public string? SortOrder { get; set; } = "desc";
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
}

public class OptionItem
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Extra { get; set; }
}
