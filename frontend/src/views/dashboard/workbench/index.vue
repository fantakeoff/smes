<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { http } from "@/utils/http";
import { useUserStoreHook } from "@/store/modules/user";
import { workOrderStatus as workOrderStatusDict } from "@/utils/biz";

const router = useRouter();
const loading = ref(false);
const overview = ref<any>({});
const recent = ref<any[]>([]);
const pendingExceptions = ref<number | null>(null);
const pendingInspections = ref<number | null>(null);
const activeWorkOrders = ref<number | null>(null);
const inventoryAlerts = ref<any[]>([]);
const inventorySource = ref<"api" | "fallback" | "error">("api");

const greeting = computed(() => {
  const hour = new Date().getHours();
  return hour < 11 ? "早上好" : hour < 14 ? "中午好" : hour < 18 ? "下午好" : "晚上好";
});
const todayText = computed(() => new Date().toLocaleDateString("zh-CN", {
  year: "numeric", month: "long", day: "numeric", weekday: "long"
}));
const userName = computed(() => useUserStoreHook().nickname || "管理员");
const inventoryCount = computed(() => inventorySource.value === "error" ? null : inventoryAlerts.value.length);

async function getData(url: string, params?: Record<string, any>) {
  try {
    const res: any = await http.request("get", url, { params });
    return res?.code === 0 ? res.data : null;
  } catch {
    return null;
  }
}

async function loadInventoryFallback() {
  const [productData, stockData] = await Promise.all([
    getData("/product", { "page.Page": 1, "page.PageSize": 100 }),
    getData("/inventory/stock", { "page.Page": 1, "page.PageSize": 100 })
  ]);
  if (!productData?.list || !stockData?.list) return null;
  const stockByProduct = new Map<number, number>();
  for (const row of stockData.list) {
    const id = Number(row.productId);
    stockByProduct.set(id, (stockByProduct.get(id) ?? 0) + Number(row.qty ?? 0));
  }
  return productData.list
    .filter((product: any) => Number(product.safetyStock) > 0 && (stockByProduct.get(Number(product.id)) ?? 0) < Number(product.safetyStock))
    .map((product: any) => {
      const currentQty = stockByProduct.get(Number(product.id)) ?? 0;
      return { ...product, currentQty, shortageQty: Number(product.safetyStock) - currentQty };
    });
}

async function load() {
  loading.value = true;
  try {
    const [ov, recentData, exceptionData, inspectionData, workOrderData, alertData] = await Promise.all([
      getData("/dashboard/overview"),
      getData("/dashboard/recent-work-orders", { limit: 6 }),
      getData("/exception", { exceptionStatus: 0, page: 1, pageSize: 5 }),
      getData("/quality-inspection", { result: 0, page: 1, pageSize: 5 }),
      getData("/work-order", { page: 1, pageSize: 100, workOrderStatus: 2 }),
      getData("/inventory/alert")
    ]);
    overview.value = ov ?? {};
    recent.value = Array.isArray(recentData) ? recentData : [];
    pendingExceptions.value = exceptionData == null ? null : Number(exceptionData.total ?? exceptionData.list?.length ?? 0);
    pendingInspections.value = inspectionData == null ? null : Number(inspectionData.total ?? inspectionData.list?.length ?? 0);
    activeWorkOrders.value = workOrderData == null ? null : Number(workOrderData.total ?? workOrderData.list?.length ?? 0);

    if (Array.isArray(alertData)) {
      inventoryAlerts.value = alertData;
      inventorySource.value = "api";
    } else {
      const fallback = await loadInventoryFallback();
      if (fallback) {
        inventoryAlerts.value = fallback;
        inventorySource.value = "fallback";
      } else {
        inventoryAlerts.value = [];
        inventorySource.value = "error";
      }
    }
  } finally {
    loading.value = false;
  }
}

function go(path: string) {
  router.push(path);
}
function progress(row: any) {
  const qty = Number(row.qty ?? 0);
  return qty > 0 ? Math.max(0, Math.min(100, Math.round(Number(row.completedQty ?? 0) / qty * 100))) : 0;
}
function statusInfo(status: number) {
  const item = workOrderStatusDict[Number(status)];
  return { label: item?.label ?? "未知", type: (item?.type ?? "info") as any };
}
function quantity(value: any) {
  return Number(value ?? 0).toLocaleString("zh-CN", { maximumFractionDigits: 2 });
}

