<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { useLookupStore } from "@/store/modules/lookup";
import { planStatus } from "@/utils/biz";

const lookup = useLookupStore();
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const statusFilter = ref<number>();
const dialogVisible = ref(false);
const detailVisible = ref(false);
const saving = ref(false);
const formRef = ref();
const editId = ref<number | null>(null);
const detail = ref<any>(null);
const form = reactive<any>({
  name: "",
  planDate: "",
  startDate: "",
  endDate: "",
  workshopId: undefined,
  sourceType: 3,
  remark: "",
  lines: []
});

const products = computed(() => lookup.options("products"));
const workshops = computed(() => lookup.options("workshops"));
const sourceTypes = [
  { label: "销售订单", value: 1 },
  { label: "预测", value: 2 },
  { label: "手工", value: 3 }
];
const totalQty = computed(() => form.lines.reduce((sum: number, line: any) => sum + (Number(line.qty) || 0), 0));

function dateText(value: any) {
  return value ? String(value).slice(0, 10) : "-";
}
function normalizeDateTime(value: any) {
  if (!value) return "";
  const text = String(value).trim();
  const dateTime = /^(\d{4}-\d{2}-\d{2})[T\s](\d{2}):(\d{2})(?::(\d{2}))?/.exec(text);
  if (dateTime) return `${dateTime[1]}T${dateTime[2]}:${dateTime[3]}:${dateTime[4] ?? "00"}`;
  const dateOnly = /^(\d{4}-\d{2}-\d{2})$/.exec(text);
  return dateOnly ? `${dateOnly[1]}T00:00:00` : "";
}
function formatDateTime(value: any) {
  const normalized = normalizeDateTime(value);
  return normalized ? `${normalized.slice(0, 10)} ${normalized.slice(11, 16)}` : "-";
}
const FACTORY_TIME_ZONE = "Asia/Shanghai";
function factoryNow() {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: FACTORY_TIME_ZONE,
    year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", hourCycle: "h23"
  }).formatToParts(new Date());
  const number = (name: string) => Number(parts.find(p => p.type === name)?.value ?? 0);
  // Date.UTC 仅用于墙钟字段的运算；发送 API 时不附加 Z 或时区偏移。
  return new Date(Date.UTC(number("year"), number("month") - 1, number("day"), number("hour")));
}
function localDateString(value: Date) {
  const year = value.getUTCFullYear();
  const month = String(value.getUTCMonth() + 1).padStart(2, "0");
  const day = String(value.getUTCDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}
function localDateTimeString(value: Date) {
  const hours = String(value.getUTCHours()).padStart(2, "0");
  const minutes = String(value.getUTCMinutes()).padStart(2, "0");
  const seconds = String(value.getUTCSeconds()).padStart(2, "0");
  return `${localDateString(value)}T${hours}:${minutes}:${seconds}`;
}
function isExactHour(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})$/.exec(value);
  if (!match || match[5] !== "00" || match[6] !== "00") return false;
  const [, year, month, day, hour] = match.map(Number);
  const parsed = new Date(Date.UTC(year, month - 1, day, hour));
  return parsed.getUTCFullYear() === year && parsed.getUTCMonth() === month - 1 &&
    parsed.getUTCDate() === day && parsed.getUTCHours() === hour;
}
function statusInfo(value: any) {
  const entry = planStatus[Number(value)];
  return { label: entry?.label ?? "未知", type: (entry?.type ?? "info") as any };
}
function workshopName(id: number) {
  return lookup.name("workshops", id) || "-";
}

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/production-plan", {
      params: {
        page: page.value,
        pageSize: pageSize.value,
        keyword: keyword.value || undefined,
        planStatus: statusFilter.value
      }
    });
    if (res?.code === 0) {
      list.value = res.data?.list ?? [];
      total.value = res.data?.total ?? 0;
    } else message(res?.message || "计划列表加载失败", { type: "error" });
  } catch (error: any) {
    message(error?.message || "计划列表加载失败", { type: "error" });
  } finally {
    loading.value = false;
  }
}
function search() {
  page.value = 1;
  fetchList();
}
function resetSearch() {
  keyword.value = "";
  statusFilter.value = undefined;
  search();
}
function setForm(value: any) {
  Object.assign(form, {
    name: value?.name ?? "",
    planDate: dateText(value?.planDate) === "-" ? "" : dateText(value?.planDate),
    startDate: normalizeDateTime(value?.startDate),
    endDate: normalizeDateTime(value?.endDate),
    workshopId: value?.workshopId ?? undefined,
    sourceType: value?.sourceType ?? 3,
    remark: value?.remark ?? "",
    lines: (value?.lines ?? []).map((line: any) => ({
      productId: line.productId,
      qty: Number(line.qty) || 1,
      deliveryDate: normalizeDateTime(line.deliveryDate)
    }))
  });
}
function openCreate() {
  editId.value = null;
  const start = factoryNow();
  start.setUTCHours(start.getUTCHours() + 1);
  const end = new Date(start);
  end.setUTCHours(end.getUTCHours() + 8);
  setForm({
    planDate: localDateString(start),
    startDate: localDateTimeString(start),
    endDate: localDateTimeString(end),
    sourceType: 3,
    lines: [{ productId: undefined, qty: 1, deliveryDate: localDateTimeString(end) }]
  });
  dialogVisible.value = true;
}
async function openEdit(row: any) {
  const res: any = await http.request("get", `/production-plan/${row.id}`);
  if (res?.code !== 0) return message(res?.message || "计划明细加载失败", { type: "error" });
  editId.value = row.id;
  setForm(res.data);
  dialogVisible.value = true;
}
async function openDetail(row: any) {
  const res: any = await http.request("get", `/production-plan/${row.id}`);
  if (res?.code !== 0) return message(res?.message || "计划明细加载失败", { type: "error" });
  detail.value = res.data;
  detailVisible.value = true;
}
function addLine() {
  form.lines.push({ productId: undefined, qty: 1, deliveryDate: form.endDate || "" });
}
function removeLine(index: number) {
  form.lines.splice(index, 1);
}
async function save() {
  const valid = await formRef.value?.validate().catch(() => false);
  if (!valid) return;
  const startDate = normalizeDateTime(form.startDate);
  const endDate = normalizeDateTime(form.endDate);
  if (!startDate || !endDate) return message("请选择计划开始和结束时间", { type: "warning" });
  if (!isExactHour(startDate) || !isExactHour(endDate)) {
    return message("计划开始和结束时间必须精确到整点（分钟、秒须为 00）", { type: "warning" });
  }
  if (startDate > endDate) return message("计划开始时间不能晚于结束时间", { type: "warning" });
  if (!form.lines.length) return message("请至少添加一行计划明细", { type: "warning" });
  if (form.lines.some((line: any) => !line.productId || Number(line.qty) <= 0)) {
    return message("请完整填写产品和大于 0 的计划数量", { type: "warning" });
  }
  const invalidLine = form.lines.findIndex((line: any) => {
    if (!line.deliveryDate) return false;
    const delivery = normalizeDateTime(line.deliveryDate);
    return !isExactHour(delivery) || delivery < startDate;
  });
  if (invalidLine >= 0) {
    return message(`第 ${invalidLine + 1} 行交货时间须为整点，且不能早于计划开始时间`, { type: "warning" });
  }
  saving.value = true;
  try {
    const payload = {
      id: editId.value ?? 0,
      name: form.name,
      planDate: form.planDate,
      startDate,
      endDate,
      workshopId: form.workshopId ?? null,
      sourceType: form.sourceType,
      remark: form.remark,
      lines: form.lines.map((line: any) => ({
        productId: line.productId,
        qty: Number(line.qty),
        deliveryDate: line.deliveryDate ? normalizeDateTime(line.deliveryDate) : null
      }))
    };
    const res: any = editId.value
      ? await http.request("put", `/production-plan/${editId.value}`, { data: payload })
      : await http.request("post", "/production-plan", { data: payload });
    if (res?.code === 0) {
      message(editId.value ? "计划已更新" : "计划已创建", { type: "success" });
      dialogVisible.value = false;
      fetchList();
    } else message(res?.message || "计划保存失败", { type: "error" });
  } catch (error: any) {
    message(error?.message || "计划保存失败", { type: "error" });
  } finally {
    saving.value = false;
  }
}
async function release(row: any) {
  try {
    await ElMessageBox.confirm(`下达计划「${row.code}」并按明细生成工单？`, "确认下达", { type: "warning" });
  } catch {
    return;
  }
  const res: any = await http.request("post", `/production-plan/${row.id}/release`);
  if (res?.code === 0) message(res?.message || "计划已下达", { type: "success" });
  else message(res?.message || "计划下达失败", { type: "error" });
  fetchList();
}
async function closePlan(row: any) {
  try {
    await ElMessageBox.confirm(`确定关闭生产计划「${row.code}」吗？`, "确认关闭", { type: "warning" });
  } catch {
    return;
  }
  const res: any = await http.request("post", `/production-plan/${row.id}/close`);
  message(res?.code === 0 ? "计划已关闭" : res?.message || "关闭失败", { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}

onMounted(async () => {
  await lookup.load();
  fetchList();
});
</script>

<template>
  <div class="production-plan-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字">
          <el-input v-model="keyword" clearable placeholder="计划编号 / 名称" style="width: 210px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="statusFilter" clearable placeholder="全部状态" style="width: 150px">
            <el-option v-for="(item, key) in planStatus" :key="key" :label="item.label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
          <el-button @click="resetSearch">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div class="toolbar">
        <span class="title">生产计划</span>
        <el-button type="primary" @click="openCreate">新建计划</el-button>
      </div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="计划编号" width="170" fixed />
        <el-table-column prop="name" label="计划名称" min-width="150" show-overflow-tooltip />
        <el-table-column label="计划日期" width="120"><template #default="{ row }">{{ dateText(row.planDate) }}</template></el-table-column>
        <el-table-column label="计划周期" min-width="300">
          <template #default="{ row }">{{ formatDateTime(row.startDate) }} ~ {{ formatDateTime(row.endDate) }}</template>
        </el-table-column>
        <el-table-column label="车间" min-width="130"><template #default="{ row }">{{ workshopName(row.workshopId) }}</template></el-table-column>
        <el-table-column prop="totalQty" label="计划数量" width="110" align="right" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }"><el-tag :type="statusInfo(row.planStatus).type" size="small">{{ statusInfo(row.planStatus).label }}</el-tag></template>
        </el-table-column>
        <el-table-column label="操作" width="250" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openDetail(row)">明细</el-button>
            <el-button v-if="Number(row.planStatus) < 2" link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button v-if="Number(row.planStatus) < 2" link type="success" @click="release(row)">下达</el-button>
            <el-button v-if="Number(row.planStatus) !== 4" link type="danger" @click="closePlan(row)">关闭</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="pager">
        <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" />
      </div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editId ? '编辑生产计划' : '新建生产计划'" width="960px" destroy-on-close top="5vh">
      <el-form ref="formRef" :model="form" label-width="95px">
        <el-row :gutter="16">
          <el-col :span="8"><el-form-item label="计划名称" prop="name" required><el-input v-model="form.name" placeholder="请输入计划名称" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="计划日期" required><el-date-picker v-model="form.planDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="来源类型" required><el-select v-model="form.sourceType" style="width: 100%"><el-option v-for="item in sourceTypes" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="计划车间"><el-select v-model="form.workshopId" clearable filterable placeholder="可选" style="width: 100%"><el-option v-for="item in workshops" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="开始日期" required><el-date-picker v-model="form.startDate" type="datetime" format="YYYY-MM-DD HH:mm" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="结束日期" required><el-date-picker v-model="form.endDate" type="datetime" format="YYYY-MM-DD HH:mm" value-format="YYYY-MM-DDTHH:mm:ss" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item label="备注"><el-input v-model="form.remark" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
        <el-divider content-position="left">计划明细</el-divider>
        <el-table :data="form.lines" border size="small">
          <el-table-column type="index" label="#" width="55" />
          <el-table-column label="产品" min-width="240">
            <template #default="{ row }"><el-select v-model="row.productId" filterable placeholder="选择产品" style="width: 100%"><el-option v-for="item in products" :key="item.value" :label="`${lookup.code('products', item.value)} ${item.label}`" :value="item.value" /></el-select></template>
          </el-table-column>
          <el-table-column label="计划数量" width="180"><template #default="{ row }"><el-input-number v-model="row.qty" :min="0.0001" :precision="4" controls-position="right" style="width: 100%" /></template></el-table-column>
          <el-table-column label="交货时间" width="220"><template #default="{ row }"><el-date-picker v-model="row.deliveryDate" type="datetime" format="YYYY-MM-DD HH:mm" value-format="YYYY-MM-DDTHH:mm:ss" clearable style="width: 100%" /></template></el-table-column>
          <el-table-column label="操作" width="75" align="center"><template #default="{ $index }"><el-button link type="danger" @click="removeLine($index)">删除</el-button></template></el-table-column>
        </el-table>
        <div class="line-footer"><el-button size="small" @click="addLine">添加明细</el-button><span>计划总量：<b>{{ totalQty }}</b></span></div>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="save">保存计划</el-button></template>
    </el-dialog>

    <el-dialog v-model="detailVisible" title="生产计划明细" width="850px">
      <template v-if="detail">
        <el-descriptions :column="3" border>
          <el-descriptions-item label="计划编号">{{ detail.code }}</el-descriptions-item>
          <el-descriptions-item label="计划名称">{{ detail.name || '-' }}</el-descriptions-item>
          <el-descriptions-item label="状态"><el-tag :type="statusInfo(detail.planStatus).type" size="small">{{ statusInfo(detail.planStatus).label }}</el-tag></el-descriptions-item>
          <el-descriptions-item label="计划日期">{{ dateText(detail.planDate) }}</el-descriptions-item>
          <el-descriptions-item label="计划周期">{{ formatDateTime(detail.startDate) }} ~ {{ formatDateTime(detail.endDate) }}</el-descriptions-item>
          <el-descriptions-item label="车间">{{ workshopName(detail.workshopId) }}</el-descriptions-item>
          <el-descriptions-item label="备注" :span="3">{{ detail.remark || '-' }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="detail.lines || []" border stripe size="small" style="margin-top: 16px">
          <el-table-column type="index" label="#" width="55" />
          <el-table-column label="产品" min-width="220"><template #default="{ row }">{{ lookup.name('products', row.productId) }}</template></el-table-column>
          <el-table-column prop="qty" label="计划数量" width="120" align="right" />
          <el-table-column prop="completedQty" label="已完成" width="120" align="right" />
          <el-table-column label="交货时间" width="190"><template #default="{ row }">{{ formatDateTime(row.deliveryDate) }}</template></el-table-column>
        </el-table>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.production-plan-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .line-footer { display: flex; align-items: center; justify-content: space-between; margin-top: 12px; }
}
</style>
