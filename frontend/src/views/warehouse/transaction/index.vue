<script setup lang="ts">
import { onMounted, ref } from "vue";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { billTypeDict } from "@/utils/biz";

type WarehouseOption = { id: number; code?: string; name: string };
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const warehouseId = ref<number | undefined>();
const billType = ref<number | undefined>();
const warehouses = ref<WarehouseOption[]>([]);
const billTypes = Object.entries(billTypeDict).map(([value, item]) => ({ value: Number(value), ...item }));
function quantity(value: any) { return Number(value ?? 0).toLocaleString("zh-CN", { maximumFractionDigits: 3 }); }
function money(value: any) { return Number(value ?? 0).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
function typeInfo(value: number) { return billTypeDict[value] ?? { label: `类型${value}`, type: "info" }; }
function errorText(error: any) { return error?.response?.data?.message || error?.response?.data?.Message || error?.message || "加载失败"; }
async function loadWarehouses() {
  try {
    const res: any = await http.request("get", "/warehouse/options");
    if (res?.code === 0) warehouses.value = res.data ?? [];
  } catch (error: any) { message(errorText(error), { type: "error" }); }
}
async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/inventory/transactions", {
      params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined, warehouseId: warehouseId.value, billType: billType.value }
    });
    if (res?.code === 0) { list.value = res.data?.list ?? []; total.value = res.data?.total ?? 0; }
    else message(res?.message || "库存流水加载失败", { type: "error" });
  } catch (error: any) { message(errorText(error), { type: "error" }); }
  finally { loading.value = false; }
}
function search() { page.value = 1; fetchList(); }
function reset() { keyword.value = ""; warehouseId.value = undefined; billType.value = undefined; search(); }
onMounted(() => { loadWarehouses(); fetchList(); });
</script>

<template>
  <div class="transaction-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字"><el-input v-model="keyword" clearable placeholder="单据号 / 产品编码 / 名称" style="width: 240px" @keyup.enter="search" /></el-form-item>
        <el-form-item label="仓库"><el-select v-model="warehouseId" clearable filterable placeholder="全部仓库" style="width: 180px"><el-option v-for="w in warehouses" :key="w.id" :label="w.name" :value="w.id" /></el-select></el-form-item>
        <el-form-item label="业务类型"><el-select v-model="billType" clearable placeholder="全部类型" style="width: 160px"><el-option v-for="t in billTypes" :key="t.value" :label="t.label" :value="t.value" /></el-select></el-form-item>
        <el-form-item><el-button type="primary" @click="search">查询</el-button><el-button @click="reset">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><span class="title">库存流水</span></div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="occurredAt" label="发生时间" width="170"><template #default="{ row }">{{ row.occurredAt ? String(row.occurredAt).replace("T", " ").slice(0, 19) : "-" }}</template></el-table-column>
        <el-table-column prop="billCode" label="单据编号" width="170" />
        <el-table-column label="业务类型" width="120"><template #default="{ row }"><el-tag :type="typeInfo(row.billType).type as any" size="small">{{ typeInfo(row.billType).label }}</el-tag></template></el-table-column>
        <el-table-column prop="productCode" label="产品编码" width="135" />
        <el-table-column prop="productName" label="产品名称" min-width="145" show-overflow-tooltip />
        <el-table-column prop="warehouseName" label="仓库" width="130" />
        <el-table-column label="方向" width="90" align="center"><template #default="{ row }"><el-tag :type="row.direction > 0 ? 'success' : 'danger'" size="small">{{ row.direction > 0 ? "入库" : "出库" }}</el-tag></template></el-table-column>
        <el-table-column prop="qty" label="变动数量" width="110" align="right"><template #default="{ row }">{{ quantity(row.qty) }}</template></el-table-column>
        <el-table-column prop="beforeQty" label="变动前" width="110" align="right"><template #default="{ row }">{{ quantity(row.beforeQty) }}</template></el-table-column>
        <el-table-column prop="afterQty" label="变动后" width="110" align="right"><template #default="{ row }">{{ quantity(row.afterQty) }}</template></el-table-column>
        <el-table-column prop="price" label="参考单价" width="120" align="right"><template #default="{ row }">¥{{ money(row.price) }}</template></el-table-column>
      </el-table>
      <div class="pager"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50, 100]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" /></div>
    </el-card>
  </div>
</template>

<style scoped lang="scss">
.transaction-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
}
</style>