onMounted(load);
</script>

<template>
  <div class="workbench-page">
    <el-card shadow="never" class="welcome-card">
      <div>
        <div class="welcome-title">{{ greeting }}，{{ userName }}</div>
        <div class="welcome-subtitle">{{ todayText }} · 这里汇总了您需要关注的生产事项</div>
      </div>
      <div class="welcome-summary">
        <div><span>今日产量</span><strong>{{ quantity(overview.todayOutput) }}</strong></div>
        <div><span>今日良率</span><strong>{{ overview.todayYield ?? "—" }}<small v-if="overview.todayYield != null">%</small></strong></div>
      </div>
    </el-card>

    <section class="section-block">
      <div class="section-heading"><div><h3>快捷入口</h3><span>常用生产操作</span></div></div>
      <el-row :gutter="12">
        <el-col v-for="item in [
          { title: '新建计划', desc: '编制生产计划', mark: '计', path: '/production/plan', color: 'blue' },
          { title: '新建工单', desc: '创建生产工单', mark: '单', path: '/production/workorder', color: 'orange' },
          { title: '报工', desc: '登记生产进度', mark: '报', path: '/production/report', color: 'green' },
          { title: '异常提报', desc: '记录现场异常', mark: '异', path: '/exception/report', color: 'red' }
        ]" :key="item.path" :xs="12" :sm="6">
          <button class="quick-entry" type="button" @click="go(item.path)">
            <span class="entry-mark" :class="item.color">{{ item.mark }}</span>
            <span class="entry-copy"><strong>{{ item.title }}</strong><small>{{ item.desc }}</small></span>
            <span class="entry-arrow">›</span>
          </button>
        </el-col>
      </el-row>
    </section>

    <section class="section-block">
      <div class="section-heading"><div><h3>待办事项</h3><span>点击卡片查看待处理列表</span></div><el-button text type="primary" :loading="loading" @click="load">刷新</el-button></div>
      <el-row :gutter="12">
        <el-col :xs="12" :sm="6">
          <button class="todo-card danger" type="button" @click="go('/exception/report')">
            <span class="todo-label">待处理异常</span><strong>{{ pendingExceptions ?? "—" }}</strong><span class="todo-foot">现场异常待跟进 <b>›</b></span>
          </button>
        </el-col>
        <el-col :xs="12" :sm="6">
          <button class="todo-card warning" type="button" @click="go('/quality/inspection')">
            <span class="todo-label">待检任务</span><strong>{{ pendingInspections ?? "—" }}</strong><span class="todo-foot">检验结果待判定 <b>›</b></span>
          </button>
        </el-col>
        <el-col :xs="12" :sm="6">
          <button class="todo-card primary" type="button" @click="go('/production/workorder')">
            <span class="todo-label">生产中的工单</span><strong>{{ activeWorkOrders ?? "—" }}</strong><span class="todo-foot">查看工单进度 <b>›</b></span>
          </button>
        </el-col>
        <el-col :xs="12" :sm="6">
          <button class="todo-card warning" type="button" @click="go('/warehouse/alert')">
            <span class="todo-label">库存预警</span><strong>{{ inventoryCount ?? "—" }}</strong><span class="todo-foot">{{ inventorySource === 'fallback' ? '由库存台账核算' : inventorySource === 'error' ? '库存数据暂不可用' : '低于安全库存' }} <b>›</b></span>
          </button>
        </el-col>
      </el-row>
    </section>

    <el-card shadow="never" class="recent-card">
      <div class="table-heading">
        <div><h3>最近工单</h3><span>最新工单状态与完成进度</span></div>
        <el-button text type="primary" @click="go('/production/workorder')">查看全部 <span class="arrow">›</span></el-button>
      </div>
      <el-table v-loading="loading" :data="recent" border stripe>
        <el-table-column prop="code" label="工单编号" min-width="170" />
        <el-table-column prop="productName" label="产品" min-width="150" show-overflow-tooltip>
          <template #default="{ row }">{{ row.productName || row.name || "—" }} <span class="muted">{{ row.spec }}</span></template>
        </el-table-column>
        <el-table-column label="状态" width="110">
          <template #default="{ row }"><el-tag size="small" :type="statusInfo(row.workOrderStatus).type">{{ statusInfo(row.workOrderStatus).label }}</el-tag></template>
        </el-table-column>
        <el-table-column label="计划 / 完成" width="150" align="right">
          <template #default="{ row }">{{ quantity(row.completedQty) }} / {{ quantity(row.qty) }}</template>
        </el-table-column>
        <el-table-column label="进度" min-width="190">
          <template #default="{ row }"><el-progress :percentage="progress(row)" :stroke-width="8" /></template>
        </el-table-column>
        <el-table-column label="计划周期" min-width="190">
          <template #default="{ row }">{{ row.startDate?.slice(0, 10) || "—" }} ~ {{ row.endDate?.slice(0, 10) || "—" }}</template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!loading && recent.length === 0" description="暂无工单数据" />
    </el-card>
  </div>
