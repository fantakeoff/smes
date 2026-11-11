using Smes.Domain.Common;

namespace Smes.Domain.Production;

/// <summary>
/// 委外加工单：MVP 为一项投入物料对应一项加工产出物料；不代表质检放行。
/// 状态：0 草稿 / 1 已发料 / 2 收货中 / 3 已完成 / 4 已关闭。
/// </summary>
public class SubcontractOrder : BaseEntity
{
    public long SupplierId { get; set; }
    public long InputProductId { get; set; }
    public long OutputProductId { get; set; }
    public decimal OrderQty { get; set; }
    public decimal IssuedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public long SourceWarehouseId { get; set; }
    public long TargetWarehouseId { get; set; }
    public int OrderStatus { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.Today;
    public DateTime? DueDate { get; set; }
    public string? InputBatchNo { get; set; }
    public string? InputLocation { get; set; }
    public string? OutputBatchNo { get; set; }
    public string? OutputLocation { get; set; }
}
