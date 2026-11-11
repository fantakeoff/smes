<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
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
const exceptionStatus = ref<number>();
const exceptionType = ref<number>();
const level = ref<number>();
const createVisible = ref(false);
const detailVisible = ref(false);
const detail = ref<any>(null);
const form = reactive<any>({ exceptionType: 1, title: "", description: "", level: 1, source: "" });
const statusOptions = toOptions(exceptionStatusDict);
const typeOptions = toOptions(exceptionTypeDict);
const levelOptions = toOptions(levelDict);
const tagInfo = (dict: Record<number, { label: string; type: string }>, value: number) => dict[value] ?? { label: String(value ?? "-"), type: "info" };

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/exception", { params: {
      page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined,
      exceptionStatus: exceptionStatus.value, exceptionType: exceptionType.value, level: level.value
    } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "异常列表加载失败", { type: "error" });
  } catch (e: any) { message(e?.message || "异常列表加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; exceptionStatus.value = undefined; exceptionType.value = undefined; level.value = undefined; search(); }
function openCreate() { Object.assign(form, { exceptionType: 1, title: "", description: "", level: 1, source: "" }); createVisible.value = true; }
async function submit() {
  if (!form.title.trim()) return message("请填写异常标题", { type: "warning" });
  if (!form.description.trim()) return message("请填写异常描述", { type: "warning" });
  saving.value = true;
  try {
    const res: any = await http.request("post", "/exception", { data: {
      exceptionType: form.exceptionType, title: form.title.trim(), description: form.description.trim(),
      level: form.level, source: form.source.trim() || null
    } });
    if (res?.code === 0) { message("异常已提报", { type: "success" }); createVisible.value = false; await fetchList(); }
    else message(res?.message || "提报失败", { type: "error" });
  } catch (e: any) { message(e?.response?.data?.message || e?.message || "提报失败", { type: "error" }); }
  finally { saving.value = false; }
}
async function openDetail(row: any) {
  try {
    const res: any = await http.request("get", `/exception/${row.id}`);
    if (res?.code === 0) { detail.value = res.data; detailVisible.value = true; }
    else message(res?.message || "异常详情加载失败", { type: "error" });
  } catch (e: any) { message(e?.message || "异常详情加载失败", { type: "error" }); }
}
async function remove(row: any) {
  try { await ElMessageBox.confirm(`确定删除异常「${row.title || row.code}」吗？`, "删除确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("delete", `/exception/${row.id}`);
    if (res?.code === 0) { message("异常已删除", { type: "success" }); await fetchList(); }
    else message(res?.message || "删除失败", { type: "error" });
  } catch (e: any) { message(e?.response?.data?.message || e?.message || "删除失败", { type: "error" }); }
}
onMounted(fetchList);
</script>

<template>
  <div class="exception-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="异常编号 / 标题" style="width: 190px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="异常类型"><el-select v-model="exceptionType" clearable placeholder="全部" style="width: 120px"><el-option v-for="x in typeOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="级别"><el-select v-model="level" clearable placeholder="全部" style="width: 110px"><el-option v-for="x in levelOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="状态"><el-select v-model="exceptionStatus" clearable placeholder="全部" style="width: 120px"><el-option v-for="x in statusOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">现场异常提报</span><el-button type="primary" @click="openCreate">新建提报</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe row-key="id">
        <el-table-column prop="code" label="异常编号" width="155" />
        <el-table-column prop="title" label="异常标题" min-width="180" show-overflow-tooltip />
        <el-table-column label="类型" width="100"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(exceptionTypeDict, row.exceptionType).type as any)">{{ tagInfo(exceptionTypeDict, row.exceptionType).label }}</el-tag></template></el-table-column>
        <el-table-column label="级别" width="90"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(levelDict, row.level).type as any)">{{ tagInfo(levelDict, row.level).label }}</el-tag></template></el-table-column>
        <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag size="small" :type="(tagInfo(exceptionStatusDict, row.exceptionStatus).type as any)">{{ tagInfo(exceptionStatusDict, row.exceptionStatus).label }}</el-tag></template></el-table-column>
        <el-table-column prop="source" label="来源" width="130" show-overflow-tooltip />
        <el-table-column prop="reportTime" label="提报时间" width="160"><template #default="{ row }">{{ String(row.reportTime || "").replace("T", " ").slice(0, 16) || "-" }}</template></el-table-column>
        <el-table-column label="操作" width="130" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="openDetail(row)">详情</el-button><el-button link type="danger" @click="remove(row)">删除</el-button></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>
    <el-dialog v-model="createVisible" title="新建异常提报" width="720px" destroy-on-close>
      <el-form label-width="100px">
        <el-row :gutter="16"><el-col :span="12"><el-form-item label="异常类型"><el-select v-model="form.exceptionType" style="width: 100%"><el-option v-for="x in typeOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item></el-col><el-col :span="12"><el-form-item label="异常级别"><el-select v-model="form.level" style="width: 100%"><el-option v-for="x in levelOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item></el-col><el-col :span="24"><el-form-item label="异常标题" required><el-input v-model="form.title" maxlength="100" show-word-limit placeholder="简要说明异常" /></el-form-item></el-col><el-col :span="24"><el-form-item label="异常描述" required><el-input v-model="form.description" type="textarea" :rows="4" maxlength="1000" show-word-limit /></el-form-item></el-col><el-col :span="24"><el-form-item label="来源"><el-input v-model="form.source" placeholder="工位、设备或业务来源（可选）" /></el-form-item></el-col></el-row>
      </el-form>
      <template #footer><el-button @click="createVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="submit">提交异常</el-button></template>
    </el-dialog>
    <el-drawer v-model="detailVisible" title="异常详情" size="560px">
      <template v-if="detail">
        <el-descriptions :column="1" border>
          <el-descriptions-item label="异常编号">{{ detail.code || "-" }}</el-descriptions-item>
          <el-descriptions-item label="标题">{{ detail.title || "-" }}</el-descriptions-item>
          <el-descriptions-item label="类型">{{ tagInfo(exceptionTypeDict, detail.exceptionType).label }}</el-descriptions-item>
          <el-descriptions-item label="级别">{{ tagInfo(levelDict, detail.level).label }}</el-descriptions-item>
          <el-descriptions-item label="状态">{{ tagInfo(exceptionStatusDict, detail.exceptionStatus).label }}</el-descriptions-item>
          <el-descriptions-item label="来源">{{ detail.source || "-" }}</el-descriptions-item>
          <el-descriptions-item label="提报时间">{{ String(detail.reportTime || "").replace("T", " ").slice(0, 16) || "-" }}</el-descriptions-item>
          <el-descriptions-item label="异常描述">{{ detail.description || "-" }}</el-descriptions-item>
          <el-descriptions-item label="处理方案">{{ detail.solution || "-" }}</el-descriptions-item>
        </el-descriptions>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped lang="scss">
.exception-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
}
</style>
