<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { useLookupStore } from "@/store/modules/lookup";

const lookup = useLookupStore();
const loading = ref(false);
const saving = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const statusFilter = ref<number>();
const detailVisible = ref(false);
const detail = ref<any>(null);
const editorVisible = ref(false);
const editId = ref<number>();
const moveVisible = ref(false);
const moveOrder = ref<any>(null);
const moveKind = ref<"dispatch" | "receipt">("dispatch");
const moveForm = reactive<any>({ qty: 0, batchNo: "", location: "", remark: "" });
const form = reactive<any>({ name: "", supplierId: undefined, inputProductId: undefined, outputProductId: undefined, orderQty: 1, sourceWarehouseId: undefined, targetWarehouseId: undefined, orderDate: new Date().toISOString().slice(0, 10), dueDate: "", inputBatchNo: "", inputLocation: "", outputBatchNo: "", outputLocation: "", remark: "" });
const suppliers = computed(() => lookup.options("suppliers"));
const products = computed(() => lookup.options("products"));
const warehouses = computed(() => lookup.options("warehouses"));
const statusMap: Record<number, { label: string; type: string }> = {
  0: { label: "草稿", type: "info" }, 1: { label: "已发料", type: "primary" },
  2: { label: "收货中", type: "warning" }, 3: { label: "已完成", type: "success" }, 4: { label: "已关闭", type: "info" }
};
const stockType: Record<number, string> = { 5: "其他入库", 6: "其他出库" };
function nameOf(items: any[], id: number) { return items.find(x => Number(x.value) === Number(id))?.label || "-"; }
function statusOf(value: any) { return statusMap[Number(value)] || { label: "未知", type: "info" }; }
function dateText(v: any) { return v ? String(v).slice(0, 10) : "-"; }
function resetForm() {
  Object.assign(form, { name: "", supplierId: undefined, inputProductId: undefined, outputProductId: undefined, orderQty: 1,
    sourceWarehouseId: warehouses.value[0]?.value, targetWarehouseId: warehouses.value[1]?.value ?? warehouses.value[0]?.value,
    orderDate: new Date().toISOString().slice(0, 10), dueDate: "", inputBatchNo: "", inputLocation: "", outputBatchNo: "", outputLocation: "", remark: "" });
}
async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/subcontract", { params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined, orderStatus: statusFilter.value } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "委外订单加载失败", { type: "error" });
  } catch (e: any) { message(e?.message || "委外订单加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function openCreate() { editId.value = undefined; resetForm(); editorVisible.value = true; }
async function openEdit(row: any) {
  const res: any = await http.request("get", `/subcontract/${row.id}`);
  if (res?.code !== 0) return message(res?.message || "订单详情加载失败", { type: "error" });
  const x = res.data.order;
  editId.value = x.id;
  Object.assign(form, { name: x.name, supplierId: x.supplierId, inputProductId: x.inputProductId, outputProductId: x.outputProductId,
    orderQty: x.orderQty, sourceWarehouseId: x.sourceWarehouseId, targetWarehouseId: x.targetWarehouseId,
    orderDate: dateText(x.orderDate), dueDate: dateText(x.dueDate) === "-" ? "" : dateText(x.dueDate),
    inputBatchNo: x.inputBatchNo || "", inputLocation: x.inputLocation || "", outputBatchNo: x.outputBatchNo || "", outputLocation: x.outputLocation || "", remark: x.remark || "" });
  editorVisible.value = true;
}
async function saveOrder() {
  if (!form.supplierId || !form.inputProductId || !form.outputProductId || !form.sourceWarehouseId || !form.targetWarehouseId || Number(form.orderQty) <= 0)
    return message("请填写供应商、投入/产出物料、仓库和有效订单数量", { type: "warning" });
  saving.value = true;
  try {
    const payload = { ...form, dueDate: form.dueDate || null, orderDate: `${form.orderDate}T00:00:00` };
    const res: any = await http.request(editId.value ? "put" : "post", editId.value ? `/subcontract/${editId.value}` : "/subcontract", { data: payload });
    if (res?.code === 0) { message(res.message || "保存成功", { type: "success" }); editorVisible.value = false; fetchList(); }
    else message(res?.message || "保存失败", { type: "error" });
  } catch (e: any) { message(e?.message || "保存失败", { type: "error" }); }
  finally { saving.value = false; }
}
async function openDetail(row: any) {
  const res: any = await http.request("get", `/subcontract/${row.id}`);
  if (res?.code !== 0) return message(res?.message || "订单详情加载失败", { type: "error" });
  detail.value = res.data; detailVisible.value = true;
}
function openMove(row: any, kind: "dispatch" | "receipt") {
  moveOrder.value = row; moveKind.value = kind;
  moveForm.qty = kind === "dispatch" ? Number(row.orderQty) - Number(row.issuedQty) : Number(row.issuedQty) - Number(row.receivedQty);
  moveForm.batchNo = kind === "dispatch" ? row.inputBatchNo || "" : row.outputBatchNo || "";
  moveForm.location = kind === "dispatch" ? row.inputLocation || "" : row.outputLocation || "";
  moveForm.remark = ""; moveVisible.value = true;
}
async function submitMove() {
  if (!moveOrder.value || Number(moveForm.qty) <= 0) return message("请输入大于 0 的数量", { type: "warning" });
  saving.value = true;
  try {
    const res: any = await http.request("post", `/subcontract/${moveOrder.value.id}/${moveKind.value}`, { data: { qty: Number(moveForm.qty), batchNo: moveForm.batchNo || null, location: moveForm.location || null, remark: moveForm.remark || null } });
    if (res?.code === 0) { message(res.message || "过账成功", { type: "success" }); moveVisible.value = false; await fetchList(); }
    else message(res?.message || "过账失败", { type: "error" });
  } catch (e: any) { message(e?.message || "过账失败", { type: "error" }); }
  finally { saving.value = false; }
}
async function closeOrder(row: any) {
  try { await ElMessageBox.confirm(`关闭委外订单「${row.code}」？`, "确认关闭", { type: "warning" }); } catch { return; }
  const res: any = await http.request("post", `/subcontract/${row.id}/close`);
  message(res?.code === 0 ? (res.message || "已关闭") : (res?.message || "关闭失败"), { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}
onMounted(async () => { await lookup.load(); resetForm(); fetchList(); });
</script>

<template>
  <div class="subcontract-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="订单编号 / 名称" style="width: 210px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="状态"><el-select v-model="statusFilter" clearable placeholder="全部状态" style="width: 140px"><el-option v-for="(item, key) in statusMap" :key="key" :label="item.label" :value="Number(key)" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="keyword = ''; statusFilter = undefined; search()">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><div><strong>委外加工订单</strong><el-tag class="scope-tag" type="warning" size="small">收货不代表质检放行</el-tag></div><el-button type="primary" @click="openCreate">新建委外订单</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="订单编号" width="165" fixed />
        <el-table-column prop="name" label="订单名称" min-width="145" />
        <el-table-column label="供应商" min-width="145"><template #default="{ row }">{{ nameOf(suppliers, row.supplierId) }}</template></el-table-column>
        <el-table-column label="投入物料 → 加工产出" min-width="220"><template #default="{ row }">{{ nameOf(products, row.inputProductId) }} → {{ nameOf(products, row.outputProductId) }}</template></el-table-column>
        <el-table-column prop="orderQty" label="订单数" width="95" align="right" />
        <el-table-column prop="issuedQty" label="已发料" width="95" align="right" />
        <el-table-column prop="receivedQty" label="已收货" width="95" align="right" />
        <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag :type="statusOf(row.orderStatus).type as any" size="small">{{ statusOf(row.orderStatus).label }}</el-tag></template></el-table-column>
        <el-table-column label="交期" width="115"><template #default="{ row }">{{ dateText(row.dueDate) }}</template></el-table-column>
        <el-table-column label="操作" width="260" fixed="right"><template #default="{ row }">
          <el-button link type="primary" @click="openDetail(row)">详情</el-button>
          <el-button v-if="Number(row.orderStatus) === 0" link type="primary" @click="openEdit(row)">编辑</el-button>
          <el-button v-if="[0,1,2].includes(Number(row.orderStatus)) && Number(row.issuedQty) < Number(row.orderQty)" link type="warning" @click="openMove(row, 'dispatch')">发料</el-button>
          <el-button v-if="[1,2].includes(Number(row.orderStatus)) && Number(row.receivedQty) < Number(row.issuedQty)" link type="success" @click="openMove(row, 'receipt')">收货</el-button>
          <el-button v-if="[0,1,2].includes(Number(row.orderStatus))" link type="danger" @click="closeOrder(row)">关闭</el-button>
        </template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10,20,50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>

    <el-dialog v-model="editorVisible" :title="editId ? '编辑委外订单' : '新建委外订单'" width="760px" destroy-on-close>
      <el-alert title="MVP 按一项投入物料对应一项加工产出物料建单；收货仅更新库存和数量，不执行质量检验或放行。" type="info" :closable="false" show-icon class="hint" />
      <el-form label-width="115px" class="form-grid">
        <el-form-item label="订单名称"><el-input v-model="form.name" placeholder="可选" /></el-form-item>
        <el-form-item label="供应商" required><el-select v-model="form.supplierId" filterable><el-option v-for="x in suppliers" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="投入物料" required><el-select v-model="form.inputProductId" filterable><el-option v-for="x in products" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="加工产出物料" required><el-select v-model="form.outputProductId" filterable><el-option v-for="x in products" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="订单数量" required><el-input-number v-model="form.orderQty" :min="0.0001" :precision="4" controls-position="right" /></el-form-item>
        <el-form-item label="订单日期"><el-date-picker v-model="form.orderDate" type="date" value-format="YYYY-MM-DD" /></el-form-item>
        <el-form-item label="交期"><el-date-picker v-model="form.dueDate" type="date" value-format="YYYY-MM-DD" clearable /></el-form-item>
        <el-form-item label="发料仓" required><el-select v-model="form.sourceWarehouseId" filterable><el-option v-for="x in warehouses" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="收货仓" required><el-select v-model="form.targetWarehouseId" filterable><el-option v-for="x in warehouses" :key="x.value" :label="x.label" :value="x.value" /></el-select></el-form-item>
        <el-form-item label="投入批次"><el-input v-model="form.inputBatchNo" /></el-form-item><el-form-item label="投入库位"><el-input v-model="form.inputLocation" /></el-form-item>
        <el-form-item label="产出批次"><el-input v-model="form.outputBatchNo" /></el-form-item><el-form-item label="产出库位"><el-input v-model="form.outputLocation" /></el-form-item>
        <el-form-item label="备注" class="wide"><el-input v-model="form.remark" type="textarea" :rows="2" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="editorVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="saveOrder">保存草稿</el-button></template>
    </el-dialog>

    <el-dialog v-model="moveVisible" :title="moveKind === 'dispatch' ? '委外发料过账' : '委外加工收货过账'" width="520px">
      <el-alert :title="moveKind === 'dispatch' ? '将投入物料按其他出库（6）扣减库存。' : '将加工产出物料按其他入库（5）增加库存；这不代表质检已完成或放行。'" type="warning" :closable="false" show-icon class="hint" />
      <el-form label-width="90px"><el-form-item label="本次数量" required><el-input-number v-model="moveForm.qty" :min="0.0001" :max="moveKind === 'dispatch' ? Number(moveOrder?.orderQty) - Number(moveOrder?.issuedQty) : Number(moveOrder?.issuedQty) - Number(moveOrder?.receivedQty)" :precision="4" controls-position="right" /></el-form-item>
        <el-form-item label="批次"><el-input v-model="moveForm.batchNo" /></el-form-item><el-form-item label="库位"><el-input v-model="moveForm.location" /></el-form-item><el-form-item label="备注"><el-input v-model="moveForm.remark" type="textarea" /></el-form-item></el-form>
      <template #footer><el-button @click="moveVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="submitMove">确认并过账</el-button></template>
    </el-dialog>

    <el-drawer v-model="detailVisible" title="委外订单详情" size="62%"><template v-if="detail">
      <el-alert title="委外收货仅代表库存收货，本模块未实现质检判定或质量放行。" type="warning" :closable="false" show-icon class="hint" />
      <el-descriptions :column="3" border><el-descriptions-item label="订单编号">{{ detail.order.code }}</el-descriptions-item><el-descriptions-item label="供应商">{{ nameOf(suppliers, detail.order.supplierId) }}</el-descriptions-item><el-descriptions-item label="状态">{{ statusOf(detail.order.orderStatus).label }}</el-descriptions-item>
        <el-descriptions-item label="投入物料">{{ nameOf(products, detail.order.inputProductId) }}</el-descriptions-item><el-descriptions-item label="加工产出物料">{{ nameOf(products, detail.order.outputProductId) }}</el-descriptions-item><el-descriptions-item label="订单数量">{{ detail.order.orderQty }}</el-descriptions-item>
        <el-descriptions-item label="已发料">{{ detail.order.issuedQty }}</el-descriptions-item><el-descriptions-item label="已收货">{{ detail.order.receivedQty }}</el-descriptions-item><el-descriptions-item label="发料仓 / 收货仓">{{ nameOf(warehouses, detail.order.sourceWarehouseId) }} / {{ nameOf(warehouses, detail.order.targetWarehouseId) }}</el-descriptions-item>
        <el-descriptions-item label="备注" :span="3">{{ detail.order.remark || '-' }}</el-descriptions-item></el-descriptions>
      <el-divider content-position="left">关联库存单据</el-divider><el-table :data="detail.bills" border stripe><el-table-column prop="code" label="单据编号" /><el-table-column label="类型"><template #default="{ row }">{{ stockType[row.billType] || row.billType }}</template></el-table-column><el-table-column prop="totalQty" label="数量" /><el-table-column label="日期"><template #default="{ row }">{{ dateText(row.billDate) }}</template></el-table-column><el-table-column label="单据状态"><template #default="{ row }">{{ Number(row.billStatus) === 1 ? '已过账' : '草稿' }}</template></el-table-column></el-table>
    </template></el-drawer>
  </div>
</template>

<style scoped lang="scss">
.subcontract-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
  .scope-tag { margin-left: 10px; vertical-align: middle; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .hint { margin-bottom: 16px; }
  .form-grid { display: grid; grid-template-columns: 1fr 1fr; column-gap: 16px; }
  .form-grid :deep(.el-form-item) { min-width: 0; }
  .form-grid :deep(.el-select), .form-grid :deep(.el-date-editor), .form-grid :deep(.el-input-number) { width: 100%; }
  .wide { grid-column: 1 / -1; }
}
</style>
