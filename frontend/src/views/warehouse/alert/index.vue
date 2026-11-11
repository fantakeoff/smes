<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { http } from "@/utils/http";
import { message } from "@/utils/message";

type AlertItem = { id: number; code: string; name: string; spec?: string; safetyStock: number; currentQty: number; shortageQty: number };
const loading = ref(false);
const list = ref<AlertItem[]>([]);
const keyword = ref("");
const filteredList = computed(() => {
  const value = keyword.value.trim().toLowerCase();
  if (!value) return list.value;
  return list.value.filter(item => `${item.code ?? ""} ${item.name ?? ""} ${item.spec ?? ""}`.toLowerCase().includes(value));
});
function quantity(value: any) { return Number(value ?? 0).toLocaleString("zh-CN", { maximumFractionDigits: 3 }); }
function errorText(error: any) { return error?.response?.data?.message || error?.response?.data?.Message || error?.message || "库存预警加载失败"; }
async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/inventory/alert");
    if (res?.code === 0) list.value = res.data ?? [];
    else message(res?.message || "库存预警加载失败", { type: "error" });
  } catch (error: any) { message(errorText(error), { type: "error" }); }
  finally { loading.value = false; }
}
onMounted(fetchList);
</script>

<template>
  <div class="alert-page">
    <el-card shadow="never" class="search-card"><el-form inline @submit.prevent><el-form-item label="产品"><el-input v-model="keyword" clearable placeholder="产品编码 / 名称 / 规格" style="width: 260px" /></el-form-item><el-form-item><el-button type="primary" @click="fetchList">刷新</el-button></el-form-item></el-form></el-card>
    <el-card shadow="never">
      <div class="toolbar"><div><span class="title">库存预警</span><span class="count">低于安全库存 {{ filteredList.length }} 项</span></div><el-button @click="fetchList" :loading="loading">刷新</el-button></div>
      <el-table v-loading="loading" :data="filteredList" border stripe>
        <el-table-column prop="code" label="产品编码" width="150" />
        <el-table-column prop="name" label="产品名称" min-width="180" show-overflow-tooltip />
        <el-table-column prop="spec" label="规格型号" width="150" show-overflow-tooltip />
        <el-table-column prop="safetyStock" label="安全库存" width="140" align="right"><template #default="{ row }">{{ quantity(row.safetyStock) }}</template></el-table-column>
        <el-table-column prop="currentQty" label="当前库存" width="140" align="right"><template #default="{ row }">{{ quantity(row.currentQty) }}</template></el-table-column>
        <el-table-column prop="shortageQty" label="缺口数量" width="140" align="right"><template #default="{ row }"><el-tag type="danger" size="small">{{ quantity(row.shortageQty) }}</el-tag></template></el-table-column>
        <el-table-column label="预警状态" width="120" align="center"><template #default><el-tag type="warning" size="small">低于安全库存</el-tag></template></el-table-column>
      </el-table>
      <el-empty v-if="!loading && filteredList.length === 0" :description="keyword ? '没有匹配的预警产品' : '当前没有库存预警'" />
    </el-card>
  </div>
</template>

<style scoped lang="scss">
.alert-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
  .count { margin-left: 12px; color: var(--el-color-danger); font-size: 13px; }
}
</style>
