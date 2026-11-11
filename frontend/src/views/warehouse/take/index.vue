<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { takeStatusDict } from "@/utils/biz";

type OptionItem = { id: number; code?: string; name: string; extra?: string };
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const warehouses = ref<OptionItem[]>([]);
const products = ref<OptionItem[]>([]);
const createVisible = ref(false);
const detailVisible = ref(false);
const creating = ref(false);
const saving = ref(false);
const currentTake = ref<any>(null);
const createForm = reactive<any>({ warehouseId: undefined, name: "", remark: "" });
const statuses = Object.entries(takeStatusDict).reduce((map: Record<number, any>, [key, item]) => { map[Number(key)] = item; return map; }, {});
function quantity(value: any) { return Number(value ?? 0).toLocaleString("zh-CN", { maximumFractionDigits: 3 }); }
function optionName(options: OptionItem[], id?: number) { return options.find(x => x.id === id)?.name ?? "-"; }
function productLabel(product: OptionItem) { return `${product.code ?? ""} ${product.name}${product.extra ? `（${product.extra}）` : ""}`.trim(); }
function statusInfo(value: number) { return statuses[value] ?? { label: `状态${value}`, type: "info" }; }
function diffOf(line: any) { return Number(line.actualQty ?? 0) - Number(line.bookQty ?? 0); }
function errorText(error: any, fallback = "操作失败") { return error?.response?.data?.message || error?.response?.data?.Message || error?.message || fallback; }
async function loadOptions() {
  try {
    const [warehouseRes, productRes]: any[] = await Promise.all([http.request("get", "/warehouse/options"), http.request("get", "/product/options")]);
    if (warehouseRes?.code === 0) warehouses.value = warehouseRes.data ?? [];
    if (productRes?.code === 0) products.value = productRes.data ?? [];
  } catch (error: any) { message(errorText(error, "产品或仓库选项加载失败"), { type: "error" }); }
}
async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/stock-take", { params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined } });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "盘点单加载失败", { type: "error" });
  } catch (error: any) { message(errorText(error, "盘点单加载失败"), { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; search(); }
function openCreate() { Object.assign(createForm, { warehouseId: undefined, name: "", remark: "" }); createVisible.value = true; }
async function createTake() {
  if (!createForm.warehouseId) return message("请选择盘点仓库", { type: "warning" });
  creating.value = true;
  try {
    const res: any = await http.request("post", "/stock-take", { data: { warehouseId: createForm.warehouseId, name: createForm.name || "库存盘点", remark: createForm.remark || null } });
    if (res?.code === 0) {
      message("盘点单已生成", { type: "success" });
      createVisible.value = false;
      await fetchList();
      if (res.data?.id) await openTake({ id: res.data.id });
    } else message(res?.message || "生成盘点单失败", { type: "error" });
  } catch (error: any) { message(errorText(error, "生成盘点单失败"), { type: "error" }); }
  finally { creating.value = false; }
}
async function openTake(row: any) {
  try {
    const res: any = await http.request("get", `/stock-take/${row.id}`);
    if (res?.code === 0) {
      currentTake.value = { ...res.data, lines: (res.data?.lines ?? []).map((line: any) => ({ ...line, actualQty: Number(line.actualQty ?? 0) })) };
      detailVisible.value = true;
    } else message(res?.message || "盘点明细加载失败", { type: "error" });
  } catch (error: any) { message(errorText(error, "盘点明细加载失败"), { type: "error" }); }
}
async function saveLines(showMessage = true) {
  if (!currentTake.value) return false;
  try {
    const res: any = await http.request("put", `/stock-take/${currentTake.value.id}/lines`, { data: currentTake.value.lines.map((line: any) => ({ id: line.id, actualQty: Number(line.actualQty) || 0 })) });
    if (res?.code === 0) {
      if (showMessage) message("实盘数量已保存", { type: "success" });
      await openTake({ id: currentTake.value.id });
      return true;
    }
    message(res?.message || "保存实盘数量失败", { type: "error" });
    return false;
  } catch (error: any) { message(errorText(error, "保存实盘数量失败"), { type: "error" }); return false; }
}
async function completeTake() {
  if (!currentTake.value) return;
  try { await ElMessageBox.confirm("完成盘点后将按差异调整库存，是否继续？", "完成盘点", { type: "warning" }); }
  catch { return; }
  saving.value = true;
  try {
    const saved = await saveLines(false);
    if (!saved) return;
    const res: any = await http.request("post", `/stock-take/${currentTake.value.id}/complete`);
    if (res?.code === 0) {
      message("盘点完成，库存已调整", { type: "success" });
      await fetchList();
      await openTake({ id: currentTake.value.id });
    } else message(res?.message || "完成盘点失败", { type: "error" });
  } catch (error: any) { message(errorText(error, "完成盘点失败"), { type: "error" }); }
  finally { saving.value = false; }
}
async function removeTake(row: any) {
  try { await ElMessageBox.confirm(`删除未完成盘点单「${row.code || row.name || row.id}」？`, "删除确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("delete", `/stock-take/${row.id}`);
    if (res?.code === 0) { message("盘点单已删除", { type: "success" }); fetchList(); }
    else message(res?.message || "删除失败", { type: "error" });
  } catch (error: any) { message(errorText(error, "删除失败"), { type: "error" }); }
}
onMounted(() => { loadOptions(); fetchList(); });
</script>

<template>
  <div class="take-page">
    <el-card shadow="never" class="search-card"><el-form inline @submit.prevent><el-form-item label="盘点单号"><el-input v-model="keyword" clearable placeholder="输入盘点单号" style="width: 220px" @keyup.enter="search" /></el-form-item><el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item></el-form></el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">库存盘点</span><el-button type="primary" @click="openCreate">按仓库生成盘点单</el-button></div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="盘点单号" width="170" />
        <el-table-column prop="name" label="盘点名称" min-width="150" show-overflow-tooltip />
        <el-table-column label="仓库" min-width="140"><template #default="{ row }">{{ optionName(warehouses, row.warehouseId) }}</template></el-table-column>
        <el-table-column prop="takeDate" label="盘点日期" width="120"><template #default="{ row }">{{ row.takeDate ? String(row.takeDate).slice(0, 10) : "-" }}</template></el-table-column>
        <el-table-column prop="lineCount" label="品项数" width="90" align="right" />
        <el-table-column prop="totalDiffQty" label="差异数量" width="110" align="right"><template #default="{ row }"><span :class="Number(row.totalDiffQty) < 0 ? 'diff-negative' : Number(row.totalDiffQty) > 0 ? 'diff-positive' : ''">{{ quantity(row.totalDiffQty) }}</span></template></el-table-column>
        <el-table-column label="状态" width="110"><template #default="{ row }"><el-tag :type="statusInfo(row.takeStatus).type as any" size="small">{{ statusInfo(row.takeStatus).label }}</el-tag></template></el-table-column>
        <el-table-column label="操作" width="170" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="openTake(row)">明细 / 实盘</el-button><el-button v-if="row.takeStatus !== 2" link type="danger" @click="removeTake(row)">删除</el-button></template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>

    <el-dialog v-model="createVisible" title="按仓库生成盘点单" width="560px" destroy-on-close>
      <el-form label-width="95px"><el-form-item label="盘点仓库" required><el-select v-model="createForm.warehouseId" filterable placeholder="请选择仓库" style="width: 100%"><el-option v-for="w in warehouses" :key="w.id" :label="w.name" :value="w.id" /></el-select></el-form-item><el-form-item label="盘点名称"><el-input v-model="createForm.name" placeholder="默认：库存盘点" /></el-form-item><el-form-item label="备注"><el-input v-model="createForm.remark" type="textarea" :rows="3" /></el-form-item><div class="hint">系统将按该仓库当前库存生成盘点明细。</div></el-form>
      <template #footer><el-button @click="createVisible = false">取消</el-button><el-button type="primary" :loading="creating" @click="createTake">生成盘点单</el-button></template>
    </el-dialog>

    <el-dialog v-model="detailVisible" :title="currentTake ? `盘点明细 - ${currentTake.code || ''}` : '盘点明细'" width="960px" destroy-on-close>
      <template v-if="currentTake">
        <el-descriptions :column="3" border><el-descriptions-item label="盘点单号">{{ currentTake.code || "-" }}</el-descriptions-item><el-descriptions-item label="仓库">{{ optionName(warehouses, currentTake.warehouseId) }}</el-descriptions-item><el-descriptions-item label="状态"><el-tag :type="statusInfo(currentTake.takeStatus).type as any" size="small">{{ statusInfo(currentTake.takeStatus).label }}</el-tag></el-descriptions-item><el-descriptions-item label="盘点日期">{{ currentTake.takeDate ? String(currentTake.takeDate).slice(0, 10) : "-" }}</el-descriptions-item><el-descriptions-item label="差异合计"><span :class="Number(currentTake.totalDiffQty) < 0 ? 'diff-negative' : Number(currentTake.totalDiffQty) > 0 ? 'diff-positive' : ''">{{ quantity(currentTake.totalDiffQty) }}</span></el-descriptions-item><el-descriptions-item label="备注">{{ currentTake.remark || "-" }}</el-descriptions-item></el-descriptions>
        <el-table :data="currentTake.lines" border stripe size="small" style="margin-top: 14px">
          <el-table-column label="产品" min-width="220"><template #default="{ row }">{{ optionName(products, row.productId) }}</template></el-table-column>
          <el-table-column prop="location" label="库位" width="120" />
          <el-table-column prop="bookQty" label="账面数量" width="120" align="right"><template #default="{ row }">{{ quantity(row.bookQty) }}</template></el-table-column>
          <el-table-column label="实盘数量" width="165" align="right"><template #default="{ row }"><el-input-number v-if="currentTake.takeStatus !== 2" v-model="row.actualQty" :min="0" :precision="3" controls-position="right" style="width: 145px" /><span v-else>{{ quantity(row.actualQty) }}</span></template></el-table-column>
          <el-table-column label="差异" width="120" align="right"><template #default="{ row }"><span :class="diffOf(row) < 0 ? 'diff-negative' : diffOf(row) > 0 ? 'diff-positive' : ''">{{ quantity(diffOf(row)) }}</span></template></el-table-column>
        </el-table>
      </template>
      <template #footer><el-button v-if="currentTake && currentTake.takeStatus !== 2" @click="saveLines()">保存实盘数量</el-button><el-button v-if="currentTake && currentTake.takeStatus !== 2" type="primary" :loading="saving" @click="completeTake">完成盘点</el-button><el-button @click="detailVisible = false">关闭</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.take-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .hint { color: var(--el-text-color-secondary); font-size: 12px; padding-left: 95px; }
  .diff-negative { color: var(--el-color-danger); font-weight: 600; }
  .diff-positive { color: var(--el-color-success); font-weight: 600; }
}
</style>
