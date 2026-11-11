<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";

const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const unreadCount = ref(0);
const page = ref(1);
const pageSize = ref(20);
const readFilter = ref<"all" | "unread" | "read">("all");
const detailVisible = ref(false);
const activeMessage = ref<any>(null);

async function fetchUnreadCount() {
  try {
    const res: any = await http.request("get", "/message/unread-count");
    if (res?.code === 0) unreadCount.value = Number(res.data ?? 0);
  } catch { /* 未读数失败不影响收件箱列表 */ }
}
async function fetchList() {
  loading.value = true;
  try {
    const params: Record<string, any> = { page: page.value, pageSize: pageSize.value };
    if (readFilter.value !== "all") params.isRead = readFilter.value === "read";
    const res: any = await http.request("get", "/message", { params });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "消息加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "消息加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
async function refresh() { await Promise.all([fetchList(), fetchUnreadCount()]); }
function changeFilter() { page.value = 1; fetchList(); }
function dateText(value?: string) { return value ? String(value).replace("T", " ").slice(0, 19) : "-"; }
function typeText(type: number) { return ({ 1: "系统通知", 2: "站内信", 3: "业务提醒" } as Record<number, string>)[type] ?? "消息"; }
function typeTag(type: number) { return type === 1 ? "primary" : type === 3 ? "warning" : "info"; }
function showDetail(row: any) { activeMessage.value = row; detailVisible.value = true; }
async function markRead(row: any) {
  if (row.isRead) return;
  try {
    const res: any = await http.request("post", `/message/${row.id}/read`);
    if (res?.code === 0) {
      row.isRead = true;
      row.readAt = new Date().toISOString();
      if (activeMessage.value?.id === row.id) activeMessage.value = { ...row };
      message("已标记为已读", { type: "success" });
      await fetchUnreadCount();
    } else message(res?.message || "标记已读失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "标记已读失败", { type: "error" }); }
}
async function markAllRead() {
  if (unreadCount.value === 0) return;
  try { await ElMessageBox.confirm(`确定将 ${unreadCount.value} 条未读消息全部标记为已读吗？`, "全部已读", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("post", "/message/read-all");
    if (res?.code === 0) {
      message("所有消息已标记为已读", { type: "success" });
      await refresh();
    } else message(res?.message || "操作失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "操作失败", { type: "error" }); }
}
async function remove(row: any) {
  try { await ElMessageBox.confirm(`确定删除消息「${row.title}」吗？`, "删除确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("delete", `/message/${row.id}`);
    if (res?.code === 0) {
      message("消息已删除", { type: "success" });
      if (list.value.length === 1 && page.value > 1) page.value -= 1;
      await refresh();
    } else message(res?.message || "删除失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "删除失败", { type: "error" }); }
}
function rowClassName({ row }: { row: any }) { return row.isRead ? "read-row" : "unread-row"; }

onMounted(refresh);
</script>

<template>
  <div class="inbox-page">
    <el-card shadow="never">
      <div class="toolbar">
        <div class="title-wrap"><div class="title">我的消息</div><el-badge :value="unreadCount" :hidden="unreadCount === 0" type="danger"><span class="unread-label">未读</span></el-badge></div>
        <div class="toolbar-actions">
          <el-select v-model="readFilter" style="width: 130px" @change="changeFilter"><el-option label="全部消息" value="all" /><el-option label="未读消息" value="unread" /><el-option label="已读消息" value="read" /></el-select>
          <el-button :disabled="unreadCount === 0" @click="markAllRead">全部标为已读</el-button>
          <el-button @click="refresh">刷新</el-button>
        </div>
      </div>
      <el-table v-loading="loading" :data="list" row-key="id" border stripe :row-class-name="rowClassName">
        <el-table-column label="状态" width="82" align="center"><template #default="{ row }"><el-tag :type="row.isRead ? 'info' : 'danger'" size="small">{{ row.isRead ? "已读" : "未读" }}</el-tag></template></el-table-column>
        <el-table-column prop="title" label="消息标题" min-width="190" show-overflow-tooltip />
        <el-table-column label="类型" width="110" align="center"><template #default="{ row }"><el-tag :type="typeTag(row.type) as any" size="small">{{ typeText(row.type) }}</el-tag></template></el-table-column>
        <el-table-column prop="content" label="内容摘要" min-width="260" show-overflow-tooltip />
        <el-table-column label="接收时间" width="175"><template #default="{ row }">{{ dateText(row.createdAt) }}</template></el-table-column>
        <el-table-column label="操作" width="190" fixed="right" align="center">
          <template #default="{ row }"><el-button link type="primary" @click="showDetail(row)">查看</el-button><el-button v-if="!row.isRead" link type="success" @click="markRead(row)">标记已读</el-button><el-button link type="danger" @click="remove(row)">删除</el-button></template>
        </el-table-column>
        <template #empty><el-empty description="暂无消息" /></template>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next, jumper" background @current-change="fetchList" @size-change="changeFilter" /></div>
    </el-card>

    <el-dialog v-model="detailVisible" title="消息详情" width="700px" destroy-on-close>
      <template v-if="activeMessage">
        <div class="detail-heading"><el-tag :type="typeTag(activeMessage.type) as any" size="small">{{ typeText(activeMessage.type) }}</el-tag><strong>{{ activeMessage.title }}</strong><el-tag :type="activeMessage.isRead ? 'info' : 'danger'" size="small">{{ activeMessage.isRead ? "已读" : "未读" }}</el-tag></div>
        <div class="detail-meta">接收时间：{{ dateText(activeMessage.createdAt) }}<span v-if="activeMessage.readAt">　阅读时间：{{ dateText(activeMessage.readAt) }}</span></div>
        <div class="detail-content">{{ activeMessage.content }}</div>
        <div v-if="activeMessage.link" class="detail-link">关联地址：{{ activeMessage.link }}</div>
      </template>
      <template #footer>
        <el-button v-if="activeMessage && !activeMessage.isRead" type="primary" @click="markRead(activeMessage)">标记已读</el-button>
        <el-button @click="detailVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.inbox-page {
  .toolbar { display: flex; justify-content: space-between; align-items: center; gap: 16px; margin-bottom: 12px; }
  .title-wrap, .toolbar-actions { display: flex; align-items: center; gap: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .unread-label { padding: 2px 8px; color: var(--el-color-danger); font-size: 12px; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .detail-heading { display: flex; align-items: center; gap: 10px; font-size: 18px; }
  .detail-meta { margin: 12px 0 18px; color: var(--el-text-color-secondary); font-size: 13px; }
  .detail-content { min-height: 100px; white-space: pre-wrap; line-height: 1.8; word-break: break-word; }
  .detail-link { margin-top: 18px; padding-top: 12px; border-top: 1px solid var(--el-border-color-lighter); color: var(--el-text-color-secondary); font-size: 13px; word-break: break-all; }
  :deep(.unread-row) { font-weight: 600; background: var(--el-color-primary-light-9); }
  :deep(.read-row) { color: var(--el-text-color-secondary); }
}
@media (max-width: 768px) {
  .inbox-page .toolbar { align-items: flex-start; flex-direction: column; }
  .inbox-page .toolbar-actions { flex-wrap: wrap; }
}
</style>
