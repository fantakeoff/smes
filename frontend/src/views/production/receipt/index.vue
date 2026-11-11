<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { useLookupStore } from "@/store/modules/lookup";
import { billStatusDict, billTypeDict } from "@/utils/biz";

const lookup = useLookupStore();
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const statusFilter = ref<number>();
const warehouseFilter = ref<number>();
const detailVisible = ref(false);
const detail = ref<any>(null);
const createVisible = ref(false);
const workOrders = ref<any[]>([]);
const selectedOrderId = ref<number>();
const selectedOrderDetail = ref<any>(null);
const warehouses = computed(() => lookup.options("warehouses"));
const form = reactive<any>({ warehouseId: undefined, qty: 1, batchNo: "", location: "", remark: "" });
const submitting = ref(false);

function dateText(value: any) { return value ? String(value).slice(0, 10) : "-"; }
function billStatus(value: any) {
  const item = billStatusDict[Number(value)];
  return { label: item?.label ?? "未知", type: (item?.type ?? "info") as any };
}
function warehouseName(id: number) { return lookup.name("warehouses", id) || "-"; }
function orderLabel(order: any) { return `${order.code || ''} · ${order.name || lookup.name('products', order.productId) || '生产工单'}`; }
function remainingQty(order: any) { return Math.max(0, Number(order?.completedQty || 0) - Number(order?.receivedQty || 0)); }

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/stock-bill", { params: {
      page: page.value, pageSize: pageSize.value, billType: 3,
      keyword: keyword.value || undefined, billStatus: statusFilter.value, warehouseId: warehouseFilter.value
    } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "生产入库单列表加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "生产入库单列表加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function resetSearch() { keyword.value = ""; statusFilter.value = undefined; warehouseFilter.value = undefined; search(); }