</template>

<style scoped lang="scss">
.workbench-page {
  .welcome-card {
    margin-bottom: 16px;
    border: 0;
    color: #fff;
    background: linear-gradient(115deg, #2365d7 0%, #347ee8 58%, #69a7f5 100%);
    :deep(.el-card__body) { display: flex; align-items: center; justify-content: space-between; gap: 20px; min-height: 110px; }
  }
  .welcome-title { font-size: 24px; font-weight: 700; letter-spacing: .3px; }
  .welcome-subtitle { margin-top: 9px; opacity: .84; font-size: 13px; }
  .welcome-summary { display: flex; gap: 32px; padding-right: 12px; > div { display: flex; flex-direction: column; gap: 7px; } span { font-size: 12px; opacity: .78; } strong { font-size: 21px; } small { font-size: 13px; margin-left: 3px; } }
  .section-block { margin-bottom: 18px; }
  .section-heading, .table-heading { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; }
  h3 { margin: 0; font-size: 16px; font-weight: 650; }
  .section-heading span, .table-heading span { color: var(--el-text-color-secondary); font-size: 12px; }
  .section-heading h3, .table-heading h3 { margin-bottom: 3px; }
  .quick-entry, .todo-card { width: 100%; border: 1px solid var(--el-border-color-lighter); background: var(--el-bg-color); border-radius: 8px; text-align: left; cursor: pointer; transition: transform .18s ease, box-shadow .18s ease; }
  .quick-entry { min-height: 88px; display: flex; align-items: center; gap: 12px; padding: 14px; &:hover, &:focus-visible { transform: translateY(-2px); box-shadow: 0 7px 20px rgba(40, 76, 125, .12); outline: none; } }
  .entry-mark { display: grid; place-items: center; width: 42px; height: 42px; border-radius: 12px; font-weight: 700; font-size: 18px; &.blue { color: #3478db; background: #eaf2ff; } &.orange { color: #db8b25; background: #fff3e3; } &.green { color: #31a36b; background: #e7f8ef; } &.red { color: #db5a62; background: #fff0f0; } }
  .entry-copy { flex: 1; display: flex; flex-direction: column; gap: 5px; strong { font-size: 14px; color: var(--el-text-color-primary); } small { font-size: 12px; color: var(--el-text-color-secondary); } }
  .entry-arrow { color: #aab4c2; font-size: 25px; }
  .todo-card { position: relative; min-height: 126px; display: flex; flex-direction: column; align-items: flex-start; padding: 15px 17px 12px; &:hover, &:focus-visible { transform: translateY(-2px); box-shadow: 0 7px 20px rgba(40, 76, 125, .1); outline: none; } strong { margin: 5px 0 7px; font-size: 28px; line-height: 1.1; color: var(--el-text-color-primary); } &.danger { border-left: 3px solid #e76c6c; } &.warning { border-left: 3px solid #e8a23b; } &.primary { border-left: 3px solid #4388e7; } &.success { border-left: 3px solid #48b883; } }
  .todo-label { color: var(--el-text-color-secondary); font-size: 13px; }
  .todo-foot { width: 100%; display: flex; justify-content: space-between; align-items: center; color: var(--el-text-color-secondary); font-size: 12px; b { color: #99a5b4; font-size: 18px; font-weight: 400; } }
  .recent-card { :deep(.el-card__body) { padding: 18px; } }
  .table-heading { margin-bottom: 14px; }
  .arrow { margin-left: 4px; font-size: 16px; }
  .muted { margin-left: 5px; color: var(--el-text-color-secondary); font-size: 12px; }
  @media (max-width: 768px) { .welcome-card :deep(.el-card__body) { align-items: flex-start; flex-direction: column; } .welcome-summary { gap: 24px; } }
}
</style>
