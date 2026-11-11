using Smes.Domain.Common;

namespace Smes.Domain.Quality
{
    /// <summary>检验项目（检测项主数据）</summary>
    public class InspectionItem : BaseEntity
    {
        /// <summary>1 定量 / 2 定性 / 3 外观</summary>
        public int ItemType { get; set; } = 1;
        public string? Method { get; set; }
        public string? Standard { get; set; }
        public string? Unit { get; set; }
        public string? Instrument { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
    }

    /// <summary>检验方案：按检验类型 + 产品配置检测项</summary>
    public class InspectionScheme : BaseEntity
    {
        /// <summary>1 来料 / 2 过程 / 3 出货 / 4 首件</summary>
        public int InspectionType { get; set; } = 1;
        public long? ProductId { get; set; }
        public string? AqlLevel { get; set; }
        public string? SamplePlan { get; set; }
        public bool IsDefault { get; set; }
        public List<InspectionSchemeItem> Items { get; set; } = new();
    }

    public class InspectionSchemeItem : BaseEntity
    {
        public long SchemeId { get; set; }
        public int Seq { get; set; }
        public long ItemId { get; set; }
        public string? Standard { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
    }

    /// <summary>检验单：Result 0 待检 / 1 合格 / 2 不合格 / 3 让步接收</summary>
    public class QualityInspection : BaseEntity
    {
        public int InspectionType { get; set; } = 1;
        public string? SourceType { get; set; }
        public long? SourceId { get; set; }
        public string? SourceCode { get; set; }
        public long ProductId { get; set; }
        public string? BatchNo { get; set; }
        public decimal Qty { get; set; }
        public decimal SampleQty { get; set; }
        public decimal QualifiedQty { get; set; }
        public decimal DefectQty { get; set; }
        public int Result { get; set; }
        public long? InspectorId { get; set; }
        public DateTime? InspectTime { get; set; }
        public long? DefectItemId { get; set; }
        public long? SchemeId { get; set; }
        public List<QualityInspectionLine> Lines { get; set; } = new();
    }

    public class QualityInspectionLine : BaseEntity
    {
        public long InspectionId { get; set; }
        public int Seq { get; set; }
        public long ItemId { get; set; }
        public string? Standard { get; set; }
        public string? ActualValue { get; set; }
        /// <summary>1 合格 / 2 不合格</summary>
        public int Result { get; set; } = 1;
        public string? DefectDesc { get; set; }
    }
}

namespace Smes.Domain.Equipment
{
    public class EquipmentCategory : BaseEntity
    {
        public long ParentId { get; set; }
        public int Sort { get; set; }
    }

    /// <summary>设备状态：1 运行 / 2 待机 / 3 维修 / 4 报废</summary>
    public class Equipment : BaseEntity
    {
        public long? CategoryId { get; set; }
        public string? Model { get; set; }
        public string? SerialNo { get; set; }
        public long? WorkshopId { get; set; }
        public int EquipmentStatus { get; set; } = 1;
        public DateTime? PurchaseDate { get; set; }
        public string? Vendor { get; set; }
        public string? Location { get; set; }
        public string? IpAddress { get; set; }
        public decimal RunHours { get; set; }
        public DateTime? LastMaintainAt { get; set; }
    }

    /// <summary>点检/保养计划：1 点检 / 2 保养</summary>
    public class MaintenancePlan : BaseEntity
    {
        public long EquipmentId { get; set; }
        public int MaintenanceType { get; set; } = 1;
        public int CycleDays { get; set; } = 30;
        public DateTime NextDate { get; set; } = DateTime.Today;
        public string? Items { get; set; }
        public long? OwnerId { get; set; }
    }

    /// <summary>点检/保养任务：0 待执行 / 1 已执行 / 2 异常</summary>
    public class MaintenanceTask : BaseEntity
    {
        public long? PlanId { get; set; }
        public long EquipmentId { get; set; }
        public int MaintenanceType { get; set; } = 1;
        public DateTime TaskDate { get; set; } = DateTime.Today;
        public long? ExecutorId { get; set; }
        public int TaskStatus { get; set; }
        public string? Result { get; set; }
        public string? Items { get; set; }
        public decimal Cost { get; set; }
        public DateTime? FinishAt { get; set; }
    }

    /// <summary>维修单：0 待受理 / 1 维修中 / 2 已完成 / 3 已关闭</summary>
    public class RepairOrder : BaseEntity
    {
        public long EquipmentId { get; set; }
        public string? FaultDesc { get; set; }
        public int Level { get; set; } = 1;
        public long? ReporterId { get; set; }
        public DateTime ReportTime { get; set; } = DateTime.Now;
        public long? HandlerId { get; set; }
        public int RepairStatus { get; set; }
        public string? Solution { get; set; }
        public DateTime? FinishTime { get; set; }
        public decimal Cost { get; set; }
        public int DowntimeMinutes { get; set; }
    }

    /// <summary>设备报警：0 未恢复 / 1 已恢复 / 2 已确认 / 3 已关闭</summary>
    public class EquipmentAlarm : BaseEntity
    {
        public long EquipmentId { get; set; }
        public string? AlarmCode { get; set; }
        /// <summary>1 超上限 / 2 超下限 / 3 离线 / 4 故障</summary>
        public int AlarmType { get; set; }
        public string? Content { get; set; }
        public int Level { get; set; } = 1;
        public int AlarmStatus { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.Now;
        public DateTime? RecoveredAt { get; set; }
    }
}

namespace Smes.Domain.Tooling
{
    public class ToolCategory : BaseEntity
    {
        public long ParentId { get; set; }
        public int Sort { get; set; }
    }

    /// <summary>工装状态：1 在库 / 2 领用中 / 3 维修 / 4 报废</summary>
    public class Tool : BaseEntity
    {
        public long? CategoryId { get; set; }
        public string? Spec { get; set; }
        public decimal TotalQty { get; set; }
        public decimal AvailableQty { get; set; }
        public long? UnitId { get; set; }
        public int ToolStatus { get; set; } = 1;
        public string? Location { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public int ServiceLifeDays { get; set; }
    }

    /// <summary>工装领用/归还：1 领用 / 2 归还</summary>
    public class ToolTransaction : BaseEntity
    {
        public long ToolId { get; set; }
        public int TransactionType { get; set; } = 1;
        public decimal Qty { get; set; }
        public long? UserId { get; set; }
        public DateTime OccurDate { get; set; } = DateTime.Today;
        public DateTime? ExpectedReturnDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public int TransactionStatus { get; set; }
        public long? WorkOrderId { get; set; }
    }
}

namespace Smes.Domain.Exceptions
{
    /// <summary>现场异常：0 待处理 / 1 处理中 / 2 已关闭</summary>
    public class ExceptionRecord : BaseEntity
    {
        /// <summary>1 设备 / 2 质量 / 3 物料 / 4 安全 / 5 其他</summary>
        public int ExceptionType { get; set; } = 1;
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        /// <summary>1 一般 / 2 重要 / 3 紧急</summary>
        public int Level { get; set; } = 1;
        public string? Source { get; set; }
        public long? ReporterId { get; set; }
        public DateTime ReportTime { get; set; } = DateTime.Now;
        public long? HandlerId { get; set; }
        public int ExceptionStatus { get; set; }
        public string? Solution { get; set; }
        public DateTime? CloseTime { get; set; }
        public long? WorkOrderId { get; set; }
        public long? EquipmentId { get; set; }
    }
}