async function openDetail(row: any) {
  const res: any = await http.request("get", `/stock-bill/${row.id}`);
  if (res?.code !== 0) return message(res?.message || "入库单详情加载失败", { type: "error" });
  detail.value = res.data;
  detailVisible.value = true;
}
async function audit(row: any) {
  try { await ElMessageBox.confirm(`审核生产入库单「${row.code}」并更新库存？`, "确认审核", { type: "warning" }); }
  catch { return; }
  const res: any = await http.request("post", `/stock-bill/${row.id}/audit`);
  message(res?.code === 0 ? (res?.message || "审核成功") : (res?.message || "审核失败"), { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}
async function loadWorkOrders() {
  const res: any = await http.request("get", "/work-order", { params: { page: 1, pageSize: 500 } });
  if (res?.code !== 0) return message(res?.message || "工单列表加载失败", { type: "error" });
  workOrders.value = (res.data?.list ?? []).filter((row: any) =>
    [1, 2, 3].includes(Number(row.workOrderStatus)) && remainingQty(row) > 0
  );
}
async function openCreate() {
  await loadWorkOrders();
  selectedOrderId.value = undefined;
  selectedOrderDetail.value = null;
  form.warehouseId = lookup.list("warehouses")[0]?.id;
  form.qty = 1;
  form.batchNo = "";
  form.location = "";
  form.remark = "";
  createVisible.value = true;
}
async function onOrderChange(id: number) {
  selectedOrderDetail.value = null;
  if (!id) return;
  const res: any = await http.request("get", `/work-order/${id}`);
  if (res?.code === 0) {
    selectedOrderDetail.value = res.data;
    form.qty = Math.max(0, remainingQty(res.data.workOrder)) || 1;
  } else message(res?.message || "工单详情加载失败", { type: "error" });
}
async function createFromWorkOrder() {
  if (!selectedOrderId.value) return message("请选择生产工单", { type: "warning" });
  if (!form.warehouseId) return message("请选择入库仓库", { type: "warning" });
  if (Number(form.qty) <= 0) return message("入库数量必须大于 0", { type: "warning" });
  submitting.value = true;
  try {
    const res: any = await http.request("post", `/work-order/${selectedOrderId.value}/receipt`, { data: {
      warehouseId: form.warehouseId,
      qty: Number(form.qty),
      batchNo: form.batchNo || null,
      location: form.location || null,
      remark: form.remark || null
    } });
    if (res?.code === 0) {
      message(res?.message || "生产入库单已生成并完成库存过账", { type: "success" });
      createVisible.value = false;
      fetchList();
    } else message(res?.message || "入库单生成失败", { type: "error" });
  } catch (error: any) { message(error?.message || "入库单生成失败", { type: "error" }); }
  finally { submitting.value = false; }
}

onMounted(async () => { await lookup.load(); fetchList(); });
</script>

<template>
  <div class="production-receipt-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="单据编号 / 来源工单" style="width: 210px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="仓库"><el-select v-model="warehouseFilter" clearable filterable placeholder="全部仓库" style="width: 170px"><el-option v-for="item in warehouses" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item>
        <el-form-item label="状态"><el-select v-model="statusFilter" clearable placeholder="全部状态" style="width: 145px"><el-option v-for="(item, key) in billStatusDict" :key="key" :label="item.label" :value="Number(key)" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="resetSearch">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">生产入库单</span><el-button type="primary" @click="openCreate">从工单生成入库单</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="单据编号" width="170" fixed />
        <el-table-column label="类型" width="110"><template #default="{ row }"><el-tag :type="billTypeDict[row.billType]?.type" size="small">{{ billTypeDict[row.billType]?.label || '生产入库' }}</el-tag></template></el-table-column>
        <el-table-column label="单据日期" width="120"><template #default="{ row }">{{ dateText(row.billDate) }}</template></el-table-column>
        <el-table-column label="仓库" min-width="140"><template #default="{ row }">{{ warehouseName(row.warehouseId) }}</template></el-table-column>
        <el-table-column prop="sourceCode" label="来源工单" min-width="150" />
        <el-table-column prop="totalQty" label="入库数量" width="110" align="right" />
        <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag :type="billStatus(row.billStatus).type" size="small">{{ billStatus(row.billStatus).label }}</el-tag></template></el-table-column>
        <el-table-column label="操作" width="150" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="openDetail(row)">明细</el-button><el-button v-if="Number(row.billStatus) === 0" link type="success" @click="audit(row)">审核</el-button></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>

    <el-dialog v-model="createVisible" title="从生产工单生成入库单" width="700px" destroy-on-close>
      <el-alert title="选择工单和入库数量后生成入库单，并执行库存过账。" type="info" :closable="false" show-icon class="hint" />
      <el-form label-width="105px">
        <el-form-item label="生产工单" required><el-select v-model="selectedOrderId" filterable placeholder="选择已下达或生产中的工单" style="width: 100%" @change="onOrderChange"><el-option v-for="row in workOrders" :key="row.id" :label="orderLabel(row)" :value="row.id" /></el-select></el-form-item>
        <template v-if="selectedOrderDetail?.workOrder">
          <el-form-item label="成品"><span>{{ lookup.name('products', selectedOrderDetail.workOrder.productId) }}</span></el-form-item>
          <el-form-item label="可入库合格数"><span>{{ remainingQty(selectedOrderDetail.workOrder) }}</span></el-form-item>
        </template>
        <el-form-item label="入库仓库" required><el-select v-model="form.warehouseId" filterable placeholder="选择仓库" style="width: 100%"><el-option v-for="item in warehouses" :key="item.value" :label="item.label" :value="item.value" /></el-select></el-form-item>
        <el-form-item label="入库数量" required><el-input-number v-model="form.qty" :min="0.0001" :precision="4" controls-position="right" style="width: 100%" /></el-form-item>
        <el-form-item label="成品批次"><el-input v-model="form.batchNo" placeholder="批次管控产品必须填写" /></el-form-item>
        <el-form-item label="库位"><el-input v-model="form.location" placeholder="可选" /></el-form-item>
        <el-form-item label="备注"><el-input v-model="form.remark" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="createVisible = false">取消</el-button><el-button type="primary" :loading="submitting" @click="createFromWorkOrder">生成并过账</el-button></template>
    </el-dialog>

    <el-drawer v-model="detailVisible" title="生产入库单详情" size="60%">
      <template v-if="detail">
        <el-descriptions :column="3" border>
          <el-descriptions-item label="单据编号">{{ detail.code }}</el-descriptions-item>
          <el-descriptions-item label="单据类型">{{ billTypeDict[detail.billType]?.label || '生产入库' }}</el-descriptions-item>
          <el-descriptions-item label="状态"><el-tag :type="billStatus(detail.billStatus).type" size="small">{{ billStatus(detail.billStatus).label }}</el-tag></el-descriptions-item>
          <el-descriptions-item label="单据日期">{{ dateText(detail.billDate) }}</el-descriptions-item>
          <el-descriptions-item label="仓库">{{ warehouseName(detail.warehouseId) }}</el-descriptions-item>
          <el-descriptions-item label="来源工单">{{ detail.sourceCode || '-' }}</el-descriptions-item>
          <el-descriptions-item label="备注" :span="3">{{ detail.remark || '-' }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="detail.lines || []" border stripe style="margin-top: 16px">
          <el-table-column prop="seq" label="序号" width="75" />
          <el-table-column label="产品" min-width="220"><template #default="{ row }">{{ lookup.name('products', row.productId) }}</template></el-table-column>
          <el-table-column prop="qty" label="入库数量" width="120" align="right" />
          <el-table-column prop="price" label="成本单价" width="120" align="right" />
          <el-table-column prop="amount" label="金额" width="130" align="right" />
          <el-table-column prop="batchNo" label="批次" min-width="120" />
        </el-table>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped lang="scss">
.production-receipt-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .hint { margin-bottom: 18px; }
}
</style>
