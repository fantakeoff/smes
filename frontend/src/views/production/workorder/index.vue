<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { useLookupStore } from "@/store/modules/lookup";
import { workOrderStatus } from "@/utils/biz";

const lookup = useLookupStore();
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const statusFilter = ref<number>();
const workshopFilter = ref<number>();
const productFilter = ref<number>();
const detailVisible = ref(false);
const detail = ref<any>(null);
const actionWorkOrder = ref<any>(null);
const issueVisible = ref(false);
const receiptVisible = ref(false);
const reportVisible = ref(false);
const submitting = ref(false);
const warehouses = computed(() => lookup.options("warehouses"));
const workshops = computed(() => lookup.options("workshops"));
const products = computed(() => lookup.options("products"));
const processes = computed(() => lookup.options("processes"));
const workers = computed(() => lookup.options("users"));
const defects = computed(() => lookup.options("defects"));
const issueForm = reactive<any>({ warehouseId: undefined, remark: "" });
const receiptForm = reactive<any>({ warehouseId: undefined, qty: 1, remark: "" });
const reportForm = reactive<any>({ processId: undefined, workerId: undefined, qty: 1, qualifiedQty: 1, defectQty: 0, workHours: 0, defectItemId: undefined, remark: "" });

function dateText(value: any) { return value ? String(value).replace("T", " ").slice(0, 16) : "-"; }
function statusInfo(value: any) {
  const entry = workOrderStatus[Number(value)];
  return { label: entry?.label ?? "未知", type: (entry?.type ?? "info") as any };
}
function progress(row: any) {
  if (!Number(row.qty)) return 0;
  return Math.max(0, Math.min(100, Math.round((Number(row.completedQty || 0) / Number(row.qty)) * 100)));
}
async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/work-order", {
      params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined, workOrderStatus: statusFilter.value, workshopId: workshopFilter.value, productId: productFilter.value }
    });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "工单列表加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "工单列表加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function resetSearch() { keyword.value = ""; statusFilter.value = undefined; workshopFilter.value = undefined; productFilter.value = undefined; search(); }
