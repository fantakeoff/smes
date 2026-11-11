using System.Text.Json.Serialization;

namespace Smes.Domain.Common;

/// <summary>所有业务实体的基类：统一主键、编码、状态、审计字段与软删除。</summary>
public abstract class BaseEntity
{
    public long Id { get; set; }
    /// <summary>Owning tenant. Null is reserved for explicitly global/catalog data.</summary>
    public long? TenantId { get; set; }
    /// <summary>业务编码（单号/编号），由编号规则生成或手工录入</summary>
    public string? Code { get; set; }
    public string? Name { get; set; }
    /// <summary>状态：1 启用 / 0 停用（业务单据另有自己的状态字段）</summary>
    public int Status { get; set; } = 1;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public long? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
}
