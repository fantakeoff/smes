using System.Text.Json.Serialization;

namespace SmesMobile.Core;

public sealed class ApiEnvelope<T>
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}

public sealed class MobileLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string TenantCode { get; set; } = "machinery";
}

public sealed class MobileLoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public long TenantId { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
}

public sealed class MobileBootstrapResponse
{
    public MobileBootstrapUser User { get; set; } = new();
    public List<string> Roles { get; set; } = [];
    public List<MobileMenuEntry> Menus { get; set; } = [];
}

public sealed class MobileBootstrapUser
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
}

public sealed class MobileMenuEntry
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public int Sort { get; set; }
}

public sealed class MobilePagedResult<T>
{
    public List<T> List { get; set; } = [];
    public int Total { get; set; }
    public int PageSize { get; set; }
    public int CurrentPage { get; set; }
}

public sealed class MobileLookupResponse
{
    public List<MobileOption> Products { get; set; } = [];
    public List<MobileOption> Warehouses { get; set; } = [];
    public List<MobileOption> Processes { get; set; } = [];
    public List<MobileOption> Users { get; set; } = [];
    public List<MobileOption> Defects { get; set; } = [];
    public List<MobileOption> Equipments { get; set; } = [];
    public List<MobileOption> Workshops { get; set; } = [];
}

public sealed class MobileOption
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Extra { get; set; }

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var parts = new[] { Code, Name, Extra }.Where(x => !string.IsNullOrWhiteSpace(x));
            return string.Join(" · ", parts);
        }
    }
}

public sealed class MobileChoice<T>
{
    public T Value { get; set; } = default!;
    public string Label { get; set; } = string.Empty;
}

public sealed class RoutingDetailsResponse
{
    public long Id { get; set; }
    public List<RoutingStepSummary> Steps { get; set; } = [];
}

public sealed class RoutingStepSummary
{
    public int Seq { get; set; }
    public long ProcessId { get; set; }
    public long? WorkshopId { get; set; }
    public decimal StandardHours { get; set; }
    public string? Description { get; set; }
}

public sealed class InventoryStockSummary
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public long WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal AvailableQty { get; set; }
    public string? BatchNo { get; set; }
    public string? Location { get; set; }

    [JsonIgnore]
    public string DisplayText =>
        $"{(string.IsNullOrWhiteSpace(BatchNo) ? "不分批" : $"批次 {BatchNo}")} · " +
        $"{(string.IsNullOrWhiteSpace(Location) ? "未指定库位" : Location)} · 可用 {AvailableQty}";
}

public sealed class WorkOrderSummary
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public long ProductId { get; set; }
    public long? RoutingId { get; set; }
    public decimal Qty { get; set; }
    public decimal CompletedQty { get; set; }
    public int WorkOrderStatus { get; set; }
    [JsonIgnore]
    public string DisplayText => $"{Code ?? "工单"} · {Name ?? "未命名"}";
}

public sealed class WorkOrderDetailsResponse
{
    public WorkOrderSummary WorkOrder { get; set; } = new();
    public List<WorkOrderMaterialSummary> Materials { get; set; } = [];
}

public sealed class WorkOrderMaterialSummary
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public decimal RequiredQty { get; set; }
    public decimal IssuedQty { get; set; }
    public long? UnitId { get; set; }
    [JsonIgnore]
    public string DisplayText => $"{Code ?? "物料"} · {Name ?? "未命名"} · 待领 {Math.Max(0, RequiredQty - IssuedQty):0.####}";
}

public sealed class QualityInspectionSummary
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public string? SourceCode { get; set; }
    public long ProductId { get; set; }
    public decimal Qty { get; set; }
    public decimal SampleQty { get; set; }
    public decimal QualifiedQty { get; set; }
    public decimal DefectQty { get; set; }
    public int Result { get; set; }
    [JsonIgnore]
    public string DisplayText => $"{Code ?? "检验单"} · {SourceCode ?? "无来源单号"} · 抽检 {SampleQty:0.####}";
}

