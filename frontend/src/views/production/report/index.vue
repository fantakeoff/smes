<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { useLookupStore } from "@/store/modules/lookup";

const lookup = useLookupStore();
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const workOrderFilter = ref<number>();
const processFilter = ref<number>();
const workerFilter = ref<number>();
const workOrders = ref<any[]>([]);
const createVisible = ref(false);
const submitting = ref(false);
const activeJob = ref<any>(null);
let pollingJobId = 0;
const form = reactive<any>({ workOrderId: undefined, processId: undefined, workerId: undefined, qty: 1, qualifiedQty: 1, defectQty: 0, workHours: 0, defectItemId: undefined, remark: "" });
const workOrderOptions = computed(() => workOrders.value.map(row => ({ label: `${row.code || ''} · ${row.name || lookup.name('products', row.productId) || '生产工单'}`, value: row.id })));
const processes = computed(() => lookup.options("processes"));
const workers = computed(() => lookup.options("users"));
const defects = computed(() => lookup.options("defects"));
const orderById = computed(() => new Map(workOrders.value.map(row => [Number(row.id), row])));

function orderName(id: number) {
  const order: any = orderById.value.get(Number(id));
  return order ? `${order.code}${order.name ? ` · ${order.name}` : ''}` : `工单 #${id}`;
}
function userName(id: number) { return lookup.name("users", id) || "-"; }
function processName(id: number) { return lookup.name("processes", id) || "-"; }
function dateTime(value: any) { return value ? String(value).replace("T", " ").slice(0, 16) : "-"; }

async function loadWorkOrders() {
  const res: any = await http.request("get", "/work-order", { params: { page: 1, pageSize: 500 } });
  if (res?.code === 0) workOrders.value = res.data?.list ?? [];
  else message(res?.message || "工单选项加载失败", { type: "error" });
}
async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/work-report", { params: {
      "page.Page": page.value, "page.PageSize": pageSize.value,
      "page.Keyword": keyword.value || undefined,
      workOrderId: workOrderFilter.value, processId: processFilter.value,
      workerId: workerFilter.value
    } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "报工记录加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "报工记录加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function resetSearch() { keyword.value = ""; workOrderFilter.value = undefined; processFilter.value = undefined; workerFilter.value = undefined; search(); }
