<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { maintainTypeDict, taskStatusDict, toOptions } from "@/utils/biz";

const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const taskStatus = ref<number>();
const maintenanceType = ref<number>();
const equipmentId = ref<number>();
const equipmentOptions = ref<Array<{ label: string; value: number }>>([]);
const planOptions = ref<Array<{ label: string; value: number }>>([]);
const createVisible = ref(false);
const executeVisible = ref(false);
const saving = ref(false);
const activeTask = ref<any>(null);
const createForm = reactive<any>({ planId: undefined, equipmentId: undefined, maintenanceType: 1, taskDate: "", items: "" });
const executeForm = reactive<any>({ result: "", cost: 0, remark: "" });
const typeOptions = toOptions(maintainTypeDict);
const statusOptions = toOptions(taskStatusDict);

const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
const equipmentName = (id: number) => equipmentOptions.value.find(x => x.value === id)?.label || `设备#${id}`;
const tagInfo = (dict: Record<number, { label: string; type: string }>, value: number) => dict[value] ?? { label: String(value ?? "-"), type: "info" };
const createDialogTitle = computed(() => "新建点检/保养任务");

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/maintenance-task", {
      params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined, taskStatus: taskStatus.value, maintenanceType: maintenanceType.value, equipmentId: equipmentId.value }
    });
    if (res?.code === 0) {
      list.value = res.data?.list ?? [];
      total.value = res.data?.total ?? 0;
    } else message(res?.message || "任务加载失败", { type: "error" });
  } catch (e: any) {
    message(e?.message || "任务加载失败", { type: "error" });
  } finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; taskStatus.value = undefined; maintenanceType.value = undefined; equipmentId.value = undefined; search(); }
function openCreate() {
  Object.assign(createForm, { planId: undefined, equipmentId: undefined, maintenanceType: 1, taskDate: today(), items: "" });
  createVisible.value = true;
}
async function createTask() {
  if (!createForm.equipmentId || !createForm.taskDate) return message("请选择设备和任务日期", { type: "warning" });
  saving.value = true;
  try {
    const res: any = await http.request("post", "/maintenance-task", { data: {
      planId: createForm.planId || null,
      equipmentId: createForm.equipmentId,
      maintenanceType: createForm.maintenanceType,
      taskDate: createForm.taskDate,
      items: createForm.items || null
    } });
    if (res?.code === 0) {
      message("任务创建成功", { type: "success" });
      createVisible.value = false;
      await fetchList();
    } else message(res?.message || "任务创建失败", { type: "error" });
  } catch (e: any) { message(e?.response?.data?.message || e?.message || "任务创建失败", { type: "error" }); }
  finally { saving.value = false; }
}
function openExecute(row: any) {
  activeTask.value = row;
  Object.assign(executeForm, { result: "", cost: 0, remark: "" });
  executeVisible.value = true;
}
async function executeTask() {
  if (!executeForm.result.trim()) return message("请填写执行结果", { type: "warning" });
  saving.value = true;
  try {
    const res: any = await http.request("post", `/maintenance-task/${activeTask.value.id}/execute`, { data: {
      result: executeForm.result,
      cost: Number(executeForm.cost) || 0,
      remark: executeForm.remark || null
    } });
    if (res?.code === 0) {
      message("任务执行已提交", { type: "success" });
      executeVisible.value = false;
      await fetchList();
    } else message(res?.message || "执行失败", { type: "error" });
  } catch (e: any) { message(e?.response?.data?.message || e?.message || "执行失败", { type: "error" }); }
  finally { saving.value = false; }
}
onMounted(async () => {
  const [equipmentRes, planRes]: any[] = await Promise.all([
    http.request("get", "/equipment/options"),
    http.request("get", "/maintenance-plan/options")
  ]);
  if (equipmentRes?.code === 0) equipmentOptions.value = (equipmentRes.data ?? []).map((x: any) => ({ label: `${x.code ? `${x.code} ` : ""}${x.name}`, value: x.id }));
  if (planRes?.code === 0) planOptions.value = (planRes.data ?? []).map((x: any) => ({ label: `${x.code ? `${x.code} ` : ""}${x.name}`, value: x.id }));
  fetchList();
});
</script>

