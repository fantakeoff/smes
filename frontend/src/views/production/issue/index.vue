<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from "vue";
import { ElMessageBox } from "element-plus";
import QRCode from "qrcode";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { useLookupStore } from "@/store/modules/lookup";
import { billStatusDict, billTypeDict } from "@/utils/biz";

const lookup = useLookupStore();
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const statusFilter = ref<number>();
const warehouseFilter = ref<number>();
const detailVisible = ref(false);
const detail = ref<any>(null);
const createVisible = ref(false);
const workOrders = ref<any[]>([]);
const selectedOrderId = ref<number>();
const selectedOrderDetail = ref<any>(null);
const warehouses = computed(() => lookup.options("warehouses"));
const form = reactive<any>({ warehouseId: undefined, remark: "", items: [] as any[] });
const stocks = ref<any[]>([]);
const stockLoading = ref(false);
const submitting = ref(false);
const issueJob = ref<any>(null);
const draftQr = ref("");
const detailQr = ref("");
let draftOperationKey: string | undefined;
watch(() => issueJob.value?.code, async code => {
  draftQr.value = code ? await QRCode.toDataURL(String(code), { errorCorrectionLevel: "M", width: 180, margin: 2 }) : "";
});
watch(() => detail.value?.code, async code => {
  detailQr.value = code ? await QRCode.toDataURL(String(code), { errorCorrectionLevel: "M", width: 180, margin: 2 }) : "";
});
let pollTimer: ReturnType<typeof setTimeout> | undefined;
function newIdempotencyKey() { return crypto.randomUUID?.() || `${Date.now()}-${Math.random().toString(36).slice(2)}`; }

function dateText(value: any) { return value ? String(value).slice(0, 10) : "-"; }
function billStatus(value: any) {
  const item = billStatusDict[Number(value)];
  return { label: item?.label ?? "未知", type: (item?.type ?? "info") as any };
}
function warehouseName(id: number) { return lookup.name("warehouses", id) || "-"; }
function orderLabel(order: any) { return `${order.code || ''} · ${order.name || lookup.name('products', order.productId) || '生产工单'}`; }

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/stock-bill", { params: {
      page: page.value, pageSize: pageSize.value, billType: 2,
      keyword: keyword.value || undefined, billStatus: statusFilter.value, warehouseId: warehouseFilter.value
    } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "领料单列表加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "领料单列表加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function resetSearch() { keyword.value = ""; statusFilter.value = undefined; warehouseFilter.value = undefined; search(); }
