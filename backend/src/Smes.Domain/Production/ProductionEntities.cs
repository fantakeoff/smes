using Smes.Domain.Common;

namespace Smes.Domain.Production
{
    /// <summary>生产计划状态：0 草稿 / 1 已下达 / 2 生产中 / 3 已完成 / 4 已关闭</summary>
    public class ProductionPlan : BaseEntity
    {
        public DateTime PlanDate { get; set; } = DateTime.Today;
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today.AddDays(7);
        public long? WorkshopId { get; set; }
        public int PlanStatus { get; set; }
        public decimal TotalQty { get; set; }
        /// <summary>来源：1 销售订单 / 2 预测 / 3 手工</summary>
        public int SourceType { get; set; } = 3;
        public long? SalesOrderId { get; set; }
        public List<ProductionPlanLine> Lines { get; set; } = new();
    }

    public class ProductionPlanLine : BaseEntity
    {
        public long PlanId { get; set; }
        public int Seq { get; set; }
        public long ProductId { get; set; }
        public decimal Qty { get; set; }
        public decimal CompletedQty { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public long? SalesOrderLineId { get; set; }
    }

    /// <summary>工单状态：0 未下达 / 1 已下达 / 2 生产中 / 3 已完工 / 4 已关闭</summary>
    public class WorkOrder : BaseEntity
    {
        public long? PlanId { get; set; }
        public long? PlanLineId { get; set; }
        public long ProductId { get; set; }
        public long? RoutingId { get; set; }
        public long? BomId { get; set; }
        public decimal Qty { get; set; }
        public decimal CompletedQty { get; set; }
        /// <summary>已完成报工中的不良数量。</summary>
        public decimal DefectQty { get; set; }
        /// <summary>已经生产入库的合格数量，与报工完成数量分开守恒。</summary>
        public decimal ReceivedQty { get; set; }
        public long? WorkshopId { get; set; }
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today.AddDays(3);
        public int WorkOrderStatus { get; set; }
        /// <summary>1 产品工单 / 2 零组件工单</summary>
        public int WorkOrderType { get; set; } = 1;
        /// <summary>1 低 / 2 中 / 3 高 / 4 紧急</summary>
        public int Priority { get; set; } = 2;
        public long? ParentId { get; set; }
        public string? SourceCode { get; set; }
    }

    public class WorkOrderMaterial : BaseEntity
    {
        public long WorkOrderId { get; set; }
        public long ProductId { get; set; }
        public long? UnitId { get; set; }
        public decimal RequiredQty { get; set; }
        public decimal IssuedQty { get; set; }
    }

    /// <summary>报工记录</summary>
    public class WorkReport : BaseEntity
    {
        public long WorkOrderId { get; set; }
        public long? ProcessId { get; set; }
        public long? WorkerId { get; set; }
        public long? WorkshopId { get; set; }
        public decimal Qty { get; set; }
        public decimal QualifiedQty { get; set; }
        public decimal DefectQty { get; set; }
        public decimal WorkHours { get; set; }
        public DateTime ReportTime { get; set; } = DateTime.Now;
        public long? DefectItemId { get; set; }
    }

    /// <summary>工厂日历：1 工作日 / 2 休息日 / 3 节假日</summary>
    public class WorkCalendar : BaseEntity
    {
        public DateTime Date { get; set; }
        public int DayType { get; set; } = 1;
        public string? Shift { get; set; }
    }

    /// <summary>异步报工任务：0 待处理 / 1 处理中 / 2 已完成 / 3 失败。</summary>
    public class WorkReportJob : BaseEntity
    {
        public long WorkOrderId { get; set; }
        public long? ProcessId { get; set; }
        public long? WorkerId { get; set; }
        public decimal Qty { get; set; }
        public decimal QualifiedQty { get; set; }
        public decimal DefectQty { get; set; }
        public decimal WorkHours { get; set; }
        public long? DefectItemId { get; set; }
        public string? IdempotencyKey { get; set; }
        public int JobStatus { get; set; }
        public int Attempts { get; set; }
        public string? Error { get; set; }
        public long? WorkReportId { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>事务性 Outbox 事件。</summary>
    public class OutboxMessage : BaseEntity
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public string EventType { get; set; } = "work-report.requested.v1";
        public string RoutingKey { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public int PublishAttempts { get; set; }
        public string? LastError { get; set; }
    }

    /// <summary>Consumer 幂等收件记录。</summary>
    public class InboxMessage : BaseEntity
    {
        public Guid MessageId { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.Now;
    }

    /// <summary>生产领料异步任务：0 queued / 1 processing / 2 completed / 3 failed。</summary>
    public class MaterialIssueJob : BaseEntity
    {
        public long BillId { get; set; }
        public long WorkOrderId { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string PayloadHash { get; set; } = string.Empty;
        public int JobStatus { get; set; }
        public int Attempts { get; set; }
        public string? Error { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>Links a stock bill line to its exact BOM/work-order material row, including duplicate products.</summary>
    public class MaterialIssueLineLink : BaseEntity
    {
        public long BillLineId { get; set; }
        public long WorkOrderMaterialId { get; set; }
        public decimal Qty { get; set; }
    }
}