<template>
  <div class="equipment-task-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="任务编号 / 名称" style="width: 190px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="设备"><el-select v-model="equipmentId" clearable filterable placeholder="全部设备" style="width: 190px"><el-option v-for="x in equipmentOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="任务类型"><el-select v-model="maintenanceType" clearable placeholder="全部" style="width: 130px"><el-option v-for="x in typeOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="任务状态"><el-select v-model="taskStatus" clearable placeholder="全部" style="width: 130px"><el-option v-for="x in statusOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">点检保养任务</span><el-button type="primary" @click="openCreate">新建任务</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe row-key="id">
        <el-table-column prop="code" label="任务编号" width="165" />
        <el-table-column prop="name" label="任务名称" min-width="145" show-overflow-tooltip />
        <el-table-column label="设备" min-width="180" show-overflow-tooltip><template #default="{ row }">{{ equipmentName(row.equipmentId) }}</template></el-table-column>
        <el-table-column label="类型" width="95"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(maintainTypeDict, row.maintenanceType).type as any)">{{ tagInfo(maintainTypeDict, row.maintenanceType).label }}</el-tag></template></el-table-column>
        <el-table-column prop="taskDate" label="任务日期" width="115"><template #default="{ row }">{{ String(row.taskDate || "").slice(0, 10) || "-" }}</template></el-table-column>
        <el-table-column label="任务状态" width="105"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(taskStatusDict, row.taskStatus).type as any)">{{ tagInfo(taskStatusDict, row.taskStatus).label }}</el-tag></template></el-table-column>
        <el-table-column prop="result" label="执行结果" min-width="160" show-overflow-tooltip />
        <el-table-column prop="cost" label="费用" width="100" align="right" />
        <el-table-column label="操作" width="100" fixed="right"><template #default="{ row }"><el-button v-if="row.taskStatus === 0" link type="primary" @click="openExecute(row)">执行</el-button><span v-else class="muted">-</span></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>
    <el-dialog v-model="createVisible" :title="createDialogTitle" width="720px" destroy-on-close>
      <el-form label-width="105px">
        <el-row :gutter="16">
          <el-col :span="12"><el-form-item label="关联计划"><el-select v-model="createForm.planId" clearable filterable placeholder="可不关联" style="width: 100%"><el-option v-for="x in planOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="设备" required><el-select v-model="createForm.equipmentId" filterable placeholder="请选择设备" style="width: 100%"><el-option v-for="x in equipmentOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="任务类型"><el-select v-model="createForm.maintenanceType" style="width: 100%"><el-option v-for="x in typeOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="任务日期"><el-date-picker v-model="createForm.taskDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item label="任务项目"><el-input v-model="createForm.items" type="textarea" :rows="3" placeholder="记录本次点检/保养项目" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer><el-button @click="createVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="createTask">创建任务</el-button></template>
    </el-dialog>
    <el-dialog v-model="executeVisible" title="执行点检/保养任务" width="640px" destroy-on-close>
      <el-alert v-if="activeTask" :title="`${activeTask.code || '任务'} · ${equipmentName(activeTask.equipmentId)}`" type="info" :closable="false" style="margin-bottom: 16px" />
      <el-form label-width="100px">
        <el-form-item label="执行结果" required><el-input v-model="executeForm.result" type="textarea" :rows="4" placeholder="填写检查结果；如含“异常”将标记为异常任务" /></el-form-item>
        <el-form-item label="费用"><el-input-number v-model="executeForm.cost" :min="0" :precision="2" controls-position="right" style="width: 220px" /></el-form-item>
        <el-form-item label="备注"><el-input v-model="executeForm.remark" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="executeVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="executeTask">提交执行结果</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.equipment-task-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .muted { color: var(--el-text-color-secondary); }
}
</style>
