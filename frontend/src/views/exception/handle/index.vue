<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { exceptionStatusDict, exceptionTypeDict, levelDict, toOptions } from "@/utils/biz";

const loading = ref(false);
const saving = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const exceptionStatus = ref<number | undefined>(0);
const exceptionType = ref<number>();
const level = ref<number>();
const userOptions = ref<Array<{ label: string; value: number }>>([]);
const handleVisible = ref(false);
const activeRecord = ref<any>(null);
const form = reactive<any>({ handlerId: undefined, exceptionStatus: 1, solution: "" });
const statusOptions = toOptions(exceptionStatusDict);
const typeOptions = toOptions(exceptionTypeDict);
const levelOptions = toOptions(levelDict);
const tagInfo = (dict: Record<number, { label: string; type: string }>, value: number) => dict[value] ?? { label: String(value ?? "-"), type: "info" };
const userName = (id?: number) => id ? (userOptions.value.find(x => x.value === id)?.label || `用户#${id}`) : "-";

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/exception", { params: {
      page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined,
      exceptionStatus: exceptionStatus.value, exceptionType: exceptionType.value, level: level.value
    } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "待办异常加载失败", { type: "error" });
  } catch (e: any) { message(e?.message || "待办异常加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; exceptionStatus.value = 0; exceptionType.value = undefined; level.value = undefined; search(); }
async function openHandle(row: any) {
  try {
    const res: any = await http.request("get", `/exception/${row.id}`);
    if (res?.code !== 0) return message(res?.message || "异常详情加载失败", { type: "error" });
    activeRecord.value = res.data;
    Object.assign(form, { handlerId: res.data.handlerId ?? undefined, exceptionStatus: res.data.exceptionStatus === 0 ? 1 : res.data.exceptionStatus, solution: res.data.solution || "" });
    handleVisible.value = true;
  } catch (e: any) { message(e?.message || "异常详情加载失败", { type: "error" }); }
}
async function submitHandle() {
  if (!form.solution.trim()) return message("请填写处理方案", { type: "warning" });
  saving.value = true;
  try {
    const res: any = await http.request("post", `/exception/${activeRecord.value.id}/handle`, { data: {
      handlerId: form.handlerId || null, exceptionStatus: form.exceptionStatus, solution: form.solution.trim()
    } });
    if (res?.code === 0) { message("异常处理已保存", { type: "success" }); handleVisible.value = false; await fetchList(); }
    else message(res?.message || "处理失败", { type: "error" });
  } catch (e: any) { message(e?.response?.data?.message || e?.message || "处理失败", { type: "error" }); }
  finally { saving.value = false; }
}
onMounted(async () => {
  const res: any = await http.request("get", "/user/options");
  if (res?.code === 0) userOptions.value = (res.data ?? []).map((x: any) => ({ label: x.name || x.code || String(x.id), value: x.id }));
  fetchList();
});
</script>

<template>
  <div class="exception-handle-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="异常编号 / 标题" style="width: 190px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="状态"><el-select v-model="exceptionStatus" clearable placeholder="全部" style="width: 120px"><el-option v-for="x in statusOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="类型"><el-select v-model="exceptionType" clearable placeholder="全部" style="width: 120px"><el-option v-for="x in typeOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="级别"><el-select v-model="level" clearable placeholder="全部" style="width: 110px"><el-option v-for="x in levelOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">异常处理待办</span><el-button @click="fetchList">刷新</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe row-key="id">
        <el-table-column prop="code" label="异常编号" width="155" />
        <el-table-column prop="title" label="异常标题" min-width="180" show-overflow-tooltip />
        <el-table-column label="类型" width="95"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(exceptionTypeDict, row.exceptionType).type as any)">{{ tagInfo(exceptionTypeDict, row.exceptionType).label }}</el-tag></template></el-table-column>
        <el-table-column label="级别" width="85"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(levelDict, row.level).type as any)">{{ tagInfo(levelDict, row.level).label }}</el-tag></template></el-table-column>
        <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(exceptionStatusDict, row.exceptionStatus).type as any)">{{ tagInfo(exceptionStatusDict, row.exceptionStatus).label }}</el-tag></template></el-table-column>
        <el-table-column label="提报人" width="120"><template #default="{ row }">{{ userName(row.reporterId) }}</template></el-table-column>
        <el-table-column prop="reportTime" label="提报时间" width="160"><template #default="{ row }">{{ String(row.reportTime || "").replace("T", " ").slice(0, 16) || "-" }}</template></el-table-column>
        <el-table-column label="处理人" width="120"><template #default="{ row }">{{ userName(row.handlerId) }}</template></el-table-column>
        <el-table-column label="操作" width="100" fixed="right"><template #default="{ row }"><el-button v-if="row.exceptionStatus !== 2" link type="primary" @click="openHandle(row)">处理</el-button><el-button v-else link type="info" @click="openHandle(row)">查看</el-button></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>
    <el-dialog v-model="handleVisible" :title="activeRecord?.exceptionStatus === 2 ? '异常处理记录' : '处理异常'" width="760px" destroy-on-close>
      <template v-if="activeRecord">
        <el-descriptions :column="2" border style="margin-bottom: 18px">
          <el-descriptions-item label="异常编号">{{ activeRecord.code || "-" }}</el-descriptions-item>
          <el-descriptions-item label="标题">{{ activeRecord.title || "-" }}</el-descriptions-item>
          <el-descriptions-item label="类型">{{ tagInfo(exceptionTypeDict, activeRecord.exceptionType).label }}</el-descriptions-item>
          <el-descriptions-item label="级别">{{ tagInfo(levelDict, activeRecord.level).label }}</el-descriptions-item>
          <el-descriptions-item label="来源">{{ activeRecord.source || "-" }}</el-descriptions-item>
          <el-descriptions-item label="提报时间">{{ String(activeRecord.reportTime || "").replace("T", " ").slice(0, 16) || "-" }}</el-descriptions-item>
          <el-descriptions-item label="异常描述" :span="2">{{ activeRecord.description || "-" }}</el-descriptions-item>
        </el-descriptions>
        <el-form label-width="100px">
          <el-form-item label="处理状态" required><el-select v-model="form.exceptionStatus" :disabled="activeRecord.exceptionStatus === 2" style="width: 100%"><el-option v-for="x in statusOptions.filter(x => x.value !== 0)" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
          <el-form-item label="处理人"><el-select v-model="form.handlerId" clearable filterable :disabled="activeRecord.exceptionStatus === 2" placeholder="留空使用当前登录人" style="width: 100%"><el-option v-for="x in userOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
          <el-form-item label="处理方案" required><el-input v-model="form.solution" type="textarea" :rows="4" :disabled="activeRecord.exceptionStatus === 2" placeholder="记录原因分析、处置过程及结果" /></el-form-item>
        </el-form>
      </template>
      <template #footer><el-button @click="handleVisible = false">关闭</el-button><el-button v-if="activeRecord?.exceptionStatus !== 2" type="primary" :loading="saving" @click="submitHandle">保存处理</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.exception-handle-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
}
</style>
