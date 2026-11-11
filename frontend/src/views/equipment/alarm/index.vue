<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { alarmStatusDict, alarmTypeDict, levelDict, toOptions } from "@/utils/biz";

const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const alarmStatus = ref<number>();
const alarmType = ref<number>();
const equipmentId = ref<number>();
const equipmentOptions = ref<Array<{ label: string; value: number }>>([]);
const statusOptions = toOptions(alarmStatusDict);
const typeOptions = toOptions(alarmTypeDict);
const equipmentName = (id: number) => equipmentOptions.value.find(x => x.value === id)?.label || `设备#${id}`;
const tagInfo = (dict: Record<number, { label: string; type: string }>, value: number) => dict[value] ?? { label: String(value ?? "-"), type: "info" };

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/equipment-alarm", { params: {
      page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined,
      alarmStatus: alarmStatus.value, alarmType: alarmType.value, equipmentId: equipmentId.value
    } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "报警加载失败", { type: "error" });
  } catch (e: any) { message(e?.message || "报警加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; alarmStatus.value = undefined; alarmType.value = undefined; equipmentId.value = undefined; search(); }
async function changeAlarm(row: any, action: "ack" | "close") {
  const title = action === "ack" ? "确认此设备报警？" : "关闭此设备报警？";
  try { await ElMessageBox.confirm(`${title}\n${row.code || row.alarmCode || ""} ${row.content || ""}`, "请确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("post", `/equipment-alarm/${row.id}/${action}`);
    if (res?.code === 0) { message(action === "ack" ? "报警已确认" : "报警已关闭", { type: "success" }); await fetchList(); }
    else message(res?.message || "操作失败", { type: "error" });
  } catch (e: any) { message(e?.response?.data?.message || e?.message || "操作失败", { type: "error" }); }
}
onMounted(async () => {
  const res: any = await http.request("get", "/equipment/options");
  if (res?.code === 0) equipmentOptions.value = (res.data ?? []).map((x: any) => ({ label: `${x.code ? `${x.code} ` : ""}${x.name}`, value: x.id }));
  fetchList();
});
</script>

<template>
  <div class="alarm-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="报警编号 / 内容" style="width: 190px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="设备"><el-select v-model="equipmentId" clearable filterable placeholder="全部设备" style="width: 190px"><el-option v-for="x in equipmentOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="报警类型"><el-select v-model="alarmType" clearable placeholder="全部" style="width: 125px"><el-option v-for="x in typeOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="报警状态"><el-select v-model="alarmStatus" clearable placeholder="全部" style="width: 125px"><el-option v-for="x in statusOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">设备报警</span><el-button @click="fetchList">刷新</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe row-key="id">
        <el-table-column prop="code" label="报警编号" width="155" />
        <el-table-column label="设备" min-width="175" show-overflow-tooltip><template #default="{ row }">{{ equipmentName(row.equipmentId) }}</template></el-table-column>
        <el-table-column prop="alarmCode" label="报警代码" width="120" />
        <el-table-column label="报警类型" width="100"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(alarmTypeDict, row.alarmType).type as any)">{{ tagInfo(alarmTypeDict, row.alarmType).label }}</el-tag></template></el-table-column>
        <el-table-column prop="content" label="报警内容" min-width="220" show-overflow-tooltip />
        <el-table-column label="级别" width="90"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(levelDict, row.level).type as any)">{{ tagInfo(levelDict, row.level).label }}</el-tag></template></el-table-column>
        <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(alarmStatusDict, row.alarmStatus).type as any)">{{ tagInfo(alarmStatusDict, row.alarmStatus).label }}</el-tag></template></el-table-column>
        <el-table-column prop="occurredAt" label="发生时间" width="160"><template #default="{ row }">{{ String(row.occurredAt || "").replace("T", " ").slice(0, 16) || "-" }}</template></el-table-column>
        <el-table-column label="操作" width="145" fixed="right"><template #default="{ row }"><el-button v-if="row.alarmStatus === 0 || row.alarmStatus === 1" link type="primary" @click="changeAlarm(row, 'ack')">确认</el-button><el-button v-if="row.alarmStatus !== 3" link type="danger" @click="changeAlarm(row, 'close')">关闭</el-button><span v-if="row.alarmStatus === 3" class="muted">-</span></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>
  </div>
</template>

<style scoped lang="scss">
.alarm-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .muted { color: var(--el-text-color-secondary); }
}
</style>
