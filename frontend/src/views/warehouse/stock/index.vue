<script setup lang="ts">
import { onMounted, ref } from "vue";
import { http } from "@/utils/http";
import { message } from "@/utils/message";

type WarehouseOption = { id: number; code?: string; name: string };
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const warehouseId = ref<number | undefined>();
const warehouses = ref<WarehouseOption[]>([]);
const summary = ref<any>({});

function money(value: any) {
  return Number(value ?? 0).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
function quantity(value: any) {
  return Number(value ?? 0).toLocaleString("zh-CN", { maximumFractionDigits: 3 });
}
function errorText(error: any) {
  return error?.response?.data?.message || error?.response?.data?.Message || error?.message || "加载失败";
}

async function loadWarehouses() {
  try {
    const res: any = await http.request("get", "/warehouse/options");
    if (res?.code === 0) warehouses.value = res.data ?? [];
  } catch (error: any) {
    message(errorText(error), { type: "error" });
  }
}
async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/inventory/stock", {
      params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined, warehouseId: warehouseId.value }
    });
    if (res?.code === 0) {
      list.value = res.data?.list ?? [];
      total.value = res.data?.total ?? 0;
      summary.value = res.data?.summary ?? {};
    } else message(res?.message || "库存余额加载失败", { type: "error" });
  } catch (error: any) {
    message(errorText(error), { type: "error" });
  } finally {
    loading.value = false;
  }
}
function search() {
  page.value = 1;
  fetchList();
}
function reset() {
  keyword.value = "";
  warehouseId.value = undefined;
  search();
}
onMounted(() => {
  loadWarehouses();
  fetchList();
});
</script>

<template>
  <div class="stock-page">
    <el-row :gutter="12" class="summary-row">
      <el-col :xs="24" :sm="8">
        <el-card shadow="never" class="summary-card">
          <div class="summary-label">库存品项</div>
          <div class="summary-value">{{ total.toLocaleString("zh-CN") }}</div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="8">
        <el-card shadow="never" class="summary-card">
          <div class="summary-label">库存总数量</div>
          <div class="summary-value">{{ quantity(summary.totalQty) }}</div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="8">
        <el-card shadow="never" class="summary-card">
          <div class="summary-label">库存总金额</div>
          <div class="summary-value amount">¥{{ money(summary.totalAmount) }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字">
          <el-input v-model="keyword" clearable placeholder="产品编码 / 名称" style="width: 220px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="仓库">
          <el-select v-model="warehouseId" clearable filterable placeholder="全部仓库" style="width: 190px">
            <el-option v-for="w in warehouses" :key="w.id" :label="w.name" :value="w.id" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
          <el-button @click="reset">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div class="toolbar"><span class="title">库存余额</span><span class="muted">金额按库存数量 × 平均成本统计</span></div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="productCode" label="产品编码" width="140" />
        <el-table-column prop="productName" label="产品名称" min-width="150" show-overflow-tooltip />
        <el-table-column prop="spec" label="规格型号" width="130" show-overflow-tooltip />
        <el-table-column prop="warehouseName" label="仓库" width="130" />
        <el-table-column prop="qty" label="库存数量" width="110" align="right"><template #default="{ row }">{{ quantity(row.qty) }}</template></el-table-column>
        <el-table-column prop="lockedQty" label="锁定数量" width="110" align="right"><template #default="{ row }">{{ quantity(row.lockedQty) }}</template></el-table-column>
        <el-table-column prop="availableQty" label="可用数量" width="110" align="right"><template #default="{ row }">{{ quantity(row.availableQty) }}</template></el-table-column>
        <el-table-column prop="safetyStock" label="安全库存" width="110" align="right"><template #default="{ row }">{{ quantity(row.safetyStock) }}</template></el-table-column>
        <el-table-column prop="avgCost" label="平均成本" width="120" align="right"><template #default="{ row }">¥{{ money(row.avgCost) }}</template></el-table-column>
        <el-table-column prop="amount" label="库存金额" width="140" align="right"><template #default="{ row }">¥{{ money(row.amount) }}</template></el-table-column>
        <el-table-column prop="location" label="库位" width="110" />
        <el-table-column prop="batchNo" label="批次" width="120" />
      </el-table>
      <div class="pager">
        <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50, 100]" layout="total, sizes, prev, pager, next" background @current-change="fetchList" @size-change="search" />
      </div>
    </el-card>
  </div>
</template>

<style scoped lang="scss">
.stock-page {
  .summary-row { margin-bottom: 12px; }
  .summary-card { min-height: 96px; }
  .summary-label { color: var(--el-text-color-secondary); font-size: 13px; }
  .summary-value { margin-top: 8px; font-size: 24px; font-weight: 600; }
  .amount { color: var(--el-color-primary); }
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .muted { color: var(--el-text-color-secondary); font-size: 12px; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
}
</style>
