<script setup lang="ts">
import { onMounted, ref } from "vue";
import { message } from "@/utils/message";
import { http } from "@/utils/http";

const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const detailVisible = ref(false);
const selected = ref<any>(null);

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/log", { params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined } });
    if (res?.code === 0) {
      list.value = res.data?.list ?? [];
      total.value = res.data?.total ?? 0;
    } else message(res?.message || "操作日志加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "操作日志加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; search(); }
function showDetail(row: any) { selected.value = row; detailVisible.value = true; }
function dateText(value?: string) { return value ? String(value).replace("T", " ").slice(0, 19) : "-"; }
function paramsText(value: any) {
  if (!value) return "无请求参数";
  if (typeof value !== "string") return JSON.stringify(value, null, 2);
  try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; }
}
onMounted(fetchList);
</script>

<template>
  <div class="log-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="操作人 / 模块名称" style="width: 260px" @keyup.enter="search" /></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><div class="title">操作日志 <span class="subtle">仅供查询，不支持修改或删除</span></div><el-button @click="fetchList">刷新</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe row-key="id">
        <el-table-column label="操作时间" width="175"><template #default="{ row }">{{ dateText(row.createdAt) }}</template></el-table-column>
        <el-table-column prop="username" label="操作人" width="120" show-overflow-tooltip />
        <el-table-column prop="module" label="模块" width="120" show-overflow-tooltip />
        <el-table-column prop="action" label="操作内容" min-width="150" show-overflow-tooltip />
        <el-table-column prop="method" label="请求方式" width="100" align="center"><template #default="{ row }"><el-tag size="small" effect="plain">{{ row.method || "-" }}</el-tag></template></el-table-column>
        <el-table-column prop="path" label="请求路径" min-width="220" show-overflow-tooltip />
        <el-table-column prop="ip" label="IP 地址" width="140" />
        <el-table-column prop="elapsedMs" label="耗时（毫秒）" width="115" align="right" />
        <el-table-column label="结果" width="90" align="center"><template #default="{ row }"><el-tag :type="row.success === 1 ? 'success' : 'danger'" size="small">{{ row.success === 1 ? "成功" : "失败" }}</el-tag></template></el-table-column>
        <el-table-column label="详情" width="80" fixed="right" align="center"><template #default="{ row }"><el-button link type="primary" @click="showDetail(row)">查看</el-button></template></el-table-column>
        <template #empty><el-empty description="暂无操作日志" /></template>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50, 100]" layout="total, sizes, prev, pager, next, jumper" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>
    <el-dialog v-model="detailVisible" title="日志详情" width="760px" destroy-on-close>
      <el-descriptions v-if="selected" :column="2" border>
        <el-descriptions-item label="操作时间">{{ dateText(selected.createdAt) }}</el-descriptions-item>
        <el-descriptions-item label="操作人">{{ selected.username || "-" }}</el-descriptions-item>
        <el-descriptions-item label="模块">{{ selected.module || "-" }}</el-descriptions-item>
        <el-descriptions-item label="操作内容">{{ selected.action || "-" }}</el-descriptions-item>
        <el-descriptions-item label="请求方式">{{ selected.method || "-" }}</el-descriptions-item>
        <el-descriptions-item label="请求路径">{{ selected.path || "-" }}</el-descriptions-item>
        <el-descriptions-item label="IP 地址">{{ selected.ip || "-" }}</el-descriptions-item>
        <el-descriptions-item label="耗时">{{ selected.elapsedMs }} ms</el-descriptions-item>
        <el-descriptions-item label="请求参数" :span="2"><pre class="params">{{ paramsText(selected.params) }}</pre></el-descriptions-item>
      </el-descriptions>
      <template #footer><el-button type="primary" @click="detailVisible = false">关闭</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.log-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .subtle { margin-left: 8px; color: var(--el-text-color-secondary); font-size: 12px; font-weight: 400; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .params { max-height: 360px; margin: 0; overflow: auto; white-space: pre-wrap; word-break: break-all; color: var(--el-text-color-regular); }
}
</style>
