<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { billStatusDict, billTypeDict } from "@/utils/biz";

type OptionItem = { id: number; code?: string; name: string; extra?: string };
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const filterBillType = ref<number | undefined>();
const filterWarehouseId = ref<number | undefined>();
const filterStatus = ref<number | undefined>();
const products = ref<OptionItem[]>([]);
const warehouses = ref<OptionItem[]>([]);
const billTypes = Object.entries(billTypeDict).map(([value, item]) => ({ value: Number(value), ...item }));
const billStatuses = Object.entries(billStatusDict).map(([value, item]) => ({ value: Number(value), ...item }));
const dialogVisible = ref(false);
const detailVisible = ref(false);
const saving = ref(false);
const detail = ref<any>(null);
const form = reactive<any>({ billType: 5, warehouseId: undefined, billDate: "", remark: "", lines: [] as any[] });

function today() { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; }
function money(value: any) { return Number(value ?? 0).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
function quantity(value: any) { return Number(value ?? 0).toLocaleString("zh-CN", { maximumFractionDigits: 3 }); }
function typeInfo(value: number) { return billTypeDict[value] ?? { label: `类型${value}`, type: "info" }; }
function statusInfo(value: number) { return billStatusDict[value] ?? { label: `状态${value}`, type: "info" }; }
function optionName(options: OptionItem[], id?: number) { return options.find(x => x.id === id)?.name ?? "-"; }
function productLabel(product: OptionItem) { return `${product.code ?? ""} ${product.name}${product.extra ? `（${product.extra}）` : ""}`.trim(); }
function errorText(error: any, fallback = "操作失败") { return error?.response?.data?.message || error?.response?.data?.Message || error?.message || fallback; }
const totalAmount = () => form.lines.reduce((sum: number, line: any) => sum + (Number(line.qty) || 0) * (Number(line.price) || 0), 0);

async function loadOptions() {
  try {
    const [productRes, warehouseRes]: any[] = await Promise.all([
      http.request("get", "/product/options"), http.request("get", "/warehouse/options")
    ]);
    if (productRes?.code === 0) products.value = productRes.data ?? [];
    if (warehouseRes?.code === 0) warehouses.value = warehouseRes.data ?? [];
  } catch (error: any) { message(errorText(error, "产品或仓库选项加载失败"), { type: "error" }); }
}
async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/stock-bill", { params: {
      page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined,
      billType: filterBillType.value, warehouseId: filterWarehouseId.value, billStatus: filterStatus.value
    } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "出入库单加载失败", { type: "error" });
  } catch (error: any) { message(errorText(error, "出入库单加载失败"), { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; filterBillType.value = undefined; filterWarehouseId.value = undefined; filterStatus.value = undefined; search(); }
function openCreate(type: number) {
  Object.assign(form, { billType: type, warehouseId: undefined, billDate: today(), remark: "", lines: [{ productId: undefined, qty: 1, price: 0, batchNo: "", location: "" }] });
  dialogVisible.value = true;
}
function addLine() { form.lines.push({ productId: undefined, qty: 1, price: 0, batchNo: "", location: "" }); }
function removeLine(index: number) { form.lines.splice(index, 1); }
async function saveBill() {
  if (!form.warehouseId) return message("请选择仓库", { type: "warning" });
  if (!form.lines.length || form.lines.some((line: any) => !line.productId || Number(line.qty) <= 0)) return message("请添加有效的产品和数量明细", { type: "warning" });
  saving.value = true;
  try {
    const res: any = await http.request("post", "/stock-bill", { data: {
      billType: form.billType, warehouseId: form.warehouseId, billDate: form.billDate,
      name: form.billType === 5 ? "其他入库单" : "其他出库单", remark: form.remark || null,
      lines: form.lines.map((line: any, index: number) => ({
        seq: index + 1, productId: line.productId, qty: Number(line.qty), price: Number(line.price) || 0,
        batchNo: line.batchNo || null, location: line.location || null
      }))
    } });
    if (res?.code === 0) { message("单据创建成功", { type: "success" }); dialogVisible.value = false; fetchList(); }
    else message(res?.message || "创建失败", { type: "error" });
  } catch (error: any) { message(errorText(error, "创建失败"), { type: "error" }); }
  finally { saving.value = false; }
}
async function openDetail(row: any) {
  try {
    const res: any = await http.request("get", `/stock-bill/${row.id}`);
    if (res?.code === 0) { detail.value = res.data; detailVisible.value = true; }
    else message(res?.message || "单据详情加载失败", { type: "error" });
  } catch (error: any) { message(errorText(error, "单据详情加载失败"), { type: "error" }); }
}
async function audit(row: any) {
  try { await ElMessageBox.confirm(`确认审核单据「${row.code || row.name || row.id}」？审核后将更新库存。`, "审核确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("post", `/stock-bill/${row.id}/audit`);
    if (res?.code === 0) message("审核成功，库存已更新", { type: "success" });
    else message(res?.message || "审核失败", { type: "error" });
    fetchList();
  } catch (error: any) { message(errorText(error, "审核失败"), { type: "error" }); }
}
async function voidBill(row: any) {
  try { await ElMessageBox.confirm(`确定作废单据「${row.code || row.name || row.id}」？`, "作废确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("post", `/stock-bill/${row.id}/void`);
    if (res?.code === 0) message("单据已作废", { type: "success" });
    else message(res?.message || "作废失败", { type: "error" });
    fetchList();
  } catch (error: any) { message(errorText(error, "作废失败"), { type: "error" }); }
}
onMounted(() => { loadOptions(); fetchList(); });
</script>

<template>
  <div class="bill-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="单据号 / 名称" style="width: 200px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="单据类型"><el-select v-model="filterBillType" clearable placeholder="全部类型" style="width: 150px"><el-option v-for="t in billTypes" :key="t.value" :label="t.label" :value="t.value" /></el-select></el-form-item>
        <el-form-item label="仓库"><el-select v-model="filterWarehouseId" clearable filterable placeholder="全部仓库" style="width: 160px"><el-option v-for="w in warehouses" :key="w.id" :label="w.name" :value="w.id" /></el-select></el-form-item>
        <el-form-item label="状态"><el-select v-model="filterStatus" clearable placeholder="全部状态" style="width: 130px"><el-option v-for="s in billStatuses" :key="s.value" :label="s.label" :value="s.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar">
        <span class="title">出入库单</span>
        <div class="toolbar-actions"><el-button type="success" plain @click="openCreate(5)">新建其他入库</el-button><el-button type="warning" plain @click="openCreate(6)">新建其他出库</el-button></div>
      </div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="单据编号" width="170" />
        <el-table-column label="业务类型" width="130"><template #default="{ row }"><el-tag :type="typeInfo(row.billType).type as any" size="small">{{ typeInfo(row.billType).label }}</el-tag></template></el-table-column>
        <el-table-column prop="billDate" label="单据日期" width="115"><template #default="{ row }">{{ row.billDate ? String(row.billDate).slice(0, 10) : "-" }}</template></el-table-column>
        <el-table-column label="出库仓库" min-width="130"><template #default="{ row }">{{ optionName(warehouses, row.warehouseId) }}</template></el-table-column>
        <el-table-column label="目标仓库" min-width="130"><template #default="{ row }">{{ row.toWarehouseId ? optionName(warehouses, row.toWarehouseId) : "-" }}</template></el-table-column>
        <el-table-column prop="totalQty" label="总数量" width="100" align="right"><template #default="{ row }">{{ quantity(row.totalQty) }}</template></el-table-column>
        <el-table-column prop="totalAmount" label="总金额" width="130" align="right"><template #default="{ row }">¥{{ money(row.totalAmount) }}</template></el-table-column>
        <el-table-column label="状态" width="100"><template #default="{ row }"><el-tag :type="statusInfo(row.billStatus).type as any" size="small">{{ statusInfo(row.billStatus).label }}</el-tag></template></el-table-column>
        <el-table-column label="操作" width="190" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="openDetail(row)">明细</el-button><el-button v-if="row.billStatus === 0" link type="success" @click="audit(row)">审核</el-button><el-button v-if="row.billStatus === 0" link type="danger" @click="voidBill(row)">作废</el-button></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="form.billType === 5 ? '新建其他入库' : '新建其他出库'" width="920px" destroy-on-close>
      <el-form label-width="90px">
        <el-row :gutter="16"><el-col :span="12"><el-form-item label="仓库" required><el-select v-model="form.warehouseId" filterable placeholder="请选择仓库" style="width: 100%"><el-option v-for="w in warehouses" :key="w.id" :label="w.name" :value="w.id" /></el-select></el-form-item></el-col><el-col :span="12"><el-form-item label="单据日期"><el-date-picker v-model="form.billDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item></el-col><el-col :span="24"><el-form-item label="备注"><el-input v-model="form.remark" type="textarea" :rows="2" /></el-form-item></el-col></el-row>
        <el-table :data="form.lines" border size="small">
          <el-table-column label="产品" min-width="240"><template #default="{ row }"><el-select v-model="row.productId" filterable placeholder="选择产品" style="width: 100%"><el-option v-for="p in products" :key="p.id" :label="productLabel(p)" :value="p.id" /></el-select></template></el-table-column>
          <el-table-column label="数量" width="130"><template #default="{ row }"><el-input-number v-model="row.qty" :min="0.001" :precision="3" controls-position="right" style="width: 100%" /></template></el-table-column>
          <el-table-column label="单价" width="140"><template #default="{ row }"><el-input-number v-model="row.price" :min="0" :precision="2" controls-position="right" style="width: 100%" /></template></el-table-column>
          <el-table-column label="批次" width="140"><template #default="{ row }"><el-input v-model="row.batchNo" /></template></el-table-column>
          <el-table-column label="库位" width="130"><template #default="{ row }"><el-input v-model="row.location" /></template></el-table-column>
          <el-table-column width="64" align="center"><template #default="{ $index }"><el-button link type="danger" @click="removeLine($index)">删除</el-button></template></el-table-column>
        </el-table>
        <div class="line-footer"><el-button size="small" @click="addLine">添加明细</el-button><span>合计数量：{{ quantity(form.lines.reduce((sum: number, line: any) => sum + (Number(line.qty) || 0), 0)) }}　金额：¥{{ money(totalAmount()) }}</span></div>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="saveBill">创建单据</el-button></template>
    </el-dialog>

    <el-dialog v-model="detailVisible" title="出入库单明细" width="850px">
      <template v-if="detail">
        <el-descriptions :column="3" border>
          <el-descriptions-item label="单据编号">{{ detail.code || "-" }}</el-descriptions-item><el-descriptions-item label="业务类型"><el-tag :type="typeInfo(detail.billType).type as any" size="small">{{ typeInfo(detail.billType).label }}</el-tag></el-descriptions-item><el-descriptions-item label="状态">{{ statusInfo(detail.billStatus).label }}</el-descriptions-item>
          <el-descriptions-item label="仓库">{{ optionName(warehouses, detail.warehouseId) }}</el-descriptions-item><el-descriptions-item label="目标仓库">{{ detail.toWarehouseId ? optionName(warehouses, detail.toWarehouseId) : "-" }}</el-descriptions-item><el-descriptions-item label="日期">{{ detail.billDate ? String(detail.billDate).slice(0, 10) : "-" }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="detail.lines ?? []" border stripe size="small" style="margin-top: 14px"><el-table-column label="产品" min-width="220"><template #default="{ row }">{{ optionName(products, row.productId) }}</template></el-table-column><el-table-column prop="qty" label="数量" width="100" align="right"><template #default="{ row }">{{ quantity(row.qty) }}</template></el-table-column><el-table-column prop="price" label="单价" width="120" align="right"><template #default="{ row }">¥{{ money(row.price) }}</template></el-table-column><el-table-column prop="amount" label="金额" width="130" align="right"><template #default="{ row }">¥{{ money(row.amount) }}</template></el-table-column><el-table-column prop="batchNo" label="批次" width="130" /><el-table-column prop="location" label="库位" width="120" /></el-table>
        <div class="remark">备注：{{ detail.remark || "-" }}</div>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.bill-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .toolbar-actions { display: flex; gap: 8px; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .line-footer { display: flex; justify-content: space-between; align-items: center; margin-top: 12px; color: var(--el-text-color-secondary); }
  .remark { margin-top: 14px; color: var(--el-text-color-secondary); }
}
</style>
