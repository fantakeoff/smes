using Smes.Domain.Common;

namespace Smes.Domain.Sales
{
    /// <summary>销售订单状态：0 草稿 / 1 已确认 / 2 部分出库 / 3 已完成 / 4 已取消</summary>
    public class SalesOrder : BaseEntity
    {
        public long CustomerId { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.Today;
        public DateTime? DeliveryDate { get; set; }
        public int OrderStatus { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalAmount { get; set; }
        public long? SalesmanId { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Address { get; set; }
        public string? Contact { get; set; }
        public string? Phone { get; set; }
        public List<SalesOrderLine> Lines { get; set; } = new();
    }

    public class SalesOrderLine : BaseEntity
    {
        public long OrderId { get; set; }
        public int Seq { get; set; }
        public long ProductId { get; set; }
        public decimal Qty { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        public decimal DeliveredQty { get; set; }
        public DateTime? DeliveryDate { get; set; }
    }
}

namespace Smes.Domain.Purchase
{
    /// <summary>采购申请状态：0 草稿 / 1 待审批 / 2 已批准 / 3 已驳回 / 4 已关闭</summary>
    public class PurchaseRequisition : BaseEntity
    {
        public DateTime RequestDate { get; set; } = DateTime.Today;
        public int ApprovalStatus { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public long? ApprovedBy { get; set; }
        public string? ApprovalRemark { get; set; }
        public decimal TotalQty { get; set; }
        public List<PurchaseRequisitionLine> Lines { get; set; } = new();
    }

    public class PurchaseRequisitionLine : BaseEntity
    {
        public long RequisitionId { get; set; }
        public int Seq { get; set; }
        public long ProductId { get; set; }
        public decimal RequiredQty { get; set; }
        public decimal RequestedQty { get; set; }
        public decimal ConvertedQty { get; set; }
        public long? WorkOrderId { get; set; }
        public long? DemandLineId { get; set; }
        public long? PurchaseOrderLineId { get; set; }
        public string? Remark { get; set; }
    }

    /// <summary>采购订单状态：0 草稿 / 1 已确认 / 2 部分收货 / 3 已完成 / 4 已取消</summary>
    public class PurchaseOrder : BaseEntity
    {
        public long SupplierId { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.Today;
        public DateTime? ExpectedDate { get; set; }
        public int OrderStatus { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalAmount { get; set; }
        public long? BuyerId { get; set; }
        public string? PaymentMethod { get; set; }
        public List<PurchaseOrderLine> Lines { get; set; } = new();
    }

    public class PurchaseOrderLine : BaseEntity
    {
        public long OrderId { get; set; }
        public int Seq { get; set; }
        public long ProductId { get; set; }
        public decimal Qty { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        public decimal ReceivedQty { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public long? RequisitionLineId { get; set; }
    }
}