async function openDetail(row: any) {
  const res: any = await http.request("get", `/stock-bill/${row.id}`);
  if (res?.code !== 0) return message(res?.message || "领料单详情加载失败", { type: "error" });
  detail.value = res.data;
  detailVisible.value = true;
}
async function submitBill(row: any) {
  submitting.value = true;
  try {
    const submitted: any = await http.request("post", `/material-issue/${row.id ?? row.billId}/submit`, {
      headers: { "Idempotency-Key": newIdempotencyKey() }, data: {}
    });
    if (submitted?.code !== 0) throw new Error(submitted?.message || "领料任务提交失败，请先处理缺料");
    const result: any = submitted.data;
    const jobId = submitted.data?.jobId;
    issueJob.value = { ...issueJob.value, ...result, code: row.code, status: result?.status || "queued" };
    if (!jobId) throw new Error("任务未返回 jobId");
    if (result?.status === "failed") {
      message("领料任务已进入死信；请联系管理员检查缺料原因并人工重放，不会自动重试", { type: "error" });
      submitting.value = false;
      return;
    }
    message(`领料任务已提交：${row.code}`, { type: "info" });
    await pollIssueJob(String(jobId));
  } catch (error: any) {
    message(error?.message || "任务提交失败，草稿仍可稍后重试", { type: "error" });
    submitting.value = false;
  }
}
async function voidDraft(row: any) {
  try { await ElMessageBox.confirm(`确认作废未过账领料单「${row.code}」？`, "作废草稿", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("post", `/material-issue/${row.id}/void`, { data: {} });
    message(res?.message || "草稿已作废", { type: res?.code === 0 ? "success" : "error" });
    if (res?.code === 0) await fetchList();
  } catch (error: any) { message(error?.message || "作废失败", { type: "error" }); }
}
async function loadWorkOrders() {
  const res: any = await http.request("get", "/work-order", { params: { page: 1, pageSize: 500 } });
  if (res?.code !== 0) return message(res?.message || "工单列表加载失败", { type: "error" });
  workOrders.value = (res.data?.list ?? []).filter((row: any) => [1, 2].includes(Number(row.workOrderStatus)));
}
function stockFor(item: any) {
  return stocks.value.filter(stock => Number(stock.productId) === Number(item.productId));
}
function availableFor(item: any) {
  const matches = stockFor(item).filter(stock => (stock.batchNo || "") === (item.batchNo || "") && (stock.location || "") === (item.location || ""));
  return matches.reduce((sum, stock) => sum + Number(stock.availableQty ?? stock.qty ?? 0), 0);
}
function stockOptions(item: any) { return stockFor(item).filter(stock => Number(stock.availableQty ?? stock.qty ?? 0) > 0); }
function lineLabel(item: any) { return lookup.name("products", Number(item.productId)) || item.materialName || item.name || `物料${item.productId ?? ""}`; }
async function loadStocks() {
  stocks.value = [];
  if (!form.warehouseId) return;
  stockLoading.value = true;
  try {
    const res: any = await http.request("get", "/inventory/stock", { params: { page: 1, pageSize: 1000, warehouseId: form.warehouseId } });
    if (res?.code === 0) {
      stocks.value = res.data?.list ?? [];
      for (const item of form.items) {
        const options = stockOptions(item);
        if (!item.batchNo && !item.location && options.length === 1) selectStock(item, options[0]);
      }
    }
    else message(res?.message || "库存可用量加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "库存可用量加载失败", { type: "error" }); }
  finally { stockLoading.value = false; }
}
async function openCreate() {
  await loadWorkOrders();
  selectedOrderId.value = undefined;
  selectedOrderDetail.value = null;
  form.warehouseId = lookup.list("warehouses")[0]?.id;
  form.items = [];
  form.remark = "";
  issueJob.value = null;
  draftOperationKey = undefined;
  submitting.value = false;
  stocks.value = [];
  createVisible.value = true;
  if (form.warehouseId) loadStocks();
}
async function onWarehouseChange() { await loadStocks(); }
async function onOrderChange(id: number) {
  selectedOrderDetail.value = null;
  form.items = [];
  if (!id) return;
  const res: any = await http.request("get", `/work-order/${id}`);
  if (res?.code === 0) {
    selectedOrderDetail.value = res.data;
    form.items = (res.data?.materials ?? []).map((material: any) => ({
      materialId: material.id,
      productId: material.productId,
      selected: Number(material.requiredQty ?? material.qty ?? 0) > Number(material.issuedQty ?? 0),
      qty: Math.max(0, Number(material.requiredQty ?? material.qty ?? 0) - Number(material.issuedQty ?? 0)),
      requiredQty: Number(material.requiredQty ?? material.qty ?? 0),
      issuedQty: Number(material.issuedQty ?? 0), batchNo: "", location: ""
    }));
    await loadStocks();
  } else message(res?.message || "工单物料加载失败", { type: "error" });
}
function selectStock(item: any, stock: any) { item.batchNo = stock.batchNo || ""; item.location = stock.location || ""; }
async function pollIssueJob(jobId: string) {
  try {
    const res: any = await http.request("get", `/material-issue/jobs/${jobId}`);
    if (res?.code !== 0) throw new Error(res?.message || "领料任务状态查询失败");
    issueJob.value = { ...issueJob.value, ...res.data };
    const status = String(res.data?.status || "").toLowerCase();
    if (status === "completed") {
      message("领料过账完成", { type: "success" });
      submitting.value = false;
      fetchList();
      return;
    }
    if (status === "failed") {
      message(res.data?.error || "领料过账失败，请处理后重试", { type: "error" });
      submitting.value = false;
      return;
    }
    pollTimer = setTimeout(() => pollIssueJob(jobId), 1500);
  } catch (error: any) {
    if (issueJob.value) issueJob.value.status = "unknown";
    message(error?.message || "领料任务状态查询失败", { type: "error" });
    submitting.value = false;
  }
}
async function createFromWorkOrder() {
  if (!selectedOrderId.value) return message("请选择生产工单", { type: "warning" });
  if (!form.warehouseId) return message("请选择领料仓库", { type: "warning" });
  const items = form.items.filter((item: any) => item.selected && Number(item.qty) > 0);
  if (!items.length) return message("请勾选并填写本次领料数量", { type: "warning" });
  if (items.some((item: any) => Number(item.qty) > Number(item.requiredQty) - Number(item.issuedQty))) return message("本次领料不得超过工单剩余需求", { type: "warning" });
  submitting.value = true;
  issueJob.value = null;
  try {
    const operationKey = draftOperationKey ?? (draftOperationKey = newIdempotencyKey());
    const draft: any = await http.request("post", `/work-order/${selectedOrderId.value}/issue`, { headers: { "Idempotency-Key": operationKey }, data: {
      warehouseId: form.warehouseId, remark: form.remark || null,
      items: items.map((item: any) => ({ materialId: item.materialId, qty: Number(item.qty), batchNo: item.batchNo || null, location: item.location || null }))
    } });
    if (draft?.code !== 0) throw new Error(draft?.message || "领料草稿创建失败");
    issueJob.value = { ...draft.data, status: "draft" };
    const billId = draft.data?.billId;
    if (!billId) throw new Error("领料草稿未返回 billId");
    draftOperationKey = undefined;
    await fetchList();
    if (draft.data?.shortageHints?.length) {
      message(`草稿 ${draft.data.code} 已保存，所选批次/库位缺料；补货后从列表提交`, { type: "warning" });
      submitting.value = false;
      return;
    }
    await submitBill({ id: billId, code: draft.data.code });
  } catch (error: any) {
    if (issueJob.value?.status !== "queued") issueJob.value = { ...issueJob.value, status: "draft" };
    message(error?.message || "领料任务提交失败", { type: "error" });
    submitting.value = false;
  }
}

onMounted(async () => { await lookup.load(); fetchList(); });
onBeforeUnmount(() => { if (pollTimer) clearTimeout(pollTimer); });
</script>

<template>
  <div class="production-issue-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="单据编号 / 来源工单" style="width: 210px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="仓库"><el-select v-model="warehouseFilter" clearable filterable placeholder="全部仓库" style="width: 170px"><el-option v-for="item in warehouses" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item>
        <el-form-item label="状态"><el-select v-model="statusFilter" clearable placeholder="全部状态" style="width: 145px"><el-option v-for="(item, key) in billStatusDict" :key="key" :label="item.label" :value="Number(key)" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="resetSearch">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">生产领料单</span><el-button type="primary" @click="openCreate">从工单生成领料单</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="单据编号" width="170" fixed />
        <el-table-column label="类型" width="110"><template #default="{ row }"><el-tag :type="billTypeDict[row.billType]?.type" size="small">{{ billTypeDict[row.billType]?.label || '生产领料' }}</el-tag></template></el-table-column>
        <el-table-column label="单据日期" width="120"><template #default="{ row }">{{ dateText(row.billDate) }}</template></el-table-column>
        <el-table-column label="仓库" min-width="140"><template #default="{ row }">{{ warehouseName(row.warehouseId) }}</template></el-table-column>
        <el-table-column prop="sourceCode" label="来源工单" min-width="150" />
        <el-table-column prop="totalQty" label="领料数量" width="110" align="right" />
        <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag :type="billStatus(row.billStatus).type" size="small">{{ billStatus(row.billStatus).label }}</el-tag></template></el-table-column>
        <el-table-column label="操作" width="220" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="openDetail(row)">明细</el-button><el-button v-if="Number(row.billStatus) === 0" link type="success" :disabled="submitting" @click="submitBill(row)">提交领料</el-button><el-button v-if="Number(row.billStatus) === 0" link type="danger" :disabled="submitting" @click="voidDraft(row)">作废</el-button></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>

    <el-dialog v-model="createVisible" title="从生产工单生成领料单" width="980px" destroy-on-close>
      <el-alert title="先保存领料草稿，再提交异步过账任务；仅任务成功后才算领料完成。" type="info" :closable="false" show-icon class="hint" />
      <el-form label-width="105px">
        <el-form-item label="生产工单" required><el-select v-model="selectedOrderId" filterable placeholder="选择已下达或生产中的工单" style="width: 100%" @change="onOrderChange"><el-option v-for="row in workOrders" :key="row.id" :label="orderLabel(row)" :value="row.id" /></el-select></el-form-item>
        <el-form-item label="领料仓库" required><el-select v-model="form.warehouseId" filterable placeholder="选择仓库" style="width: 100%" @change="onWarehouseChange"><el-option v-for="item in warehouses" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item>
        <el-form-item label="备注"><el-input v-model="form.remark" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <template v-if="selectedOrderDetail">
        <el-divider content-position="left">工单 BOM / 库存可用量</el-divider>
        <el-table v-loading="stockLoading" :data="form.items" border stripe size="small">
          <el-table-column label="选择" width="58" align="center"><template #default="{ row }"><el-checkbox v-model="row.selected" /></template></el-table-column>
          <el-table-column label="物料" min-width="165"><template #default="{ row }">{{ lineLabel(row) }}</template></el-table-column>
          <el-table-column prop="requiredQty" label="需求" width="82" align="right" />
          <el-table-column prop="issuedQty" label="已领" width="82" align="right" />
          <el-table-column label="本次领料" width="125"><template #default="{ row }"><el-input-number v-model="row.qty" :min="0" :max="Math.max(0, row.requiredQty - row.issuedQty)" :precision="3" :step="1" controls-position="right" /></template></el-table-column>
          <el-table-column label="批次 / 库位" min-width="195"><template #default="{ row }"><el-select v-if="stockOptions(row).length" :model-value="`${row.batchNo}|${row.location}`" clearable placeholder="选择批次/库位" style="width: 100%" @change="(value: string) => { const stock = stockOptions(row).find((x: any) => `${x.batchNo || ''}|${x.location || ''}` === value); if (stock) selectStock(row, stock); else { row.batchNo = ''; row.location = ''; } }"><el-option v-for="stock in stockOptions(row)" :key="`${stock.batchNo}-${stock.location}-${stock.id}`" :label="`${stock.batchNo || '无批次'} / ${stock.location || '无库位'}`" :value="`${stock.batchNo || ''}|${stock.location || ''}`" /></el-select><el-input v-model="row.batchNo" placeholder="批次（可选）" size="small"/><el-input v-model="row.location" placeholder="库位（可选）" size="small"/></template></el-table-column>
          <el-table-column label="可用量" width="95" align="right"><template #default="{ row }"><span :class="{ shortage: Number(row.qty) > availableFor(row) }">{{ availableFor(row) }}</span></template></el-table-column>
          <el-table-column label="缺料去向" width="180"><template #default="{ row }"><template v-if="Math.max(0, Number(row.requiredQty) - Number(row.issuedQty)) > availableFor(row)"><el-link href="/purchase/requisition" type="primary" target="_blank">采购</el-link><el-link href="/warehouse/transfer" type="primary" target="_blank">调拨</el-link><el-link href="/production/subcontract" type="primary" target="_blank">委外</el-link></template><span v-else>-</span></template></el-table-column>
        </el-table>
      </template>
      <el-alert v-if="issueJob" :title="`领料单编码（可供扫码）：${issueJob.code || '草稿已创建'}`" :description="`过账任务状态：${issueJob.status || 'queued'}；${issueJob.status === 'completed' ? '领料已完成' : issueJob.status === 'failed' ? '任务失败，领料未完成' : issueJob.status === 'draft' ? '尚未提交，需补齐指定批次/库位库存' : '正在等待后台处理'}`" :type="issueJob.status === 'completed' ? 'success' : issueJob.status === 'failed' ? 'error' : 'warning'" :closable="false" show-icon class="job-state" />
      <div v-if="draftQr" class="qr-block"><img :src="draftQr" :alt="`领料单 ${issueJob.code} 二维码`" /><span>{{ issueJob.code }}</span></div>
      <template #footer><el-button :disabled="submitting" @click="createVisible = false">关闭</el-button><el-button type="primary" :disabled="!!issueJob?.billId" :loading="submitting" @click="createFromWorkOrder">{{ submitting ? '领料任务处理中' : '生成草稿并提交' }}</el-button></template>
    </el-dialog>

    <el-drawer v-model="detailVisible" title="生产领料单详情" size="60%">
      <template v-if="detail">
        <el-descriptions :column="3" border>
          <el-descriptions-item label="单据编号">{{ detail.code }}</el-descriptions-item>
          <el-descriptions-item label="单据类型">{{ billTypeDict[detail.billType]?.label || '生产领料' }}</el-descriptions-item>
          <el-descriptions-item label="状态"><el-tag :type="billStatus(detail.billStatus).type" size="small">{{ billStatus(detail.billStatus).label }}</el-tag></el-descriptions-item>
          <el-descriptions-item label="单据日期">{{ dateText(detail.billDate) }}</el-descriptions-item>
          <el-descriptions-item label="仓库">{{ warehouseName(detail.warehouseId) }}</el-descriptions-item>
          <el-descriptions-item label="来源工单">{{ detail.sourceCode || '-' }}</el-descriptions-item>
          <el-descriptions-item label="备注" :span="3">{{ detail.remark || '-' }}</el-descriptions-item>
        </el-descriptions>
        <div v-if="detailQr" class="qr-block"><img :src="detailQr" :alt="`领料单 ${detail.code} 二维码`" /><span>{{ detail.code }}</span></div>
        <el-table :data="detail.lines || []" border stripe style="margin-top: 16px">
          <el-table-column prop="seq" label="序号" width="75" />
          <el-table-column label="物料" min-width="220"><template #default="{ row }">{{ lookup.name('products', row.productId) }}</template></el-table-column>
          <el-table-column prop="qty" label="领料数量" width="120" align="right" />
          <el-table-column prop="batchNo" label="批次" min-width="120" />
          <el-table-column prop="location" label="库位" min-width="120" />
        </el-table>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped lang="scss">
.production-issue-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .hint { margin-bottom: 18px; }
  .job-state { margin-top: 16px; }
  .qr-block { display: inline-flex; flex-direction: column; align-items: center; margin: 12px 0; padding: 8px; border: 1px solid var(--el-border-color); }
  .qr-block img { width: 180px; height: 180px; }
  .qr-block span { font-family: monospace; font-size: 12px; }
  .shortage { color: var(--el-color-danger); font-weight: 600; }
  :deep(.el-link + .el-link) { margin-left: 6px; }
}
</style>
