<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { levelDict, repairStatusDict, toOptions } from "@/utils/biz";

const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const repairStatus = ref<number>();
const equipmentId = ref<number>();
const equipmentOptions = ref<Array<{ label: string; value: number }>>([]);
const reportVisible = ref(false);
const handleVisible = ref(false);
const saving = ref(false);
const activeOrder = ref<any>(null);
const reportForm = reactive<any>({ equipmentId: undefined, faultDesc: "", level: 1 });
const handleForm = reactive<any>({ solution: "", repairStatus: 1, cost: 0, downtimeMinutes: 0 });
const statusOptions = toOptions(repairStatusDict);
const levelOptions = toOptions(levelDict);
const equipmentName = (id: number) => equipmentOptions.value.find(x => x.value === id)?.label || `设备#${id}`;
const statusInfo = (status: number) => repairStatusDict[status] ?? { label: String(status ?? "-"), type: "info" };
const levelInfo = (level: number) => levelDict[level] ?? { label: String(level ?? "-"), type: "info" };

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/repair-order", { params: {
      page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined,
      repairStatus: repairStatus.value, equipmentId: equipmentId.value
    } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "报修列表加载失败", { type: "error" });
  } catch (e: any) { message(e?.message || "报修列表加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; repairStatus.value = undefined; equipmentId.value = undefined; search(); }
function openReport() { Object.assign(reportForm, { equipmentId: undefined, faultDesc: "", level: 1 }); reportVisible.value = true; }
async function submitReport() {
  if (!reportForm.equipmentId || !reportForm.faultDesc.trim()) return message("请选择设备并填写故障描述", { type: "warning" });
  saving.value = true;
  try {
    const res: any = await http.request("post", "/repair-order", { data: {
      equipmentId: reportForm.equipmentId, faultDesc: reportForm.faultDesc.trim(), level: reportForm.level
    } });
    if (res?.code === 0) { message("报修已提交", { type: "success" }); reportVisible.value = false; await fetchList(); }
    else message(res?.message || "报修失败", { type: "error" });
  } catch (e: any) { message(e?.response?.data?.message || e?.message || "报修失败", { type: "error" }); }
  finally { saving.value = false; }
}
function openHandle(row: any) {
  activeOrder.value = row;
  Object.assign(handleForm, { solution: row.solution || "", repairStatus: row.repairStatus === 0 ? 1 : row.repairStatus, cost: Number(row.cost) || 0, downtimeMinutes: Number(row.downtimeMinutes) || 0 });
  handleVisible.value = true;
}
async function submitHandle() {
  if (!handleForm.solution.trim()) return message("请填写维修方案或处理说明", { type: "warning" });
  saving.value = true;
  try {
    const res: any = await http.request("post", `/repair-order/${activeOrder.value.id}/handle`, { data: {
      solution: handleForm.solution.trim(), repairStatus: handleForm.repairStatus,
      cost: Number(handleForm.cost) || 0, downtimeMinutes: Number(handleForm.downtimeMinutes) || 0
    } });
    if (res?.code === 0) { message("维修处理已保存", { type: "success" }); handleVisible.value = false; await fetchList(); }
    else message(res?.message || "处理失败", { type: "error" });
  } catch (e: any) { message(e?.response?.data?.message || e?.message || "处理失败", { type: "error" }); }
  finally { saving.value = false; }
}
onMounted(async () => {
  const res: any = await http.request("get", "/equipment/options");
  if (res?.code === 0) equipmentOptions.value = (res.data ?? []).map((x: any) => ({ label: `${x.code ? `${x.code} ` : ""}${x.name}`, value: x.id }));
  fetchList();
});
</script>

<template>
  <div class="repair-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="报修单号 / 故障描述" style="width: 200px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="设备"><el-select v-model="equipmentId" clearable filterable placeholder="全部设备" style="width: 200px"><el-option v-for="x in equipmentOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="维修状态"><el-select v-model="repairStatus" clearable placeholder="全部" style="width: 135px"><el-option v-for="x in statusOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">设备报修</span><el-button type="primary" @click="openReport">新建报修</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe row-key="id">
        <el-table-column prop="code" label="报修单号" width="165" />
        <el-table-column label="设备" min-width="180" show-overflow-tooltip><template #default="{ row }">{{ equipmentName(row.equipmentId) }}</template></el-table-column>
        <el-table-column prop="faultDesc" label="故障描述" min-width="220" show-overflow-tooltip />
        <el-table-column label="级别" width="90"><template #default="{ row }"><el-tag size="small" :type="(levelInfo(row.level).type as any)">{{ levelInfo(row.level).label }}</el-tag></template></el-table-column>
        <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag size="small" :type="(statusInfo(row.repairStatus).type as any)">{{ statusInfo(row.repairStatus).label }}</el-tag></template></el-table-column>
        <el-table-column prop="reportTime" label="报修时间" width="160"><template #default="{ row }">{{ String(row.reportTime || "").replace("T", " ").slice(0, 16) || "-" }}</template></el-table-column>
        <el-table-column prop="cost" label="维修费用" width="110" align="right" />
        <el-table-column label="操作" width="100" fixed="right"><template #default="{ row }"><el-button v-if="row.repairStatus === 0 || row.repairStatus === 1" link type="primary" @click="openHandle(row)">处理</el-button><span v-else class="muted">-</span></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>
    <el-dialog v-model="reportVisible" title="新建设备报修" width="680px" destroy-on-close>
      <el-form label-width="100px">
        <el-form-item label="故障设备" required><el-select v-model="reportForm.equipmentId" filterable placeholder="请选择设备" style="width: 100%"><el-option v-for="x in equipmentOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="故障级别"><el-select v-model="reportForm.level" style="width: 100%"><el-option v-for="x in levelOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="故障描述" required><el-input v-model="reportForm.faultDesc" type="textarea" :rows="4" maxlength="500" show-word-limit placeholder="描述故障现象、发生时间及影响" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="reportVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="submitReport">提交报修</el-button></template>
    </el-dialog>
    <el-dialog v-model="handleVisible" title="维修处理" width="720px" destroy-on-close>
      <el-alert v-if="activeOrder" :title="`${activeOrder.code || '报修单'} · ${equipmentName(activeOrder.equipmentId)} · ${activeOrder.faultDesc || ''}`" type="info" :closable="false" style="margin-bottom: 16px" />
      <el-form label-width="105px">
        <el-form-item label="处理结果" required><el-select v-model="handleForm.repairStatus" style="width: 100%"><el-option v-for="x in statusOptions.filter(x => x.value !== 0)" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="维修方案" required><el-input v-model="handleForm.solution" type="textarea" :rows="4" placeholder="填写维修过程及处理结果" /></el-form-item>
        <el-row :gutter="16"><el-col :span="12"><el-form-item label="维修费用"><el-input-number v-model="handleForm.cost" :min="0" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col><el-col :span="12"><el-form-item label="停机时长(分)"><el-input-number v-model="handleForm.downtimeMinutes" :min="0" :precision="0" controls-position="right" style="width: 100%" /></el-form-item></el-col></el-row>
      </el-form>
      <template #footer><el-button @click="handleVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="submitHandle">保存处理</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.repair-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .muted { color: var(--el-text-color-secondary); }
}
</style>