async function openDetail(row: any) {
  const res: any = await http.request("get", `/work-order/${row.id}`);
  if (res?.code !== 0) return message(res?.message || "工单详情加载失败", { type: "error" });
  detail.value = res.data;
  detailVisible.value = true;
}
async function runAction(row: any, action: string, label: string) {
  try { await ElMessageBox.confirm(`确认${label}工单「${row.code}」？`, "操作确认", { type: "warning" }); }
  catch { return; }
  const res: any = await http.request("post", `/work-order/${row.id}/${action}`);
  message(res?.code === 0 ? (res?.message || `${label}成功`) : (res?.message || `${label}失败`), { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}
function openIssue(row: any) {
  actionWorkOrder.value = row;
  issueForm.warehouseId = lookup.list("warehouses")[0]?.id;
  issueForm.remark = "";
  issueVisible.value = true;
}
async function submitIssue() {
  if (!issueForm.warehouseId) return message("请选择领料仓库", { type: "warning" });
  submitting.value = true;
  try {
    const res: any = await http.request("post", `/work-order/${actionWorkOrder.value.id}/issue`, { data: { warehouseId: issueForm.warehouseId, remark: issueForm.remark || null } });
    if (res?.code === 0) { message(res?.message || "领料单已生成并过账", { type: "success" }); issueVisible.value = false; fetchList(); }
    else message(res?.message || "领料失败", { type: "error" });
  } catch (error: any) { message(error?.message || "领料失败", { type: "error" }); }
  finally { submitting.value = false; }
}
function openReceipt(row: any) {
  actionWorkOrder.value = row;
  receiptForm.warehouseId = lookup.list("warehouses")[0]?.id;
  receiptForm.qty = Math.max(0, Number(row.qty || 0) - Number(row.completedQty || 0)) || 1;
  receiptForm.remark = "";
  receiptVisible.value = true;
}
async function submitReceipt() {
  if (!receiptForm.warehouseId) return message("请选择入库仓库", { type: "warning" });
  if (Number(receiptForm.qty) <= 0) return message("入库数量必须大于 0", { type: "warning" });
  submitting.value = true;
  try {
    const res: any = await http.request("post", `/work-order/${actionWorkOrder.value.id}/receipt`, { data: { warehouseId: receiptForm.warehouseId, qty: Number(receiptForm.qty), remark: receiptForm.remark || null } });
    if (res?.code === 0) { message(res?.message || "入库单已生成并过账", { type: "success" }); receiptVisible.value = false; fetchList(); }
    else message(res?.message || "入库失败", { type: "error" });
  } catch (error: any) { message(error?.message || "入库失败", { type: "error" }); }
  finally { submitting.value = false; }
}
function openReport(row: any) {
  actionWorkOrder.value = row;
  Object.assign(reportForm, { processId: undefined, workerId: undefined, qty: 1, qualifiedQty: 1, defectQty: 0, workHours: 0, defectItemId: undefined, remark: "" });
  reportVisible.value = true;
}
async function submitReport() {
  if (Number(reportForm.qty) <= 0) return message("报工数量必须大于 0", { type: "warning" });
  if (Number(reportForm.qualifiedQty) + Number(reportForm.defectQty) > Number(reportForm.qty)) return message("合格数与不良数之和不能超过报工数量", { type: "warning" });
  submitting.value = true;
  try {
    const res: any = await http.request("post", "/work-report", { data: {
      workOrderId: actionWorkOrder.value.id, processId: reportForm.processId || null, workerId: reportForm.workerId || null,
      qty: Number(reportForm.qty), qualifiedQty: Number(reportForm.qualifiedQty), defectQty: Number(reportForm.defectQty),
      workHours: Number(reportForm.workHours), defectItemId: reportForm.defectItemId || null, remark: reportForm.remark || null
    } });
    if (res?.code === 0) { message(res?.message || "报工成功", { type: "success" }); reportVisible.value = false; fetchList(); }
    else message(res?.message || "报工失败", { type: "error" });
  } catch (error: any) { message(error?.message || "报工失败", { type: "error" }); }
  finally { submitting.value = false; }
}

onMounted(async () => { await lookup.load(); fetchList(); });
</script>

<template>
  <div class="workorder-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="工单编号 / 名称" style="width: 200px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="状态"><el-select v-model="statusFilter" clearable placeholder="全部" style="width: 140px"><el-option v-for="(item, key) in workOrderStatus" :key="key" :label="item.label" :value="Number(key)" /></el-select></el-form-item>
        <el-form-item label="车间"><el-select v-model="workshopFilter" clearable filterable placeholder="全部车间" style="width: 170px"><el-option v-for="item in workshops" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item>
        <el-form-item label="产品"><el-select v-model="productFilter" clearable filterable placeholder="全部产品" style="width: 190px"><el-option v-for="item in products" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="resetSearch">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">生产工单</span><el-button @click="fetchList">刷新</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="工单编号" width="170" fixed />
        <el-table-column prop="name" label="工单名称" min-width="150" show-overflow-tooltip />
        <el-table-column label="产品" min-width="150"><template #default="{ row }">{{ lookup.name('products', row.productId) }}</template></el-table-column>
        <el-table-column label="车间" min-width="130"><template #default="{ row }">{{ lookup.name('workshops', row.workshopId) || '-' }}</template></el-table-column>
        <el-table-column prop="qty" label="计划数" width="90" align="right" />
        <el-table-column prop="completedQty" label="完成数" width="90" align="right" />
        <el-table-column label="进度" width="150"><template #default="{ row }"><el-progress :percentage="progress(row)" :stroke-width="10" /></template></el-table-column>
        <el-table-column label="计划时间" min-width="280" show-overflow-tooltip><template #default="{ row }">{{ dateText(row.startDate) }} ~ {{ dateText(row.endDate) }}</template></el-table-column>
        <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag :type="statusInfo(row.workOrderStatus).type" size="small">{{ statusInfo(row.workOrderStatus).label }}</el-tag></template></el-table-column>
        <el-table-column label="操作" width="440" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openDetail(row)">详情</el-button>
            <el-button v-if="Number(row.workOrderStatus) === 0" link type="success" @click="runAction(row, 'release', '下达')">下达</el-button>
            <el-button v-if="Number(row.workOrderStatus) === 1" link type="primary" @click="runAction(row, 'start', '开工')">开工</el-button>
            <el-button v-if="[1, 2].includes(Number(row.workOrderStatus))" link type="warning" @click="openIssue(row)">领料</el-button>
            <el-button v-if="[1, 2].includes(Number(row.workOrderStatus))" link type="success" @click="openReceipt(row)">入库</el-button>
            <el-button v-if="[1, 2].includes(Number(row.workOrderStatus))" link type="primary" @click="openReport(row)">报工</el-button>
            <el-button v-if="Number(row.workOrderStatus) !== 4" link type="danger" @click="runAction(row, 'close', '关闭')">关闭</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>

    <el-drawer v-model="detailVisible" title="生产工单详情" size="65%">
      <template v-if="detail?.workOrder">
        <el-descriptions :column="3" border>
          <el-descriptions-item label="工单编号">{{ detail.workOrder.code }}</el-descriptions-item>
          <el-descriptions-item label="产品">{{ lookup.name('products', detail.workOrder.productId) }}</el-descriptions-item>
          <el-descriptions-item label="状态"><el-tag :type="statusInfo(detail.workOrder.workOrderStatus).type" size="small">{{ statusInfo(detail.workOrder.workOrderStatus).label }}</el-tag></el-descriptions-item>
          <el-descriptions-item label="车间">{{ lookup.name('workshops', detail.workOrder.workshopId) || '-' }}</el-descriptions-item>
          <el-descriptions-item label="计划数量">{{ detail.workOrder.qty }}</el-descriptions-item>
          <el-descriptions-item label="完成数量">{{ detail.workOrder.completedQty || 0 }}</el-descriptions-item>
          <el-descriptions-item label="计划周期">{{ dateText(detail.workOrder.startDate) }} ~ {{ dateText(detail.workOrder.endDate) }}</el-descriptions-item>
          <el-descriptions-item label="来源单号">{{ detail.workOrder.sourceCode || '-' }}</el-descriptions-item>
          <el-descriptions-item label="备注">{{ detail.workOrder.remark || '-' }}</el-descriptions-item>
        </el-descriptions>
        <el-divider content-position="left">用料清单</el-divider>
        <el-table :data="detail.materials || []" border stripe size="small">
          <el-table-column label="物料" min-width="200"><template #default="{ row }">{{ lookup.name('products', row.productId) }}</template></el-table-column>
          <el-table-column prop="requiredQty" label="需求数量" width="120" align="right" />
          <el-table-column prop="issuedQty" label="已领数量" width="120" align="right" />
        </el-table>
        <el-divider content-position="left">报工记录</el-divider>
        <el-table :data="detail.reports || []" border stripe size="small">
          <el-table-column prop="code" label="报工编号" width="160" />
          <el-table-column label="工序" min-width="130"><template #default="{ row }">{{ lookup.name('processes', row.processId) || '-' }}</template></el-table-column>
          <el-table-column label="员工" min-width="130"><template #default="{ row }">{{ lookup.name('users', row.workerId) || '-' }}</template></el-table-column>
          <el-table-column prop="qty" label="报工数" width="90" align="right" />
          <el-table-column prop="qualifiedQty" label="合格数" width="90" align="right" />
          <el-table-column prop="defectQty" label="不良数" width="90" align="right" />
          <el-table-column prop="workHours" label="工时" width="80" align="right" />
          <el-table-column label="报工时间" width="170"><template #default="{ row }">{{ row.reportTime ? String(row.reportTime).replace('T', ' ').slice(0, 16) : '-' }}</template></el-table-column>
        </el-table>
      </template>
    </el-drawer>

    <el-dialog v-model="issueVisible" title="工单领料" width="600px">
      <el-descriptions v-if="actionWorkOrder" :column="2" border class="dialog-info"><el-descriptions-item label="工单">{{ actionWorkOrder.code }}</el-descriptions-item><el-descriptions-item label="产品">{{ lookup.name('products', actionWorkOrder.productId) }}</el-descriptions-item></el-descriptions>
      <el-form label-width="100px"><el-form-item label="领料仓库" required><el-select v-model="issueForm.warehouseId" filterable placeholder="选择仓库" style="width: 100%"><el-option v-for="item in warehouses" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item><el-form-item label="备注"><el-input v-model="issueForm.remark" type="textarea" :rows="2" /></el-form-item></el-form>
      <template #footer><el-button @click="issueVisible = false">取消</el-button><el-button type="primary" :loading="submitting" @click="submitIssue">生成领料单</el-button></template>
    </el-dialog>
    <el-dialog v-model="receiptVisible" title="工单入库" width="600px">
      <el-descriptions v-if="actionWorkOrder" :column="2" border class="dialog-info"><el-descriptions-item label="工单">{{ actionWorkOrder.code }}</el-descriptions-item><el-descriptions-item label="产品">{{ lookup.name('products', actionWorkOrder.productId) }}</el-descriptions-item><el-descriptions-item label="剩余计划数">{{ Math.max(0, Number(actionWorkOrder.qty || 0) - Number(actionWorkOrder.completedQty || 0)) }}</el-descriptions-item></el-descriptions>
      <el-form label-width="100px"><el-form-item label="入库仓库" required><el-select v-model="receiptForm.warehouseId" filterable placeholder="选择仓库" style="width: 100%"><el-option v-for="item in warehouses" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item><el-form-item label="入库数量" required><el-input-number v-model="receiptForm.qty" :min="0.0001" :precision="4" controls-position="right" style="width: 100%" /></el-form-item><el-form-item label="备注"><el-input v-model="receiptForm.remark" type="textarea" :rows="2" /></el-form-item></el-form>
      <template #footer><el-button @click="receiptVisible = false">取消</el-button><el-button type="primary" :loading="submitting" @click="submitReceipt">生成入库单</el-button></template>
    </el-dialog>
    <el-dialog v-model="reportVisible" title="工单报工" width="760px">
      <el-descriptions v-if="actionWorkOrder" :column="2" border class="dialog-info"><el-descriptions-item label="工单">{{ actionWorkOrder.code }}</el-descriptions-item><el-descriptions-item label="产品">{{ lookup.name('products', actionWorkOrder.productId) }}</el-descriptions-item></el-descriptions>
      <el-form label-width="95px"><el-row :gutter="14">
        <el-col :span="12"><el-form-item label="工序"><el-select v-model="reportForm.processId" clearable filterable style="width: 100%"><el-option v-for="item in processes" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="报工员工"><el-select v-model="reportForm.workerId" clearable filterable placeholder="默认当前用户" style="width: 100%"><el-option v-for="item in workers" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="报工数量" required><el-input-number v-model="reportForm.qty" :min="0.0001" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="工时" required><el-input-number v-model="reportForm.workHours" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="合格数量"><el-input-number v-model="reportForm.qualifiedQty" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="不良数量"><el-input-number v-model="reportForm.defectQty" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="不良品项"><el-select v-model="reportForm.defectItemId" clearable filterable style="width: 100%"><el-option v-for="item in defects" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item></el-col>
        <el-col :span="24"><el-form-item label="备注"><el-input v-model="reportForm.remark" type="textarea" :rows="2" /></el-form-item></el-col>
      </el-row></el-form>
      <template #footer><el-button @click="reportVisible = false">取消</el-button><el-button type="primary" :loading="submitting" @click="submitReport">提交报工</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.workorder-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .dialog-info { margin-bottom: 18px; }
}
</style>