function openCreate() {
  Object.assign(form, { workOrderId: undefined, processId: undefined, workerId: undefined, qty: 1, qualifiedQty: 1, defectQty: 0, workHours: 0, defectItemId: undefined, remark: "" });
  createVisible.value = true;
}
async function pollJob(jobId: number) {
  pollingJobId = jobId;
  for (let attempt = 0; attempt < 90 && pollingJobId === jobId; attempt++) {
    try {
      const res: any = await http.request("get", `/work-report/jobs/${jobId}`);
      if (res?.code === 0) {
        activeJob.value = res.data;
        if (res.data?.status === "completed") {
          message("报工处理完成", { type: "success" });
          await fetchList();
          pollingJobId = 0;
          return;
        }
        if (res.data?.status === "failed") {
          message(res.data?.error || "报工处理失败", { type: "error" });
          pollingJobId = 0;
          return;
        }
      }
    } catch { /* Keep polling transient network failures. */ }
    await new Promise(resolve => window.setTimeout(resolve, 1000));
  }
  if (pollingJobId === jobId) pollingJobId = 0;
}
async function saveReport() {
  if (!form.workOrderId) return message("请选择生产工单", { type: "warning" });
  if (Number(form.qty) <= 0) return message("报工数量必须大于 0", { type: "warning" });
  if (Number(form.qualifiedQty) + Number(form.defectQty) > Number(form.qty)) return message("合格数与不良数之和不能超过报工数量", { type: "warning" });
  submitting.value = true;
  try {
    const res: any = await http.request("post", "/work-report", { headers: { "Idempotency-Key": crypto.randomUUID() }, data: {
      workOrderId: form.workOrderId, processId: form.processId || null, workerId: form.workerId || null,
      qty: Number(form.qty), qualifiedQty: Number(form.qualifiedQty), defectQty: Number(form.defectQty),
      workHours: Number(form.workHours), defectItemId: form.defectItemId || null, remark: form.remark || null
    } });
    if (res?.code === 0) {
      activeJob.value = res.data;
      message("已进入队列，正在处理", { type: "success" });
      createVisible.value = false;
      void pollJob(Number(res.data.jobId));
    } else message(res?.message || "报工失败", { type: "error" });
  } catch (error: any) { message(error?.message || "报工失败", { type: "error" }); }
  finally { submitting.value = false; }
}
async function remove(row: any) {
  try { await ElMessageBox.confirm(`删除报工记录「${row.code || row.id}」将回退工单完成数量，是否继续？`, "确认删除", { type: "warning" }); }
  catch { return; }
  const res: any = await http.request("delete", `/work-report/${row.id}`);
  message(res?.code === 0 ? "报工记录已删除" : res?.message || "删除失败", { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}

onMounted(async () => { await lookup.load(); await loadWorkOrders(); fetchList(); });
</script>

<template>
  <div class="work-report-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="报工编号 / 名称" style="width: 200px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="生产工单"><el-select v-model="workOrderFilter" clearable filterable placeholder="全部工单" style="width: 230px"><el-option v-for="item in workOrderOptions" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item>
        <el-form-item label="工序"><el-select v-model="processFilter" clearable filterable placeholder="全部工序" style="width: 170px"><el-option v-for="item in processes" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item>
        <el-form-item label="报工员工"><el-select v-model="workerFilter" clearable filterable placeholder="全部员工" style="width: 170px"><el-option v-for="item in workers" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="resetSearch">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">生产报工记录</span><el-button type="primary" @click="openCreate">新建报工</el-button></div>
      <el-alert v-if="activeJob" :closable="true" type="info" show-icon @close="activeJob = null">
        <template #title>报工任务 #{{ activeJob.jobId }}：{{ ({ queued: '排队中', processing: '处理中', completed: '已完成', failed: '失败' } as any)[activeJob.status] || activeJob.status }}</template>
        <template v-if="activeJob.error" #default>{{ activeJob.error }}</template>
      </el-alert>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="报工编号" width="165" fixed />
        <el-table-column label="工单" min-width="220" show-overflow-tooltip><template #default="{ row }">{{ orderName(row.workOrderId) }}</template></el-table-column>
        <el-table-column label="工序" min-width="130"><template #default="{ row }">{{ processName(row.processId) }}</template></el-table-column>
        <el-table-column label="员工" min-width="130"><template #default="{ row }">{{ userName(row.workerId) }}</template></el-table-column>
        <el-table-column prop="qty" label="报工数量" width="105" align="right" />
        <el-table-column prop="qualifiedQty" label="合格数量" width="105" align="right" />
        <el-table-column prop="defectQty" label="不良数量" width="105" align="right" />
        <el-table-column prop="workHours" label="工时" width="85" align="right" />
        <el-table-column label="报工时间" width="165"><template #default="{ row }">{{ dateTime(row.reportTime) }}</template></el-table-column>
        <el-table-column label="操作" width="85" fixed="right"><template #default="{ row }"><el-button link type="danger" @click="remove(row)">删除</el-button></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 40, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>

    <el-dialog v-model="createVisible" title="新建生产报工" width="760px" destroy-on-close>
      <el-form label-width="100px"><el-row :gutter="14">
        <el-col :span="24"><el-form-item label="生产工单" required><el-select v-model="form.workOrderId" filterable placeholder="选择已下达或生产中的工单" style="width: 100%"><el-option v-for="item in workOrderOptions.filter(option => { const row = workOrders.find(x => x.id === option.value); return row && [1, 2].includes(Number(row.workOrderStatus)); })" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="工序"><el-select v-model="form.processId" clearable filterable style="width: 100%"><el-option v-for="item in processes" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="报工员工"><el-select v-model="form.workerId" clearable filterable placeholder="默认当前用户" style="width: 100%"><el-option v-for="item in workers" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="报工数量" required><el-input-number v-model="form.qty" :min="0.0001" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="工时"><el-input-number v-model="form.workHours" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="合格数量"><el-input-number v-model="form.qualifiedQty" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="不良数量"><el-input-number v-model="form.defectQty" :min="0" :precision="4" controls-position="right" style="width: 100%" /></el-form-item></el-col>
        <el-col :span="12"><el-form-item label="不良品项"><el-select v-model="form.defectItemId" clearable filterable style="width: 100%"><el-option v-for="item in defects" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item></el-col>
        <el-col :span="24"><el-form-item label="备注"><el-input v-model="form.remark" type="textarea" :rows="2" /></el-form-item></el-col>
      </el-row></el-form>
      <template #footer><el-button @click="createVisible = false">取消</el-button><el-button type="primary" :loading="submitting" @click="saveReport">提交报工</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.work-report-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
}
</style>
