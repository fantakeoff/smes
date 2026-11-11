using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmesMobile.Core;

namespace SmesMobile.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MobileApiClient _api;
    private readonly OfflineCommandStore _store;
    private readonly OfflineCommandProcessor _processor;
    private CancellationTokenSource? _backgroundSync;
    private MobileLookupResponse? _lookup;
    private int _workOrderContextRequest;
    private int _inventoryRequest;
    private string? _ownerKey;
    private long _tenantId;
    private long _currentUserId;

    [ObservableProperty] private string _serverBaseUrl = "http://127.0.0.1:8088/api";
    [ObservableProperty] private string _tenantCode = "machinery";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "输入 SMES API 根地址和账号登录。";
    [ObservableProperty] private bool _showStatusMessage = true;
    [ObservableProperty] private string _currentUserDisplay = string.Empty;
    [ObservableProperty] private string _currentRolesText = string.Empty;
    [ObservableProperty] private MobileMenuEntry? _selectedMenu;
    [ObservableProperty] private WorkOrderSummary? _selectedWorkOrder;
    [ObservableProperty] private WorkOrderMaterialSummary? _selectedIssueMaterial;
    [ObservableProperty] private QualityInspectionSummary? _selectedInspection;
    [ObservableProperty] private MaintenanceTaskSummary? _selectedMaintenanceTask;
    [ObservableProperty] private RepairOrderSummary? _selectedRepairOrder;

    [ObservableProperty] private string _reportQtyText = string.Empty;
    [ObservableProperty] private string _reportDefectQtyText = "0";
    [ObservableProperty] private string _reportHoursText = "0";
    [ObservableProperty] private string _reportRemark = string.Empty;

    [ObservableProperty] private string _issueQtyText = string.Empty;
    [ObservableProperty] private string _issueBatchNo = string.Empty;
    [ObservableProperty] private string _issueLocation = string.Empty;
    [ObservableProperty] private string _issueRemark = string.Empty;

    [ObservableProperty] private string _judgeQualifiedQtyText = string.Empty;
    [ObservableProperty] private string _judgeDefectQtyText = "0";
    [ObservableProperty] private string _judgeRemark = string.Empty;

    [ObservableProperty] private string _maintenanceResult = "正常";
    [ObservableProperty] private string _maintenanceCostText = "0";
    [ObservableProperty] private string _maintenanceRemark = string.Empty;

    [ObservableProperty] private MobileOption? _selectedWarehouse;
    [ObservableProperty] private MobileOption? _selectedReportProcess;
    [ObservableProperty] private MobileOption? _selectedReportDefect;
    [ObservableProperty] private MobileOption? _selectedJudgeDefect;
    [ObservableProperty] private MobileOption? _selectedRepairEquipment;
    [ObservableProperty] private MobileChoice<int>? _selectedJudgeResult;
    [ObservableProperty] private MobileChoice<int>? _selectedRepairLevel;
    [ObservableProperty] private MobileChoice<int>? _selectedRepairStatus;
    [ObservableProperty] private InventoryStockSummary? _selectedIssueStock;
    [ObservableProperty] private string _inventoryHint = "先选择工单、用料和仓库，再选择有库存的批次。";
    [ObservableProperty] private string _repairFaultDescription = string.Empty;
    [ObservableProperty] private string _repairSolution = string.Empty;
    [ObservableProperty] private string _repairCostText = "0";
    [ObservableProperty] private string _repairDowntimeMinutesText = "0";

    public ObservableCollection<MobileMenuEntry> Menus { get; } = [];
    public ObservableCollection<WorkOrderSummary> WorkOrders { get; } = [];
    public ObservableCollection<WorkOrderSummary> SelectableWorkOrders { get; } = [];
    public ObservableCollection<WorkOrderMaterialSummary> WorkOrderMaterials { get; } = [];
    public ObservableCollection<MobileOption> WarehouseOptions { get; } = [];
    public ObservableCollection<MobileOption> ProcessOptions { get; } = [];
    public ObservableCollection<MobileOption> ReportProcessOptions { get; } = [];
    public ObservableCollection<MobileOption> DefectOptions { get; } = [];
    public ObservableCollection<MobileOption> EquipmentOptions { get; } = [];
    public ObservableCollection<InventoryStockSummary> InventoryStocks { get; } = [];
    public ObservableCollection<QualityInspectionSummary> Inspections { get; } = [];
    public ObservableCollection<MaintenanceTaskSummary> MaintenanceTasks { get; } = [];
    public ObservableCollection<RepairOrderSummary> RepairOrders { get; } = [];
    public ObservableCollection<OfflineCommandRecord> OfflineCommands { get; } = [];

    public IReadOnlyList<MobileChoice<int>> JudgeResultOptions { get; } =
    [
        new() { Value = 1, Label = "合格" },
        new() { Value = 2, Label = "不合格" },
        new() { Value = 3, Label = "让步接收" }
    ];

    public IReadOnlyList<MobileChoice<int>> RepairLevelOptions { get; } =
    [
        new() { Value = 1, Label = "一般" },
        new() { Value = 2, Label = "重要" },
        new() { Value = 3, Label = "紧急" }
    ];

    private static readonly IReadOnlyList<MobileChoice<int>> AllRepairStatusOptions =
    [
        new() { Value = 1, Label = "维修中" },
        new() { Value = 2, Label = "已完成" },
        new() { Value = 3, Label = "已关闭" }
    ];

    public IReadOnlyList<MobileChoice<int>> AvailableRepairStatusOptions =>
        AllRepairStatusOptions.Where(x => x.Value > (SelectedRepairOrder?.RepairStatus ?? 0)).ToArray();

    public IReadOnlyList<string> MaintenanceResultOptions { get; } = ["正常", "异常"];
    public string ReportQualifiedQtyPreview =>
        decimal.TryParse(ReportQtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) &&
        decimal.TryParse(ReportDefectQtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var defect)
            ? Math.Max(0, qty - defect).ToString("0.####", CultureInfo.InvariantCulture)
            : "填写报工数量后自动计算";
    public bool CanHandleSelectedRepair => SelectedRepairOrder is { RepairStatus: < 2 } && AvailableRepairStatusOptions.Count > 0;
    public bool CanSubmitReport => SelectedWorkOrder is not null;
    public bool CanSubmitIssue => SelectedWorkOrder is not null && SelectedIssueMaterial is not null &&
        SelectedWarehouse is not null && SelectedIssueStock is not null;
    public bool CanSubmitJudge => SelectedInspection is not null && SelectedJudgeResult is not null;
    public bool CanSubmitMaintenance => SelectedMaintenanceTask is not null;
    public bool CanCreateRepair => SelectedRepairEquipment is not null;
    public string QueueSummary
    {
        get
        {
            var attention = OfflineCommands.Count(x => x.State is OfflineCommandStates.Failed or
                OfflineCommandStates.DeadLetter or OfflineCommandStates.UnknownNeedsReconciliation);
            return attention > 0 ? $"{attention} 项需核对" : $"{OfflineCommands.Count} 项待同步";
        }
    }

    public bool IsLoggedOut => !IsLoggedIn;
    public string ConnectionSummary => $"当前租户 {TenantCode} · {ServerBaseUrl}";
    public bool IsWorkOrdersPage => SelectedMenu?.Key == "work-orders";
    public bool IsOperationReportPage => SelectedMenu?.Key == "operation-report";
    public bool IsProductionPage => IsWorkOrdersPage || IsOperationReportPage;
    public bool IsMaterialIssuePage => SelectedMenu?.Key == "material-issue";
    public bool IsQualityPage => SelectedMenu?.Key == "quality-inspection";
    public bool IsMaintenancePage => SelectedMenu?.Key == "maintenance-tasks";
    public bool IsRepairPage => SelectedMenu?.Key == "repair-orders";
    public bool HasMobilePages => Menus.Count > 0;
    public bool HasNoMobilePages => IsLoggedIn && Menus.Count == 0;
    public string SelectedWorkOrderLabel => SelectedWorkOrder is null
        ? "未选择工单"
        : $"{SelectedWorkOrder.Code} · {SelectedWorkOrder.Name} · 数量 {SelectedWorkOrder.Qty}";
    public string SelectedInspectionLabel => SelectedInspection is null
        ? "未选择检验单"
        : $"{SelectedInspection.Code} · 抽检量 {SelectedInspection.SampleQty}";
    public string SelectedTaskLabel => SelectedMaintenanceTask is null
        ? "未选择点检/保养任务"
        : $"{SelectedMaintenanceTask.Code} · {SelectedMaintenanceTask.EquipmentName ?? "设备待匹配"}";
    public string SelectedRepairOrderLabel => SelectedRepairOrder is null
        ? "未选择维修单"
        : $"{SelectedRepairOrder.Code} · {SelectedRepairOrder.EquipmentName ?? "设备待匹配"} · 状态 {SelectedRepairOrder.RepairStatus}";

    public MainViewModel(MobileApiClient api, OfflineCommandStore store, OfflineCommandProcessor processor)
    {
        _api = api;
        _store = store;
        _processor = processor;
    }

    partial void OnIsLoggedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLoggedOut));
        OnPropertyChanged(nameof(HasNoMobilePages));
        UpdateStatusVisibility();
    }

    partial void OnServerBaseUrlChanged(string value) => OnPropertyChanged(nameof(ConnectionSummary));

    partial void OnTenantCodeChanged(string value) => OnPropertyChanged(nameof(ConnectionSummary));

    partial void OnStatusMessageChanged(string value) => UpdateStatusVisibility();

    partial void OnIsBusyChanged(bool value) => UpdateStatusVisibility();

    private void UpdateStatusVisibility()
    {
        var routine = StatusMessage.StartsWith("登录成功", StringComparison.Ordinal) ||
            StatusMessage.StartsWith("已刷新：", StringComparison.Ordinal);
        ShowStatusMessage = !IsLoggedIn || IsBusy || !routine;
    }

    partial void OnSelectedMenuChanged(MobileMenuEntry? value)
    {
        OnPropertyChanged(nameof(IsWorkOrdersPage));
        OnPropertyChanged(nameof(IsOperationReportPage));
        OnPropertyChanged(nameof(IsProductionPage));
        OnPropertyChanged(nameof(IsMaterialIssuePage));
        OnPropertyChanged(nameof(IsQualityPage));
        OnPropertyChanged(nameof(IsMaintenancePage));
        OnPropertyChanged(nameof(IsRepairPage));
        if (IsLoggedIn && value is not null)
            _ = RefreshCurrentPageAsync();
    }

    partial void OnSelectedWorkOrderChanged(WorkOrderSummary? value)
    {
        OnPropertyChanged(nameof(SelectedWorkOrderLabel));
        OnPropertyChanged(nameof(CanSubmitReport));
        OnPropertyChanged(nameof(CanSubmitIssue));
        WorkOrderMaterials.Clear();
        ReportProcessOptions.Clear();
        InventoryStocks.Clear();
        SelectedIssueMaterial = null;
        SelectedIssueStock = null;
        InventoryHint = "先选择工单、用料和仓库，再选择有库存的批次。";
        if (value is not null && (IsMaterialIssuePage || IsOperationReportPage))
            _ = LoadWorkOrderContextAsync(value.Id);
    }

    partial void OnSelectedIssueMaterialChanged(WorkOrderMaterialSummary? value)
    {
        OnPropertyChanged(nameof(CanSubmitIssue));
        if (value is not null && IsMaterialIssuePage)
            _ = LoadInventoryStocksAsync();
    }

    partial void OnSelectedWarehouseChanged(MobileOption? value)
    {
        OnPropertyChanged(nameof(CanSubmitIssue));
        if (IsMaterialIssuePage && SelectedIssueMaterial is not null)
            _ = LoadInventoryStocksAsync();
    }

    partial void OnSelectedIssueStockChanged(InventoryStockSummary? value)
    {
        OnPropertyChanged(nameof(CanSubmitIssue));
        IssueBatchNo = value?.BatchNo ?? string.Empty;
        IssueLocation = value?.Location ?? string.Empty;
    }

    partial void OnReportQtyTextChanged(string value) => OnPropertyChanged(nameof(ReportQualifiedQtyPreview));
    partial void OnReportDefectQtyTextChanged(string value) => OnPropertyChanged(nameof(ReportQualifiedQtyPreview));

    partial void OnSelectedRepairOrderChanged(RepairOrderSummary? value)
    {
        OnPropertyChanged(nameof(SelectedRepairOrderLabel));
        OnPropertyChanged(nameof(AvailableRepairStatusOptions));
        OnPropertyChanged(nameof(CanHandleSelectedRepair));
        SelectedRepairStatus = AvailableRepairStatusOptions.FirstOrDefault();
    }

    partial void OnSelectedInspectionChanged(QualityInspectionSummary? value)
    {
        OnPropertyChanged(nameof(SelectedInspectionLabel));
        OnPropertyChanged(nameof(CanSubmitJudge));
        if (value is not null)
        {
            JudgeQualifiedQtyText = SelectedJudgeResult?.Value == 2
                ? "0" : value.SampleQty.ToString(CultureInfo.InvariantCulture);
            JudgeDefectQtyText = SelectedJudgeResult?.Value == 2
                ? value.SampleQty.ToString(CultureInfo.InvariantCulture) : "0";
        }
    }

    partial void OnSelectedJudgeResultChanged(MobileChoice<int>? value)
    {
        OnPropertyChanged(nameof(CanSubmitJudge));
        if (SelectedInspection is null) return;
        JudgeQualifiedQtyText = value?.Value == 2
            ? "0" : SelectedInspection.SampleQty.ToString(CultureInfo.InvariantCulture);
        JudgeDefectQtyText = value?.Value == 2
            ? SelectedInspection.SampleQty.ToString(CultureInfo.InvariantCulture) : "0";
    }

    partial void OnSelectedMaintenanceTaskChanged(MaintenanceTaskSummary? value)
    {
        OnPropertyChanged(nameof(SelectedTaskLabel));
        OnPropertyChanged(nameof(CanSubmitMaintenance));
    }

    partial void OnSelectedRepairEquipmentChanged(MobileOption? value)
        => OnPropertyChanged(nameof(CanCreateRepair));

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        var stage = "登录";
        StatusMessage = "正在连接 API 并登录…";
        try
        {
            await _store.InitializeAsync();
            var login = await _api.LoginAsync(ServerBaseUrl, TenantCode, Username, Password);
            stage = "读取用户权限";
            StatusMessage = "登录成功，正在读取角色菜单授权…";
            var bootstrap = await _api.GetAsync<MobileBootstrapResponse>("api/mobile/bootstrap");
            stage = "读取后台选项";
            StatusMessage = "正在同步工单、仓库、设备和工序选项…";
            await LoadLookupsAsync();
            _tenantId = login.TenantId;
            _currentUserId = bootstrap.User.Id;
            _ownerKey = $"tenant:{login.TenantId}:user:{bootstrap.User.Id}";
            CurrentUserDisplay = $"{bootstrap.User.Nickname} ({bootstrap.User.Username}) · {login.TenantCode}";
            CurrentRolesText = bootstrap.Roles.Count == 0 ? "未分配有效角色" : string.Join("、", bootstrap.Roles);
            Menus.Clear();
            foreach (var menu in bootstrap.Menus.OrderBy(x => x.Sort)) Menus.Add(menu);
            IsLoggedIn = true;
            Password = string.Empty;
            SelectedJudgeResult = null;
            SelectedRepairLevel = RepairLevelOptions[0];
            SelectedMenu = Menus.FirstOrDefault();
            await _processor.RecoverInterruptedCommandsAsync(_ownerKey);
            await RefreshQueueAsync();
            await SyncQueueCoreAsync();
            StartBackgroundSync();
            StatusMessage = Menus.Count == 0
                ? "登录成功，但当前用户没有已授予的移动菜单页面。请管理员为角色分配菜单。"
                : "登录成功。页面由服务端角色菜单授权提供；API 仍由服务端认证和租户隔离保护。";
        }
        catch (Exception ex)
        {
            _api.ClearSession();
            IsLoggedIn = false;
            StatusMessage = $"{stage}失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        StopBackgroundSync();
        try
        {
            if (!string.IsNullOrWhiteSpace(_ownerKey))
                await _api.PostEmptyAsync("api/auth/logout");
        }
        catch
        {
            // Clearing the in-memory token is still mandatory when the server is unreachable.
        }
        _api.ClearSession();
        _ownerKey = null;
        _tenantId = 0;
        _currentUserId = 0;
        IsLoggedIn = false;
        _lookup = null;
        Menus.Clear();
        WorkOrders.Clear();
        SelectableWorkOrders.Clear();
        WorkOrderMaterials.Clear();
        WarehouseOptions.Clear();
        ProcessOptions.Clear();
        ReportProcessOptions.Clear();
        DefectOptions.Clear();
        EquipmentOptions.Clear();
        InventoryStocks.Clear();
        Inspections.Clear();
        MaintenanceTasks.Clear();
        RepairOrders.Clear();
        OfflineCommands.Clear();
        OnPropertyChanged(nameof(QueueSummary));
        SelectedMenu = null;
        SelectedWorkOrder = null;
        SelectedIssueMaterial = null;
        SelectedIssueStock = null;
        SelectedWarehouse = null;
        SelectedReportProcess = null;
        SelectedReportDefect = null;
        SelectedJudgeDefect = null;
        SelectedRepairEquipment = null;
        SelectedInspection = null;
        SelectedMaintenanceTask = null;
        SelectedRepairOrder = null;
        CurrentUserDisplay = string.Empty;
        CurrentRolesText = string.Empty;
        StatusMessage = "已退出登录。访问令牌未写入本地数据库。";
    }

    [RelayCommand]
    private Task SelectMenuAsync(MobileMenuEntry? menu)
    {
        if (menu is not null) SelectedMenu = menu;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RefreshCurrentPageAsync()
    {
        if (!IsLoggedIn || SelectedMenu is null) return;
        IsBusy = true;
        try
        {
            switch (SelectedMenu.Key)
            {
                case "work-orders":
                case "operation-report":
                case "material-issue":
                    await LoadWorkOrdersAsync();
                    break;
                case "quality-inspection":
                    await LoadInspectionsAsync();
                    break;
                case "maintenance-tasks":
                    await LoadMaintenanceTasksAsync();
                    break;
                case "repair-orders":
                    await LoadRepairOrdersAsync();
                    break;
            }
            StatusMessage = $"已刷新：{SelectedMenu.Title}";
        }
        catch (Exception ex)
        {
            StatusMessage = "刷新失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task QueueWorkReportAsync()
    {
        try
        {
            if (SelectedWorkOrder is null) throw new InvalidOperationException("先选择工单。");
            var qty = ParseDecimal(ReportQtyText, "报工数量");
            var defectQty = ParseDecimal(ReportDefectQtyText, "不良数量");
            var qualifiedQty = qty - defectQty;
            var request = new WorkReportRequest(
                SelectedWorkOrder.Id,
                SelectedReportProcess?.Id,
                null,
                qty,
                qualifiedQty,
                defectQty,
                ParseDecimal(ReportHoursText, "工时"),
                SelectedReportDefect?.Id,
                EmptyToNull(ReportRemark));
            if (request.Qty <= 0 || request.DefectQty < 0 || request.QualifiedQty < 0)
                throw new InvalidOperationException("报工数量必须大于 0，不良数量不能超过报工数量。");
            await EnqueueAndSyncAsync(NewRecord(OfflineCommandKinds.WorkReport, OfflineCommandStages.Submit,
                SelectedWorkOrder.Id, request));
            ReportQtyText = string.Empty;
            ReportDefectQtyText = "0";
            ReportHoursText = "0";
            ReportRemark = string.Empty;
        }
        catch (Exception ex) { StatusMessage = "报工未入队：" + ex.Message; }
    }

    [RelayCommand]
    private async Task QueueMaterialIssueAsync()
    {
        try
        {
            if (SelectedWorkOrder is null) throw new InvalidOperationException("先选择工单。");
            if (SelectedIssueMaterial is null) throw new InvalidOperationException("选择一条工单用料行。");
            if (SelectedWarehouse is null) throw new InvalidOperationException("请选择领料仓库。");
            if (SelectedIssueStock is null) throw new InvalidOperationException("请选择有可用库存的批次/库位。没有选项时请先补货或更换仓库。");
            var qty = ParseDecimal(IssueQtyText, "领料数量");
            if (qty > SelectedIssueStock.AvailableQty)
                throw new InvalidOperationException($"所选批次/库位可用 {SelectedIssueStock.AvailableQty}，不足以领用 {qty}。");
            var request = new MaterialIssueDraftRequest(
                SelectedWarehouse.Id,
                [new MaterialIssueLineRequest(SelectedIssueMaterial.Id,
                    qty, EmptyToNull(IssueBatchNo), EmptyToNull(IssueLocation))],
                EmptyToNull(IssueRemark));
            if (request.Items[0].Qty <= 0) throw new InvalidOperationException("领料数量必须大于 0。");
            await EnqueueAndSyncAsync(NewRecord(OfflineCommandKinds.MaterialIssue,
                OfflineCommandStages.CreateMaterialDraft, SelectedWorkOrder.Id, request));
            IssueQtyText = string.Empty;
            IssueRemark = string.Empty;
        }
        catch (Exception ex) { StatusMessage = "领料命令未入队：" + ex.Message; }
    }

    [RelayCommand]
    private async Task QueueQualityJudgeAsync()
    {
        try
        {
            if (SelectedInspection is null) throw new InvalidOperationException("先选择检验单。");
            if (SelectedJudgeResult is null) throw new InvalidOperationException("请选择判定结果。");
            var request = new QualityJudgeRequest(
                SelectedJudgeResult.Value,
                ParseDecimal(JudgeQualifiedQtyText, "合格数量"),
                ParseDecimal(JudgeDefectQtyText, "不合格数量"),
                SelectedJudgeDefect?.Id, EmptyToNull(JudgeRemark));
            if (request.QualifiedQty + request.DefectQty != SelectedInspection.SampleQty)
                throw new InvalidOperationException("合格数量与不合格数量之和必须等于抽检数量。");
            await EnqueueAndSyncAsync(NewRecord(OfflineCommandKinds.QualityJudge,
                OfflineCommandStages.Submit, SelectedInspection.Id, request));
        }
        catch (Exception ex) { StatusMessage = "判定命令未入队：" + ex.Message; }
    }

    [RelayCommand]
    private async Task QueueMaintenanceExecutionAsync()
    {
        try
        {
            if (SelectedMaintenanceTask is null) throw new InvalidOperationException("先选择点检/保养任务。");
            var request = new MaintenanceExecuteRequest(EmptyToNull(MaintenanceResult),
                ParseDecimal(MaintenanceCostText, "费用"), EmptyToNull(MaintenanceRemark));
            await EnqueueAndSyncAsync(NewRecord(OfflineCommandKinds.MaintenanceExecute,
                OfflineCommandStages.Submit, SelectedMaintenanceTask.Id, request));
        }
        catch (Exception ex) { StatusMessage = "任务执行命令未入队：" + ex.Message; }
    }

    [RelayCommand]
    private async Task QueueRepairOrderAsync()
    {
        try
        {
            if (SelectedRepairEquipment is null) throw new InvalidOperationException("请选择故障设备。");
            if (SelectedRepairLevel is null) throw new InvalidOperationException("请选择故障等级。");
            var request = new RepairOrderCreateRequest(SelectedRepairEquipment.Id,
                RepairFaultDescription.Trim(), SelectedRepairLevel.Value);
            if (string.IsNullOrWhiteSpace(request.FaultDesc))
                throw new InvalidOperationException("请填写故障描述。");
            await EnqueueAndSyncAsync(NewRecord(OfflineCommandKinds.RepairOrderCreate,
                OfflineCommandStages.Submit, request.EquipmentId, request));
            RepairFaultDescription = string.Empty;
        }
        catch (Exception ex) { StatusMessage = "报修命令未入队：" + ex.Message; }
    }

    [RelayCommand]
    private async Task QueueRepairHandleAsync()
    {
        try
        {
            if (SelectedRepairOrder is null) throw new InvalidOperationException("先选择维修单。");
            if (SelectedRepairStatus is null) throw new InvalidOperationException("该维修单当前没有可用的后续状态。");
            var request = new RepairOrderHandleRequest(EmptyToNull(RepairSolution),
                SelectedRepairStatus.Value, ParseDecimal(RepairCostText, "维修费用"),
                ParseInt(RepairDowntimeMinutesText, "停机分钟数"));
            if (request.RepairStatus is < 1 or > 3 || request.Cost < 0 || request.DowntimeMinutes < 0)
                throw new InvalidOperationException("维修状态须为 1/2/3；费用与停机分钟数不能为负数。");
            await EnqueueAndSyncAsync(NewRecord(OfflineCommandKinds.RepairOrderHandle,
                OfflineCommandStages.Submit, SelectedRepairOrder.Id, request));
        }
        catch (Exception ex) { StatusMessage = "维修处理命令未入队：" + ex.Message; }
    }

    [RelayCommand]
    private async Task SyncQueueAsync() => await SyncQueueCoreAsync();

    [RelayCommand]
    private async Task ReconcileUnknownAsync(OfflineCommandRecord? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(_ownerKey)) return;
        IsBusy = true;
        try
        {
            StatusMessage = await _processor.ReconcileUnknownAsync(row.LocalId, _ownerKey);
            // Readback is read-only; it may clear a command only after the exact server result matches.
            await SyncQueueCoreAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "只读回查失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            await RefreshQueueAsync();
        }
    }

    private async Task EnqueueAndSyncAsync(OfflineCommandRecord row)
    {
        if (string.IsNullOrWhiteSpace(_ownerKey)) throw new InvalidOperationException("请先登录。");
        // Durable write occurs before any network request.
        await _store.EnqueueAsync(row);
        await RefreshQueueAsync();
        StatusMessage = $"命令已保存到本机离线队列：{row.CommandId}";
        await SyncQueueCoreAsync();
    }

    private async Task SyncQueueCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(_ownerKey)) return;
        try
        {
            await _processor.ProcessPendingAsync(_ownerKey);
            await _processor.PollJobsAsync(_ownerKey);
            await RefreshQueueAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "队列同步/任务轮询失败：" + ex.Message;
        }
    }

    private OfflineCommandRecord NewRecord<T>(string kind, string stage, long? targetId, T request)
        => new()
        {
            OwnerKey = _ownerKey ?? string.Empty,
            TenantId = _tenantId,
            OwnerUserId = _currentUserId,
            Kind = kind,
            Stage = stage,
            TargetId = targetId,
            PayloadJson = JsonSerializer.Serialize(request, JsonOptions),
            CommandId = Guid.NewGuid().ToString("D"),
            State = OfflineCommandStates.Pending
        };

    private async Task LoadWorkOrdersAsync()
    {
        var result = await _api.GetAsync<MobilePagedResult<WorkOrderSummary>>("api/work-order?page=1&pageSize=500");
        var selectedId = SelectedWorkOrder?.Id;
        Replace(WorkOrders, result.List);
        Replace(SelectableWorkOrders, result.List.Where(x => x.WorkOrderStatus is 1 or 2));
        var choices = IsWorkOrdersPage ? WorkOrders : SelectableWorkOrders;
        SelectedWorkOrder = (selectedId.HasValue ? choices.FirstOrDefault(x => x.Id == selectedId.Value) : null)
            ?? (choices.Count == 1 ? choices[0] : null);
    }

    private async Task LoadLookupsAsync()
    {
        _lookup = await _api.GetAsync<MobileLookupResponse>("api/lookup");
        Replace(WarehouseOptions, _lookup.Warehouses);
        Replace(ProcessOptions, _lookup.Processes);
        Replace(DefectOptions, _lookup.Defects);
        Replace(EquipmentOptions, _lookup.Equipments);
        if (WarehouseOptions.Count == 1)
            SelectedWarehouse = WarehouseOptions[0];
    }

    private async Task LoadWorkOrderContextAsync(long workOrderId)
    {
        var requestId = ++_workOrderContextRequest;
        try
        {
            var details = await _api.GetAsync<WorkOrderDetailsResponse>($"api/work-order/{workOrderId}");
            if (requestId != _workOrderContextRequest || SelectedWorkOrder?.Id != workOrderId) return;
            foreach (var material in details.Materials)
            {
                var product = _lookup?.Products.FirstOrDefault(x => x.Id == material.ProductId);
                material.Code = product?.Code;
                material.Name = product?.Name ?? $"物料 {material.ProductId}";
            }
            Replace(WorkOrderMaterials, details.Materials);

            var selectedMaterialId = SelectedIssueMaterial?.Id;
            SelectedIssueMaterial = selectedMaterialId.HasValue
                ? WorkOrderMaterials.FirstOrDefault(x => x.Id == selectedMaterialId.Value)
                : WorkOrderMaterials.Count == 1 ? WorkOrderMaterials[0] : null;

            var previousProcessId = SelectedReportProcess?.Id;
            IEnumerable<MobileOption> availableProcesses = ProcessOptions;
            var routingId = details.WorkOrder.RoutingId ?? SelectedWorkOrder.RoutingId;
            if (routingId.HasValue)
            {
                availableProcesses = [];
                try
                {
                    var routing = await _api.GetAsync<RoutingDetailsResponse>($"api/routing/{routingId.Value}");
                    if (requestId != _workOrderContextRequest || SelectedWorkOrder?.Id != workOrderId) return;
                    var ids = routing.Steps.OrderBy(x => x.Seq).Select(x => x.ProcessId).ToHashSet();
                    availableProcesses = ProcessOptions.Where(x => ids.Contains(x.Id)).ToList();
                }
                catch (Exception ex)
                {
                    if (requestId == _workOrderContextRequest)
                        StatusMessage = "读取工单工艺路线失败，暂不提供其他工序选项：" + ex.Message;
                }
            }
            var processList = availableProcesses.ToList();
            Replace(ReportProcessOptions, processList);
            SelectedReportProcess = previousProcessId.HasValue
                ? ReportProcessOptions.FirstOrDefault(x => x.Id == previousProcessId.Value)
                : ReportProcessOptions.Count == 1 ? ReportProcessOptions[0] : null;
        }
        catch (Exception ex) { StatusMessage = "读取工单关联用料/工序失败：" + ex.Message; }
    }

    private async Task LoadInventoryStocksAsync()
    {
        var requestId = ++_inventoryRequest;
        var warehouse = SelectedWarehouse;
        var material = SelectedIssueMaterial;
        InventoryStocks.Clear();
        SelectedIssueStock = null;
        if (warehouse is null || material is null)
        {
            InventoryStocks.Clear();
            SelectedIssueStock = null;
            InventoryHint = "先选择工单、用料和仓库。";
            return;
        }

        try
        {
            InventoryHint = "正在匹配可用库存…";
            var path = $"api/inventory/stock?page=1&pageSize=200&warehouseId={warehouse.Id}&productId={material.ProductId}";
            var result = await _api.GetAsync<MobilePagedResult<InventoryStockSummary>>(path);
            if (requestId != _inventoryRequest || SelectedWarehouse?.Id != warehouse.Id ||
                SelectedIssueMaterial?.Id != material.Id) return;
            Replace(InventoryStocks, result.List.Where(x => x.AvailableQty > 0));
            SelectedIssueStock = null;
            InventoryHint = InventoryStocks.Count == 0
                ? "当前仓库无该物料的可用库存；请更换仓库或先补货。"
                : "可按批次、库位搜索；提交前必须选中一项可用库存。";
        }
        catch (Exception ex)
        {
            if (requestId == _inventoryRequest)
            {
                InventoryStocks.Clear();
                SelectedIssueStock = null;
                InventoryHint = "库存查询失败，请刷新后再试，暂不允许盲目领料。";
                StatusMessage = "读取仓库批次/库位选项失败：" + ex.Message;
            }
        }
    }

    private async Task LoadInspectionsAsync()
    {
        var result = await _api.GetAsync<MobilePagedResult<QualityInspectionSummary>>("api/quality-inspection?page=1&pageSize=200&result=0");
        Replace(Inspections, result.List);
        if (SelectedInspection is not null && Inspections.All(x => x.Id != SelectedInspection.Id))
            SelectedInspection = null;
    }

    private async Task LoadMaintenanceTasksAsync()
    {
        var result = await _api.GetAsync<MobilePagedResult<MaintenanceTaskSummary>>("api/maintenance-task?page=1&pageSize=200&taskStatus=0");
        foreach (var task in result.List)
            task.EquipmentName = EquipmentOptions.FirstOrDefault(x => x.Id == task.EquipmentId)?.DisplayText;
        Replace(MaintenanceTasks, result.List);
        if (SelectedMaintenanceTask is not null && MaintenanceTasks.All(x => x.Id != SelectedMaintenanceTask.Id))
            SelectedMaintenanceTask = null;
    }

    private async Task LoadRepairOrdersAsync()
    {
        var result = await _api.GetAsync<MobilePagedResult<RepairOrderSummary>>("api/repair-order?page=1&pageSize=100");
        foreach (var order in result.List)
            order.EquipmentName = EquipmentOptions.FirstOrDefault(x => x.Id == order.EquipmentId)?.DisplayText;
        var selectedId = SelectedRepairOrder?.Id;
        Replace(RepairOrders, result.List);
        SelectedRepairOrder = (selectedId.HasValue ? RepairOrders.FirstOrDefault(x => x.Id == selectedId.Value) : null)
            ?? (RepairOrders.Count == 1 ? RepairOrders[0] : null);
    }

    private async Task RefreshQueueAsync()
    {
        if (string.IsNullOrWhiteSpace(_ownerKey)) return;
        var rows = await _store.ListAsync(_ownerKey);
        Replace(OfflineCommands, rows);
        OnPropertyChanged(nameof(QueueSummary));
    }

    private void StartBackgroundSync()
    {
        StopBackgroundSync();
        _backgroundSync = new CancellationTokenSource();
        _ = BackgroundSyncLoopAsync(_backgroundSync.Token);
    }

    private void StopBackgroundSync()
    {
        _backgroundSync?.Cancel();
        _backgroundSync?.Dispose();
        _backgroundSync = null;
    }

    private async Task BackgroundSyncLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
                if (string.IsNullOrWhiteSpace(_ownerKey)) continue;
                await _processor.ProcessPendingAsync(_ownerKey, cancellationToken);
                await _processor.PollJobsAsync(_ownerKey, cancellationToken);
                var rows = await _store.ListAsync(_ownerKey);
                Dispatcher.UIThread.Post(() =>
                {
                    Replace(OfflineCommands, rows);
                    OnPropertyChanged(nameof(QueueSummary));
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => StatusMessage = "后台同步失败：" + ex.Message);
            }
        }
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items) collection.Add(item);
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();


    private static int ParseInt(string value, string field)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : throw new InvalidOperationException($"{field}必须是整数。");

    private static decimal ParseDecimal(string value, string field)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result : throw new InvalidOperationException($"{field}必须是有效数字（小数点使用 .）。");
}