public sealed class MaintenanceTaskSummary
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public long EquipmentId { get; set; }
    public string? EquipmentName { get; set; }
    public int MaintenanceType { get; set; }
    public int TaskStatus { get; set; }
    public DateTime TaskDate { get; set; }
    [JsonIgnore]
    public string DisplayText => $"{Code ?? "维护任务"} · {EquipmentName ?? Name ?? "设备"}";
}

public sealed class MaintenanceTaskDetails
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public long EquipmentId { get; set; }
    public int TaskStatus { get; set; }
    public long? ExecutorId { get; set; }
    public string? Result { get; set; }
    public decimal Cost { get; set; }
    public string? Remark { get; set; }
    public DateTime? FinishAt { get; set; }
}

public sealed class RepairOrderSummary
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public string? FaultDesc { get; set; }
    public long EquipmentId { get; set; }
    public string? EquipmentName { get; set; }
    public int RepairStatus { get; set; }
    public DateTime ReportTime { get; set; }
    [JsonIgnore]
    public string DisplayText => $"{Code ?? "维修单"} · {EquipmentName ?? "设备"}";
}

public sealed class RepairOrderDetails
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public long EquipmentId { get; set; }
    public string? FaultDesc { get; set; }
    public int Level { get; set; }
    public long? ReporterId { get; set; }
    public long? HandlerId { get; set; }
    public int RepairStatus { get; set; }
    public string? Solution { get; set; }
    public decimal Cost { get; set; }
    public int DowntimeMinutes { get; set; }
}

public sealed class StockBillSummary
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public int BillType { get; set; }
    public long WarehouseId { get; set; }
    public int BillStatus { get; set; }
    public long? SourceId { get; set; }
    public string? SourceCode { get; set; }
}

public sealed class StockBillDetails
{
    public long Id { get; set; }
    public string? Code { get; set; }
    public int BillType { get; set; }
    public long WarehouseId { get; set; }
    public int BillStatus { get; set; }
    public long? SourceId { get; set; }
    public List<StockBillLineSummary> Lines { get; set; } = [];
}

public sealed class StockBillLineSummary
{
    public long ProductId { get; set; }
    public decimal Qty { get; set; }
    public string? BatchNo { get; set; }
    public string? Location { get; set; }
}

public sealed class JobAcceptedResponse
{
    public long JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? BillId { get; set; }
}

public sealed class MaterialDraftResponse
{
    public long BillId { get; set; }
    public string? Code { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? JobId { get; set; }
}

public sealed record WorkReportRequest(
    long WorkOrderId, long? ProcessId, long? WorkerId, decimal Qty, decimal QualifiedQty,
    decimal DefectQty, decimal WorkHours, long? DefectItemId, string? Remark);

public sealed record MaterialIssueLineRequest(long MaterialId, decimal Qty, string? BatchNo, string? Location);
public sealed record MaterialIssueDraftRequest(long WarehouseId, List<MaterialIssueLineRequest> Items, string? Remark);
public sealed record QualityJudgeRequest(int Result, decimal QualifiedQty, decimal DefectQty, long? DefectItemId, string? Remark);
public sealed record MaintenanceExecuteRequest(string? Result, decimal Cost, string? Remark);
public sealed record RepairOrderCreateRequest(long EquipmentId, string FaultDesc, int Level);
public sealed record RepairOrderHandleRequest(string? Solution, int RepairStatus, decimal Cost, int DowntimeMinutes);

public static class OfflineCommandKinds
{
    public const string WorkReport = "work-report";
    public const string MaterialIssue = "material-issue";
    public const string QualityJudge = "quality-judge";
    public const string MaintenanceExecute = "maintenance-execute";
    public const string RepairOrderCreate = "repair-order-create";
    public const string RepairOrderHandle = "repair-order-handle";
}

public static class OfflineCommandStates
{
    public const string Pending = "pending";
    public const string Sending = "sending";
    public const string AwaitingJob = "awaiting-job";
    public const string Failed = "failed";
    public const string DeadLetter = "dead-letter";
    public const string UnknownNeedsReconciliation = "unknown-needs-reconciliation";
}

public static class OfflineCommandStages
{
    public const string Submit = "submit";
    public const string CreateMaterialDraft = "create-material-draft";
    public const string SubmitMaterialDraft = "submit-material-draft";
}
