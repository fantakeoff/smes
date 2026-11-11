<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";

const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const dialogVisible = ref(false);
const detailVisible = ref(false);
const submitting = ref(false);
const editId = ref<number | null>(null);
const activeNotice = ref<any>(null);
const formRef = ref<any>();
const form = reactive<any>({ title: "", content: "", type: 1 });
const rules = {
  title: [{ required: true, message: "请输入标题", trigger: "blur" }],
  content: [{ required: true, message: "请输入公告内容", trigger: "blur" }]
};

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/notice", { params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "通知公告加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "通知公告加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function resetSearch() { keyword.value = ""; search(); }
function dateText(value?: string) { return value ? String(value).replace("T", " ").slice(0, 19) : "-"; }
function openCreate() {
  editId.value = null;
  Object.assign(form, { title: "", content: "", type: 1 });
  dialogVisible.value = true;
}
function openEdit(row: any) {
  editId.value = Number(row.id);
  Object.assign(form, { title: row.title ?? "", content: row.content ?? "", type: Number(row.type ?? 1) });
  dialogVisible.value = true;
}
function showDetail(row: any) { activeNotice.value = row; detailVisible.value = true; }
async function save() {
  const valid = await formRef.value?.validate().catch(() => false);
  if (!valid) return;
  submitting.value = true;
  try {
    const payload = { title: form.title.trim(), content: form.content.trim(), type: Number(form.type) };
    const res: any = editId.value
      ? await http.request("put", `/notice/${editId.value}`, { data: payload })
      : await http.request("post", "/notice", { data: payload });
    if (res?.code === 0) {
      message(editId.value ? "通知公告已修改" : "通知公告已发布", { type: "success" });
      dialogVisible.value = false;
      await fetchList();
    } else message(res?.message || "保存失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "保存失败", { type: "error" }); }
  finally { submitting.value = false; }
}
async function remove(row: any) {
  try { await ElMessageBox.confirm(`确定删除「${row.title}」吗？`, "删除确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("delete", `/notice/${row.id}`);
    if (res?.code === 0) {
      message("通知公告已删除", { type: "success" });
      if (list.value.length === 1 && page.value > 1) page.value -= 1;
      await fetchList();
    } else message(res?.message || "删除失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "删除失败", { type: "error" }); }
}

onMounted(fetchList);
</script>

<template>
  <div class="notice-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="标题 / 内容" style="width: 260px" @keyup.enter="search" /></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="resetSearch">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><div class="title">通知公告</div><el-button type="primary" @click="openCreate">发布通知</el-button></div>
      <el-table v-loading="loading" :data="list" row-key="id" border stripe>
        <el-table-column prop="title" label="标题" min-width="220" show-overflow-tooltip />
        <el-table-column label="类型" width="100" align="center"><template #default="{ row }"><el-tag :type="row.type === 2 ? 'warning' : 'primary'" size="small">{{ row.type === 2 ? "公告" : "通知" }}</el-tag></template></el-table-column>
        <el-table-column label="内容摘要" min-width="260" show-overflow-tooltip><template #default="{ row }">{{ row.content }}</template></el-table-column>
        <el-table-column label="发布时间" width="175"><template #default="{ row }">{{ dateText(row.publishTime) }}</template></el-table-column>
        <el-table-column prop="publisherId" label="发布人ID" width="105" align="center" />
        <el-table-column label="操作" width="170" fixed="right" align="center">
          <template #default="{ row }"><el-button link type="primary" @click="showDetail(row)">查看</el-button><el-button link type="primary" @click="openEdit(row)">编辑</el-button><el-button link type="danger" @click="remove(row)">删除</el-button></template>
        </el-table-column>
        <template #empty><el-empty description="暂无通知公告" /></template>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next, jumper" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editId ? '编辑通知公告' : '发布通知公告'" width="760px" destroy-on-close>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="100px">
        <el-form-item label="标题" prop="title"><el-input v-model="form.title" maxlength="120" show-word-limit /></el-form-item>
        <el-form-item label="类型"><el-radio-group v-model="form.type"><el-radio :value="1">通知</el-radio><el-radio :value="2">公告</el-radio></el-radio-group></el-form-item>
        <el-form-item label="内容" prop="content"><el-input v-model="form.content" type="textarea" :rows="10" maxlength="10000" show-word-limit placeholder="请输入通知或公告内容" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="submitting" @click="save">{{ editId ? "保存修改" : "发布" }}</el-button></template>
    </el-dialog>

    <el-dialog v-model="detailVisible" title="通知公告详情" width="720px" destroy-on-close>
      <template v-if="activeNotice">
        <div class="detail-heading"><el-tag :type="activeNotice.type === 2 ? 'warning' : 'primary'" size="small">{{ activeNotice.type === 2 ? "公告" : "通知" }}</el-tag><strong>{{ activeNotice.title }}</strong></div>
        <div class="detail-meta">发布时间：{{ dateText(activeNotice.publishTime) }}　发布人ID：{{ activeNotice.publisherId ?? "-" }}</div>
        <div class="detail-content">{{ activeNotice.content }}</div>
      </template>
      <template #footer><el-button @click="detailVisible = false">关闭</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.notice-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .detail-heading { display: flex; align-items: center; gap: 10px; font-size: 18px; }
  .detail-meta { margin: 12px 0 18px; color: var(--el-text-color-secondary); font-size: 13px; }
  .detail-content { white-space: pre-wrap; line-height: 1.8; word-break: break-word; }
}
</style>
