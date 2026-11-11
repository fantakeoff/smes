<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { http } from "@/utils/http";
import { message } from "@/utils/message";

const transactionType = 2;
const loading = ref(false);
const saving = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const toolId = ref<number>();
const toolOptions = ref<Array<{ label: string; value: number }>>([]);
const toolRows = ref<any[]>([]);
const userOptions = ref<Array<{ label: string; value: number }>>([]);
const dialogVisible = ref(false);
const form = reactive<any>({ toolId: undefined, qty: 1, userId: undefined, occurDate: "" });
const selectedTool = computed(() => toolRows.value.find(x => x.id === form.toolId));
const selectedOutstanding = computed(() => Math.max(0, Number(selectedTool.value?.totalQty ?? 0) - Number(selectedTool.value?.availableQty ?? 0)));
const toolName = (id: number) => toolOptions.value.find(x => x.value === id)?.label || `工装#${id}`;
const userName = (id?: number) => id ? (userOptions.value.find(x => x.value === id)?.label || `用户#${id}`) : "当前用户";
const today = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; };

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/tool-transaction", { params: {
      page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined,
      transactionType, toolId: toolId.value
    } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "归还记录加载失败", { type: "error" });
  } catch (e: any) { message(e?.message || "归还记录加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; toolId.value = undefined; search(); }
function openCreate() { Object.assign(form, { toolId: undefined, qty: 1, userId: undefined, occurDate: today() }); dialogVisible.value = true; }
async function changeTool(id?: number) {
  if (!id) return;
  const res: any = await http.request("get", `/tool/${id}`);
  if (res?.code === 0 && res.data) {
    const index = toolRows.value.findIndex(x => x.id === id);
    if (index >= 0) toolRows.value.splice(index, 1, res.data);
    else toolRows.value.push(res.data);
  }
}
async function submit() {
  if (!form.toolId || !form.occurDate) return message("请选择工装和归还日期", { type: "warning" });
  if (!(Number(form.qty) > 0)) return message("归还数量必须大于 0", { type: "warning" });
  if (Number(form.qty) > selectedOutstanding.value) return message(`归还数量超过未归还数量，当前最多可归还 ${selectedOutstanding.value}`, { type: "warning" });
  saving.value = true;
  try {
    const res: any = await http.request("post", "/tool-transaction", { data: {
      toolId: form.toolId, transactionType, qty: Number(form.qty), userId: form.userId || null,
      occurDate: form.occurDate, expectedReturnDate: null
    } });
    if (res?.code === 0) { message("工装归还成功", { type: "success" }); dialogVisible.value = false; await fetchList(); await loadTools(); }
    else message(res?.message || "归还失败", { type: "error" });
  } catch (e: any) { message(e?.response?.data?.message || e?.message || "归还失败", { type: "error" }); }
  finally { saving.value = false; }
}
async function loadTools() {
  const [optionRes, listRes]: any[] = await Promise.all([
    http.request("get", "/tool/options"),
    http.request("get", "/tool", { params: { page: 1, pageSize: 100 } })
  ]);
  if (optionRes?.code === 0) toolOptions.value = (optionRes.data ?? []).map((x: any) => ({ label: `${x.code ? `${x.code} ` : ""}${x.name}`, value: x.id }));
  if (listRes?.code === 0) toolRows.value = listRes.data?.list ?? [];
}
onMounted(async () => {
  const userRes: any = await http.request("get", "/user/options");
  if (userRes?.code === 0) userOptions.value = (userRes.data ?? []).map((x: any) => ({ label: x.name || x.code || String(x.id), value: x.id }));
  await loadTools();
  fetchList();
});
</script>

<template>
  <div class="transaction-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="单据编号 / 工装名称" style="width: 200px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="工装"><el-select v-model="toolId" clearable filterable placeholder="全部工装" style="width: 210px"><el-option v-for="x in toolOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">工装归还记录</span><el-button type="primary" @click="openCreate">新建归还</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe row-key="id">
        <el-table-column prop="code" label="归还单号" width="160" />
        <el-table-column label="工装" min-width="190" show-overflow-tooltip><template #default="{ row }">{{ toolName(row.toolId) }}</template></el-table-column>
        <el-table-column prop="qty" label="归还数量" width="100" align="right" />
        <el-table-column label="归还人" width="130"><template #default="{ row }">{{ userName(row.userId) }}</template></el-table-column>
        <el-table-column prop="occurDate" label="归还日期" width="120"><template #default="{ row }">{{ String(row.occurDate || "").slice(0, 10) || "-" }}</template></el-table-column>
        <el-table-column prop="returnDate" label="入库日期" width="120"><template #default="{ row }">{{ row.returnDate ? String(row.returnDate).slice(0, 10) : "-" }}</template></el-table-column>
        <el-table-column label="单据状态" width="100"><template #default="{ row }"><el-tag size="small" :type="row.transactionStatus === 1 ? 'success' : 'info'">{{ row.transactionStatus === 1 ? "已完成" : "草稿" }}</el-tag></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>
    <el-dialog v-model="dialogVisible" title="新建工装归还" width="680px" destroy-on-close>
      <el-form label-width="105px">
        <el-form-item label="归还工装" required><el-select v-model="form.toolId" filterable placeholder="请选择工装" style="width: 100%" @change="changeTool"><el-option v-for="x in toolOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-alert v-if="form.toolId" :title="`当前未归还数量：${selectedOutstanding}`" type="info" :closable="false" style="margin: -6px 0 16px 105px" />
        <el-row :gutter="16"><el-col :span="12"><el-form-item label="归还数量" required><el-input-number v-model="form.qty" :min="0.01" :max="selectedOutstanding || undefined" :precision="2" controls-position="right" style="width: 100%" /></el-form-item></el-col><el-col :span="12"><el-form-item label="归还人"><el-select v-model="form.userId" clearable filterable placeholder="默认当前登录人" style="width: 100%"><el-option v-for="x in userOptions" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item></el-col><el-col :span="12"><el-form-item label="归还日期" required><el-date-picker v-model="form.occurDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item></el-col></el-row>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="submit">确认归还</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.transaction-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
}
</style>
