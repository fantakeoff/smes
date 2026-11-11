using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmesMobile.Core;

/// <summary>
/// Sends persisted commands and reconciles asynchronous jobs using the server's existing routes.
/// Failed and dead-lettered rows are never selected for automatic replay.
/// </summary>
public sealed class OfflineCommandProcessor(MobileApiClient api, OfflineCommandStore store)
{
    private const int MaxSafeAttempts = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public async Task RecoverInterruptedCommandsAsync(string ownerKey, CancellationToken cancellationToken = default)
    {
        var rows = await store.ListAsync(ownerKey);
        foreach (var row in rows.Where(x => x.State == OfflineCommandStates.Sending))
        {
            if (CanReplaySafely(row))
            {
                row.State = OfflineCommandStates.Pending;
                row.LastError = "前次发送中断；将使用原幂等键安全重试。";
            }
            else
            {
                row.State = OfflineCommandStates.UnknownNeedsReconciliation;
                row.LastError = "前次发送结果不确定；当前服务器路由没有可验证的幂等重放契约，已停止自动重试。请先在服务器业务列表核对。";
            }
            await store.UpdateAsync(row);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<string> ReconcileUnknownAsync(int localId, string ownerKey, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var row = (await store.ListAsync(ownerKey)).FirstOrDefault(x => x.LocalId == localId);
            if (row is null || row.State != OfflineCommandStates.UnknownNeedsReconciliation)
                throw new InvalidOperationException("只允许读取回查 unknown-needs-reconciliation 命令。");

            switch (row.Kind)
            {
                case OfflineCommandKinds.MaterialIssue:
                    return await ReconcileMaterialDraftAsync(row, cancellationToken);
                case OfflineCommandKinds.RepairOrderCreate:
                    return await ReadBackRepairOrderAsync(row, cancellationToken);
                case OfflineCommandKinds.RepairOrderHandle:
                    return await ReadBackRepairHandleAsync(row, cancellationToken);
                case OfflineCommandKinds.MaintenanceExecute:
                    return await ReadBackMaintenanceTaskAsync(row, cancellationToken);
                default:
                    row.LastError = "当前命令类型没有已知的只读回查路由；未执行任何业务写入。";
                    await store.UpdateAsync(row);
                    return row.LastError;
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<string> ReconcileMaterialDraftAsync(OfflineCommandRecord row, CancellationToken cancellationToken)
    {
        if (!row.TargetId.HasValue || row.Stage != OfflineCommandStages.CreateMaterialDraft)
            throw new InvalidOperationException("领料草稿命令缺少工单 ID 或已进入其他阶段。");
        var request = Deserialize<MaterialIssueDraftRequest>(row.PayloadJson);
        var workOrder = await api.GetAsync<WorkOrderDetailsResponse>($"api/work-order/{row.TargetId.Value}", cancellationToken);
        var sourceCode = workOrder.WorkOrder.Code;
        if (string.IsNullOrWhiteSpace(sourceCode))
            throw new InvalidOperationException("工单读回缺少工单编码，无法安全匹配领料草稿。");

        var expectedBillCode = MaterialBillCode(row);
        var bills = await api.GetAsync<MobilePagedResult<StockBillSummary>>(
            $"api/stock-bill?page=1&pageSize=50&billType=2&keyword={Uri.EscapeDataString(expectedBillCode)}", cancellationToken);
        var expected = new List<string>();
        foreach (var item in request.Items)
        {
            var material = workOrder.Materials.FirstOrDefault(x => x.Id == item.MaterialId)
                ?? throw new InvalidOperationException($"工单用料行 {item.MaterialId} 已无法读回，不能自动匹配草稿。");
            expected.Add(Fingerprint(material.ProductId, item.Qty, item.BatchNo, item.Location));
        }
        expected.Sort(StringComparer.Ordinal);

        var exactMatches = new List<StockBillSummary>();
        foreach (var bill in bills.List.Where(x => x.BillType == 2 && x.Code == expectedBillCode &&
                     x.SourceId == row.TargetId && x.WarehouseId == request.WarehouseId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var details = await api.GetAsync<StockBillDetails>($"api/stock-bill/{bill.Id}", cancellationToken);
            var actual = details.Lines.Select(x => Fingerprint(x.ProductId, x.Qty, x.BatchNo, x.Location))
                .Order(StringComparer.Ordinal).ToList();
            if (details.Code == expectedBillCode && details.SourceId == row.TargetId && details.BillType == 2 && details.BillStatus == 0 &&
                details.WarehouseId == request.WarehouseId && expected.SequenceEqual(actual, StringComparer.Ordinal))
                exactMatches.Add(bill);
        }

        if (exactMatches.Count == 1)
        {
            row.BillId = exactMatches[0].Id;
            row.Stage = OfflineCommandStages.SubmitMaterialDraft;
            row.State = OfflineCommandStates.Pending;
            row.ServerStatus = "draft-readback-confirmed";
            row.LastError = "只读回查找到唯一且明细完全匹配的现有草稿；未新建草稿。可安全提交该单据。";
            await store.UpdateAsync(row);
            return row.LastError;
        }

        row.LastError = exactMatches.Count == 0
            ? "只读回查未找到唯一匹配草稿。命令保持 unknown；没有创建新草稿。"
            : $"只读回查发现 {exactMatches.Count} 个完全匹配草稿，无法确认本次操作对应哪一张。命令保持 unknown；没有写入。";
        await store.UpdateAsync(row);
        return row.LastError;
    }

    private async Task<string> ReadBackRepairOrderAsync(OfflineCommandRecord row, CancellationToken cancellationToken)
    {
        if (row.TenantId <= 0) throw new InvalidOperationException("报修命令缺少租户 ID，无法安全匹配单据。");
        var request = Deserialize<RepairOrderCreateRequest>(row.PayloadJson);
        var expectedCode = RepairOrderCode(row);
        var result = await api.GetAsync<MobilePagedResult<RepairOrderSummary>>(
            $"api/repair-order?page=1&pageSize=20&keyword={Uri.EscapeDataString(expectedCode)}", cancellationToken);
        var candidates = result.List.Where(x => string.Equals(x.Code, expectedCode, StringComparison.Ordinal)).ToList();
        if (candidates.Count == 1)
        {
            var details = await api.GetAsync<RepairOrderDetails>($"api/repair-order/{candidates[0].Id}", cancellationToken);
            if (RepairCreateMatches(details, row, request, expectedCode))
            {
                await store.DeleteAsync(row);
                return $"只读回查通过 UUID 派生单号 {expectedCode} 找到并验证了原维修单；本地命令已完成核对。";
            }
        }
        row.LastError = candidates.Count == 0
            ? $"只读回查未找到 UUID 派生单号 {expectedCode}。命令仍为 unknown；没有再次创建。"
            : "只读回查返回的维修单与本地精确负载/提交人不匹配。命令仍为 unknown；没有写入。";
        await store.UpdateAsync(row);
        return row.LastError;
    }

    private async Task<string> ReadBackMaintenanceTaskAsync(OfflineCommandRecord row, CancellationToken cancellationToken)
    {
        if (!row.TargetId.HasValue) throw new InvalidOperationException("点检任务缺少服务器 ID。");
        var request = Deserialize<MaintenanceExecuteRequest>(row.PayloadJson);
        var task = await api.GetAsync<MaintenanceTaskDetails>($"api/maintenance-task/{row.TargetId.Value}", cancellationToken);
        if (MaintenanceExecutionMatches(task, row, request))
        {
            await store.DeleteAsync(row);
            return $"只读回查确认任务 {task.Id} 的执行人、状态及负载均与本地命令一致；本地命令已完成核对。";
        }
        row.LastError = "只读回查到任务，但执行人、状态或执行负载不完全匹配。命令仍为 unknown；没有再次执行。";
        await store.UpdateAsync(row);
        return row.LastError;
    }

    private async Task<string> ReadBackRepairHandleAsync(OfflineCommandRecord row, CancellationToken cancellationToken)
    {
        if (!row.TargetId.HasValue) throw new InvalidOperationException("维修处理命令缺少服务器 ID。");
        var request = Deserialize<RepairOrderHandleRequest>(row.PayloadJson);
        var order = await api.GetAsync<RepairOrderDetails>($"api/repair-order/{row.TargetId.Value}", cancellationToken);
        if (RepairHandleMatches(order, row, request))
        {
            await store.DeleteAsync(row);
            return $"只读回查确认维修单 {order.Id} 的处理人、状态及负载均与本地命令一致；本地命令已完成核对。";
        }
        row.LastError = "只读回查到维修单，但处理人、状态或处理负载不完全匹配。命令仍为 unknown；没有再次处理。";
        await store.UpdateAsync(row);
        return row.LastError;
    }

    private static string Fingerprint(long productId, decimal qty, string? batchNo, string? location)
        => string.Join('\u001f', productId.ToString(CultureInfo.InvariantCulture),
            qty.ToString(CultureInfo.InvariantCulture), batchNo ?? string.Empty, location ?? string.Empty);

    public async Task ProcessPendingAsync(string ownerKey, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var pending = (await store.ListAsync(ownerKey))
                .Where(x => x.State == OfflineCommandStates.Pending)
                .OrderBy(x => x.LocalId)
                .ToList();
            if (pending.Count == 0) return;
            if (!await api.IsServerReachableAsync(cancellationToken))
            {
                foreach (var row in pending)
                {
                    row.LastError = "服务器不可达；命令仍保存在本地，尚未发送。网络恢复后会继续处理。";
                    await store.UpdateAsync(row);
                }
                return;
            }
            foreach (var row in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                row.State = OfflineCommandStates.Sending;
                row.RetryCount++;
                row.LastError = null;
                await store.UpdateAsync(row);
                try
                {
                    await SendOneAsync(row, cancellationToken);
                }
                catch (MobileApiException ex)
                {
                    row.LastError = ex.Message;
                    if (ex.HttpStatusCode >= 500 && CanReplaySafely(row) && row.RetryCount < MaxSafeAttempts)
                        row.State = OfflineCommandStates.Pending;
                    else if (ex.HttpStatusCode >= 500 && HasNonIdempotentOutcome(row))
                        row.State = OfflineCommandStates.UnknownNeedsReconciliation;
                    else if (ex.HttpStatusCode >= 500)
                        row.State = OfflineCommandStates.DeadLetter;
                    else
                        row.State = OfflineCommandStates.Failed;
                    await store.UpdateAsync(row);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Preserve Sending: recovery will replay only commands with a real server-side
                    // idempotency contract; other commands will be dead-lettered on next login.
                    throw;
                }
                catch (Exception ex)
                {
                    row.LastError = ex.Message;
                    if (CanReplaySafely(row) && row.RetryCount < MaxSafeAttempts)
                        row.State = OfflineCommandStates.Pending;
                    else if (HasNonIdempotentOutcome(row))
                        row.State = OfflineCommandStates.UnknownNeedsReconciliation;
                    else
                        row.State = OfflineCommandStates.DeadLetter;
                    await store.UpdateAsync(row);
                }
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task PollJobsAsync(string ownerKey, CancellationToken cancellationToken = default)
    {
        var rows = (await store.ListAsync(ownerKey))
            .Where(x => x.State == OfflineCommandStates.AwaitingJob && x.ServerJobId.HasValue)
            .ToList();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = row.Kind switch
                {
                    OfflineCommandKinds.WorkReport => $"api/work-report/jobs/{row.ServerJobId}",
                    OfflineCommandKinds.MaterialIssue => $"api/material-issue/jobs/{row.ServerJobId}",
                    OfflineCommandKinds.QualityJudge => $"api/quality-inspection/jobs/{row.ServerJobId}",
                    _ => null
                };
                if (path is null)
                {
                    row.State = OfflineCommandStates.DeadLetter;
                    row.LastError = "本地任务类型没有已知的服务器状态路由。";
                    await store.UpdateAsync(row);
                    continue;
                }

                var data = await api.GetAsync<JsonElement>(path, cancellationToken);
                var status = ReadString(data, "status") ?? "unknown";
                row.ServerStatus = status;
                row.ServerAttempts = ReadInt(data, "attempts") ?? row.ServerAttempts;
                if (status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                {
                    // Async commands are removed only after the server's job endpoint confirms completion.
                    await store.DeleteAsync(row);
                    continue;
                }
                if (status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("dead-lettered", StringComparison.OrdinalIgnoreCase))
                {
                    row.State = OfflineCommandStates.DeadLetter;
                    row.LastError = ReadString(data, "error") ?? $"服务器任务进入终态：{status}";
                }
                else
                {
                    row.State = OfflineCommandStates.AwaitingJob;
                    row.LastError = ReadString(data, "error");
                }
                await store.UpdateAsync(row);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Poll failures do not resend a job or alter it to Pending.
                row.LastError = "状态轮询失败：" + ex.Message;
                await store.UpdateAsync(row);
            }
        }
    }

    private async Task SendOneAsync(OfflineCommandRecord row, CancellationToken cancellationToken)
    {
        switch (row.Kind)
        {
            case OfflineCommandKinds.WorkReport:
            {
                var request = Deserialize<WorkReportRequest>(row.PayloadJson);
                var accepted = await api.PostAsync<JobAcceptedResponse>("api/work-report", request,
                    row.CommandId, cancellationToken);
                row.ServerJobId = accepted.JobId;
                row.ServerStatus = accepted.Status;
                row.State = OfflineCommandStates.AwaitingJob;
                await store.UpdateAsync(row);
                break;
            }
            case OfflineCommandKinds.QualityJudge:
            {
                if (!row.TargetId.HasValue) throw new InvalidOperationException("离线判定缺少检验单 ID。失效命令不会自动重放。");
                var request = Deserialize<QualityJudgeRequest>(row.PayloadJson);
                var accepted = await api.PostAsync<JobAcceptedResponse>(
                    $"api/quality-inspection/{row.TargetId.Value}/judge", request, row.CommandId, cancellationToken);
                row.ServerJobId = accepted.JobId;
                row.ServerStatus = accepted.Status;
                row.State = OfflineCommandStates.AwaitingJob;
                await store.UpdateAsync(row);
                break;
            }
            case OfflineCommandKinds.MaterialIssue:
            {
                if (row.Stage == OfflineCommandStages.CreateMaterialDraft)
                {
                    if (!row.TargetId.HasValue) throw new InvalidOperationException("离线领料草稿缺少工单 ID。");
                    var request = Deserialize<MaterialIssueDraftRequest>(row.PayloadJson);
                    var draft = await api.PostAsync<MaterialDraftResponse>(
                        $"api/work-order/{row.TargetId.Value}/issue", request, row.CommandId, cancellationToken);
                    if (draft.BillId <= 0)
                        throw new InvalidOperationException("领料草稿响应缺少可核对的 billId。");
                    row.BillId = draft.BillId;
                    row.ServerStatus = draft.Status;
                    row.LastError = null;
                    if (draft.JobId is > 0)
                    {
                        row.ServerJobId = draft.JobId;
                        row.State = OfflineCommandStates.AwaitingJob;
                    }
                    else if (draft.Status.Equals("draft", StringComparison.OrdinalIgnoreCase))
                    {
                        row.Stage = OfflineCommandStages.SubmitMaterialDraft;
                        row.State = OfflineCommandStates.Pending;
                    }
                    else
                    {
                        row.State = OfflineCommandStates.Failed;
                        row.LastError = $"服务器返回领料单状态 {draft.Status}，且没有可轮询的 jobId；请先核对单据。";
                    }
                    await store.UpdateAsync(row);
                    break;
                }
                if (row.Stage != OfflineCommandStages.SubmitMaterialDraft || !row.BillId.HasValue)
                    throw new InvalidOperationException("领料任务缺少已保存的服务器草稿单 ID。");
                var accepted = await api.PostAsync<JobAcceptedResponse>(
                    $"api/material-issue/{row.BillId.Value}/submit", new { }, row.CommandId, cancellationToken);
                row.ServerJobId = accepted.JobId;
                row.ServerStatus = accepted.Status;
                row.State = OfflineCommandStates.AwaitingJob;
                await store.UpdateAsync(row);
                break;
            }
            case OfflineCommandKinds.MaintenanceExecute:
            {
                if (!row.TargetId.HasValue) throw new InvalidOperationException("点检任务缺少服务器 ID。");
                var request = Deserialize<MaintenanceExecuteRequest>(row.PayloadJson);
                await api.PostAsync<JsonElement>(
                    $"api/maintenance-task/{row.TargetId.Value}/execute", request, row.CommandId, cancellationToken);
                var readback = await api.GetAsync<MaintenanceTaskDetails>(
                    $"api/maintenance-task/{row.TargetId.Value}", cancellationToken);
                if (!MaintenanceExecutionMatches(readback, row, request))
                    throw new InvalidOperationException("执行请求已响应，但 GET-by-ID 的执行人/状态/负载未完全匹配；需要人工核对。");
                await store.DeleteAsync(row);
                break;
            }
            case OfflineCommandKinds.RepairOrderCreate:
            {
                var request = Deserialize<RepairOrderCreateRequest>(row.PayloadJson);
                var created = await api.PostAsync<RepairOrderDetails>("api/repair-order", request, row.CommandId, cancellationToken);
                if (created.Id <= 0 || string.IsNullOrWhiteSpace(created.Code))
                    throw new InvalidOperationException("报修请求已响应，但未返回可用于核对的维修单 ID/单号。");
                var readback = await api.GetAsync<RepairOrderDetails>(
                    $"api/repair-order/{created.Id}", cancellationToken);
                if (!RepairCreateMatches(readback, row, request, RepairOrderCode(row)) ||
                    !string.Equals(created.Code, RepairOrderCode(row), StringComparison.Ordinal))
                    throw new InvalidOperationException("报修请求已响应，但 GET-by-ID 的单号/设备/故障内容/提交人未完全匹配。");
                await store.DeleteAsync(row);
                break;
            }
            case OfflineCommandKinds.RepairOrderHandle:
            {
                if (!row.TargetId.HasValue) throw new InvalidOperationException("维修处理命令缺少服务器 ID。");
                var request = Deserialize<RepairOrderHandleRequest>(row.PayloadJson);
                await api.PostAsync<JsonElement>(
                    $"api/repair-order/{row.TargetId.Value}/handle", request, row.CommandId, cancellationToken);
                var readback = await api.GetAsync<RepairOrderDetails>(
                    $"api/repair-order/{row.TargetId.Value}", cancellationToken);
                if (!RepairHandleMatches(readback, row, request))
                    throw new InvalidOperationException("维修处理请求已响应，但 GET-by-ID 的处理人/状态/负载未完全匹配。");
                await store.DeleteAsync(row);
                break;
            }
            default:
                throw new InvalidOperationException($"未知的离线命令类型：{row.Kind}");
        }
    }

    private static bool CanReplaySafely(OfflineCommandRecord row)
        => row.Kind is OfflineCommandKinds.WorkReport or OfflineCommandKinds.QualityJudge ||
           (row.Kind == OfflineCommandKinds.MaterialIssue &&
            (row.Stage == OfflineCommandStages.CreateMaterialDraft ||
             row.Stage == OfflineCommandStages.SubmitMaterialDraft && row.BillId.HasValue)) ||
           row.Kind is OfflineCommandKinds.MaintenanceExecute or OfflineCommandKinds.RepairOrderCreate or OfflineCommandKinds.RepairOrderHandle;

    private static bool HasNonIdempotentOutcome(OfflineCommandRecord row) => !CanReplaySafely(row);

    private static string MaterialBillCode(OfflineCommandRecord row)
    {
        if (row.TenantId <= 0) throw new InvalidOperationException("领料命令缺少租户 ID，无法安全匹配单据。");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{row.TenantId}:{row.CommandId}")));
        return "LL" + hash[..20];
    }

    private static string RepairOrderCode(OfflineCommandRecord row)
    {
        if (row.TenantId <= 0) throw new InvalidOperationException("报修命令缺少租户 ID，无法安全匹配单据。");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{row.TenantId}:{row.CommandId}")));
        return "REP" + hash[..20];
    }

    private static bool RepairCreateMatches(RepairOrderDetails order, OfflineCommandRecord row,
        RepairOrderCreateRequest request, string? expectedCode)
        => order.Id > 0 && order.EquipmentId == row.TargetId && order.ReporterId == row.OwnerUserId &&
           string.Equals(order.Code, expectedCode, StringComparison.Ordinal) &&
           order.EquipmentId == request.EquipmentId && order.Level == request.Level &&
           string.Equals(order.FaultDesc, request.FaultDesc, StringComparison.Ordinal);

    private static bool MaintenanceExecutionMatches(MaintenanceTaskDetails task, OfflineCommandRecord row,
        MaintenanceExecuteRequest request)
    {
        var expectedStatus = request.Result?.Contains("异常", StringComparison.Ordinal) == true ? 2 : 1;
        return task.Id > 0 && task.Id == row.TargetId && row.OwnerUserId > 0 &&
               task.ExecutorId == row.OwnerUserId && task.TaskStatus == expectedStatus &&
               task.Cost == request.Cost && string.Equals(task.Result, request.Result, StringComparison.Ordinal) &&
               string.Equals(task.Remark, request.Remark, StringComparison.Ordinal);
    }

    private static bool RepairHandleMatches(RepairOrderDetails order, OfflineCommandRecord row,
        RepairOrderHandleRequest request)
        => order.Id > 0 && order.Id == row.TargetId && row.OwnerUserId > 0 &&
           order.HandlerId == row.OwnerUserId && order.RepairStatus == request.RepairStatus &&
           order.Cost == request.Cost && order.DowntimeMinutes == request.DowntimeMinutes &&
           string.Equals(order.Solution, request.Solution, StringComparison.Ordinal);

    private static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions)
           ?? throw new InvalidDataException("离线命令负载无法读取。");

    private static string? ReadString(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;
}
