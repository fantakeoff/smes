<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from "vue";
import * as echarts from "echarts";
import { http } from "@/utils/http";
import { useLookupStore } from "@/store/modules/lookup";

const lookup = useLookupStore();
const loading = ref(false);
const orders = ref<any[]>([]);
const totalOrders = ref(0);
const statusData = ref<any[]>([]);
const defectData = ref<any[]>([]);
const refreshedAt = ref<Date | null>(null);
const statusChartRef = ref<HTMLDivElement>();
const defectChartRef = ref<HTMLDivElement>();
let statusChart: echarts.ECharts | null = null;
let defectChart: echarts.ECharts | null = null;
let refreshTimer: ReturnType<typeof setInterval> | undefined;
const statusLabels: Record<number, string> = { 0: "未下达", 1: "已下达", 2: "生产中", 3: "已完工", 4: "已关闭" };
const statusColors: Record<number, string> = { 0: "#6d7d98", 1: "#4388e8", 2: "#f0a33d", 3: "#31b987", 4: "#e56671" };

const statusSummary = computed(() => {
  const counts = new Map(statusData.value.map(row => [Number(row.status), Number(row.count ?? 0)]));
  return Object.keys(statusLabels).map(key => ({ status: Number(key), name: statusLabels[Number(key)], count: counts.get(Number(key)) ?? 0 }));
});
const inProgressCount = computed(() => statusSummary.value.find(row => row.status === 2)?.count ?? 0);
const completedCount = computed(() => statusSummary.value.find(row => row.status === 3)?.count ?? 0);
const achievementRate = computed(() => {
  const planned = orders.value.reduce((sum, row) => sum + Number(row.qty ?? 0), 0);
  const completed = orders.value.reduce((sum, row) => sum + Number(row.completedQty ?? 0), 0);
  return planned > 0 ? Math.min(100, completed / planned * 100) : 0;
});
const boardOrders = computed(() => [...orders.value].sort((a, b) => {
  const priority = (Number(b.workOrderStatus) === 2 ? 4 : Number(b.workOrderStatus) === 1 ? 3 : 0) - (Number(a.workOrderStatus) === 2 ? 4 : Number(a.workOrderStatus) === 1 ? 3 : 0);
  return priority || Number(b.priority ?? 0) - Number(a.priority ?? 0) || Number(b.id ?? 0) - Number(a.id ?? 0);
}));

function quantity(value: any) {
  return Number(value ?? 0).toLocaleString("zh-CN", { maximumFractionDigits: 2 });
}
function progress(row: any) {
  return Number(row.qty) > 0 ? Math.max(0, Math.min(100, Number(row.completedQty ?? 0) / Number(row.qty) * 100)) : 0;
}
function statusInfo(status: number) {
  const code = Number(status);
  const type: Record<number, string> = { 0: "info", 1: "primary", 2: "warning", 3: "success", 4: "danger" };
  return { label: statusLabels[code] ?? "未知", type: type[code] ?? "info" };
}
function refreshedText() {
  return refreshedAt.value ? refreshedAt.value.toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit", second: "2-digit" }) : "等待数据";
}

function renderCharts() {
  if (statusChartRef.value) {
    statusChart = statusChart ?? echarts.getInstanceByDom(statusChartRef.value) ?? echarts.init(statusChartRef.value);
    statusChart.setOption({
      backgroundColor: "transparent",
      tooltip: { trigger: "item", formatter: "{b}: {c} 张（{d}%）" },
      legend: { bottom: 0, textStyle: { color: "#a7b5d0" }, itemWidth: 9, itemHeight: 9 },
      series: [{ type: "pie", radius: ["43%", "68%"], center: ["50%", "43%"], avoidLabelOverlap: true,
        data: statusSummary.value.filter(row => row.count > 0).map(row => ({ name: row.name, value: row.count, itemStyle: { color: statusColors[row.status] } })),
        label: { color: "#dce6f8", formatter: "{b}  {c}" }, labelLine: { lineStyle: { color: "#667895" } }
      }]
    }, true);
  }
  if (defectChartRef.value) {
    defectChart = defectChart ?? echarts.getInstanceByDom(defectChartRef.value) ?? echarts.init(defectChartRef.value);
    const rows = [...defectData.value].slice(0, 8).reverse();
    defectChart.setOption({
      backgroundColor: "transparent",
      tooltip: { trigger: "axis", axisPointer: { type: "shadow" } },
      grid: { left: 92, right: 28, top: 12, bottom: 24 },
      xAxis: { type: "value", axisLabel: { color: "#93a4c1" }, splitLine: { lineStyle: { color: "rgba(119,145,184,.15)" } } },
      yAxis: { type: "category", data: rows.map(row => row.name), axisLabel: { color: "#d3def1", width: 82, overflow: "truncate" }, axisLine: { show: false }, axisTick: { show: false } },
      series: [{ name: "不良数", type: "bar", barMaxWidth: 18, data: rows.map(row => Number(row.qty ?? 0)), itemStyle: { color: "#ed7180", borderRadius: [0, 5, 5, 0] }, label: { show: true, position: "right", color: "#d3def1" } }]
    }, true);
  }
}

async function load() {
  loading.value = true;
  try {
    const [orderRes, statusRes, defectRes]: any[] = await Promise.all([
      http.request("get", "/work-order", { params: { page: 1, pageSize: 100 } }),
      http.request("get", "/dashboard/work-order-status"),
      http.request("get", "/dashboard/defect-rank", { params: { days: 30, limit: 8 } })
    ]);
    if (orderRes?.code === 0) {
      orders.value = orderRes.data?.list ?? [];
      totalOrders.value = Number(orderRes.data?.total ?? orders.value.length);
    }
    if (statusRes?.code === 0) statusData.value = statusRes.data ?? [];
    if (defectRes?.code === 0) defectData.value = defectRes.data ?? [];
    refreshedAt.value = new Date();
    await nextTick();
    renderCharts();
  } catch {
    // 保留最近一次成功结果，自动刷新失败时不清空看板。
  } finally {
    loading.value = false;
  }
}
function resize() { statusChart?.resize(); defectChart?.resize(); }

onMounted(async () => {
  await lookup.load();
  await load();
  refreshTimer = setInterval(load, 30_000);
  window.addEventListener("resize", resize);
});
onBeforeUnmount(() => {
  if (refreshTimer) clearInterval(refreshTimer);
  window.removeEventListener("resize", resize);
  statusChart?.dispose();
  defectChart?.dispose();
  statusChart = null;
  defectChart = null;
});
</script>

<template>
  <div class="production-board">
    <div class="board-topline">
      <div class="board-brand"><span class="brand-mark">MES</span><div><strong>工单进度看板</strong><small>生产执行实时监控</small></div></div>
      <div class="board-tools"><span class="live-dot" /> 实时数据 · 最近更新 {{ refreshedText() }}<el-button class="refresh-btn" :loading="loading" @click="load">刷新</el-button></div>
    </div>

    <el-row :gutter="12" class="metrics-row">
      <el-col :xs="12" :sm="6"><div class="metric-card"><span>工单总数</span><strong>{{ quantity(totalOrders) }}</strong><small>全部工单</small><i class="metric-icon blue">单</i></div></el-col>
      <el-col :xs="12" :sm="6"><div class="metric-card"><span>生产中</span><strong class="orange">{{ quantity(inProgressCount) }}</strong><small>正在执行</small><i class="metric-icon orange-bg">产</i></div></el-col>
      <el-col :xs="12" :sm="6"><div class="metric-card"><span>完工数</span><strong class="green">{{ quantity(completedCount) }}</strong><small>已完成工单</small><i class="metric-icon green-bg">成</i></div></el-col>
      <el-col :xs="12" :sm="6"><div class="metric-card"><span>生产达成率</span><strong class="cyan">{{ achievementRate.toFixed(1) }}<small>%</small></strong><small>累计完成量 / 计划量</small><i class="metric-icon cyan-bg">率</i></div></el-col>
    </el-row>

    <el-row :gutter="12" class="board-content">
      <el-col :xs="24" :xl="16">
        <section class="dark-panel progress-panel">
          <div class="panel-heading"><div><strong>工单执行进度</strong><small>展示 {{ boardOrders.length }} / {{ totalOrders }} 张工单</small></div><span class="panel-live">● 自动刷新 30 秒</span></div>
          <div class="order-list" v-loading="loading">
            <article v-for="order in boardOrders" :key="order.id" class="order-card">
              <div class="order-main">
                <div class="order-ident"><strong>{{ order.code }}</strong><el-tag size="small" effect="dark" :type="statusInfo(order.workOrderStatus).type as any">{{ statusInfo(order.workOrderStatus).label }}</el-tag></div>
                <span class="order-name">{{ lookup.name('products', order.productId) || order.name || '未命名产品' }}</span>
                <span class="order-workshop">{{ lookup.name('workshops', order.workshopId) || '未分配车间' }}<i>·</i>{{ order.startDate?.slice(0, 10) || '—' }} 至 {{ order.endDate?.slice(0, 10) || '—' }}</span>
              </div>
              <div class="order-progress">
                <div class="progress-meta"><span>完成 {{ quantity(order.completedQty) }} / {{ quantity(order.qty) }}</span><b>{{ progress(order).toFixed(1) }}%</b></div>
                <el-progress :percentage="progress(order)" :stroke-width="9" :show-text="false" :color="Number(order.workOrderStatus) === 3 ? '#31b987' : Number(order.workOrderStatus) === 2 ? '#f0a33d' : '#4388e8'" />
              </div>
            </article>
            <el-empty v-if="!loading && boardOrders.length === 0" description="暂无工单数据" />
          </div>
        </section>
      </el-col>
      <el-col :xs="24" :xl="8">
        <section class="dark-panel chart-panel">
          <div class="panel-heading"><div><strong>工单状态分布</strong><small>各状态工单数量</small></div></div>
          <div ref="statusChartRef" class="board-chart status-chart" />
          <div class="status-legend"><span v-for="item in statusSummary" :key="item.status"><i :style="{ background: statusColors[item.status] }" />{{ item.name }} <b>{{ item.count }}</b></span></div>
        </section>
        <section class="dark-panel chart-panel defect-panel">
          <div class="panel-heading"><div><strong>不良项排名</strong><small>近 30 天 · 按不良数量</small></div></div>
          <div ref="defectChartRef" class="board-chart defect-chart" />
          <div v-if="defectData.length === 0" class="chart-empty">暂无不良数据</div>
        </section>
      </el-col>
    </el-row>
  </div>
</template>

<style scoped lang="scss">
.production-board {
  min-height: calc(100vh - 120px); padding: 20px; border-radius: 12px; color: #dce6f8;
  background: radial-gradient(ellipse at 10% 0%, rgba(41, 94, 166, .22), transparent 38%), linear-gradient(135deg, #081426 0%, #0a1930 52%, #0c1d35 100%);
  .board-topline { display: flex; justify-content: space-between; align-items: center; margin-bottom: 18px; gap: 14px; flex-wrap: wrap; }
  .board-brand { display: flex; align-items: center; gap: 12px; .brand-mark { display: grid; place-items: center; width: 46px; height: 40px; border: 1px solid rgba(84, 166, 255, .5); border-radius: 8px; color: #71b7ff; font-weight: 800; letter-spacing: 1px; background: rgba(51, 122, 208, .16); } div { display: flex; flex-direction: column; gap: 4px; } strong { font-size: 19px; color: #f0f5ff; } small { color: #8da2c2; font-size: 12px; } }
  .board-tools { display: flex; align-items: center; gap: 8px; color: #92a6c5; font-size: 12px; }
  .live-dot { width: 7px; height: 7px; border-radius: 50%; background: #36d5a0; box-shadow: 0 0 10px rgba(54, 213, 160, .65); }
  .refresh-btn { margin-left: 8px; color: #cfe0fa; border-color: rgba(111, 145, 190, .34); background: rgba(28, 51, 81, .62); &:hover { color: #fff; border-color: #4388e8; background: rgba(54, 110, 185, .24); } }
  .metrics-row { margin-bottom: 12px; }
  .metric-card { position: relative; min-height: 122px; padding: 16px 18px; overflow: hidden; border: 1px solid rgba(105, 139, 187, .2); border-radius: 9px; background: linear-gradient(135deg, rgba(20, 41, 68, .94), rgba(14, 32, 55, .96)); box-shadow: 0 7px 22px rgba(0, 0, 0, .14); display: flex; flex-direction: column; gap: 7px; > span { color: #93a7c5; font-size: 13px; } > strong { color: #e8f1ff; font-size: 29px; line-height: 1.1; font-weight: 700; &.orange { color: #ffb85c; } &.green { color: #48d2a3; } &.cyan { color: #55c5e8; } small { margin-left: 3px; font-size: 14px; } } > small { color: #7087a8; font-size: 11px; } }
  .metric-icon { position: absolute; right: 16px; top: 16px; display: grid; place-items: center; width: 36px; height: 36px; border-radius: 10px; font-style: normal; font-weight: 700; color: #70aaff; background: rgba(57, 117, 201, .18); &.orange-bg { color: #ffb85c; background: rgba(227, 144, 45, .16); } &.green-bg { color: #48d2a3; background: rgba(42, 178, 131, .15); } &.cyan-bg { color: #55c5e8; background: rgba(45, 168, 202, .15); } }
  .dark-panel { border: 1px solid rgba(105, 139, 187, .2); border-radius: 9px; background: rgba(12, 29, 51, .9); box-shadow: 0 7px 22px rgba(0, 0, 0, .13); }
  .progress-panel { padding: 16px 17px; }
  .chart-panel { padding: 16px 15px 12px; margin-bottom: 12px; }
  .panel-heading { display: flex; justify-content: space-between; align-items: center; gap: 10px; margin-bottom: 12px; > div { display: flex; flex-direction: column; gap: 5px; } strong { color: #e5eefb; font-size: 14px; } small { color: #7188a8; font-size: 11px; } }
  .panel-live { color: #4bcda1; font-size: 11px; }
  .order-list { max-height: 650px; min-height: 220px; overflow: auto; padding-right: 3px; }
  .order-card { display: grid; grid-template-columns: minmax(210px, .9fr) minmax(170px, 1.1fr); align-items: center; gap: 20px; padding: 13px 14px; margin-bottom: 9px; border: 1px solid rgba(97, 128, 170, .16); border-radius: 7px; background: linear-gradient(100deg, rgba(23, 45, 73, .82), rgba(19, 39, 64, .76)); &:last-child { margin-bottom: 0; } }
  .order-main { min-width: 0; display: flex; flex-direction: column; gap: 6px; }
  .order-ident { display: flex; align-items: center; gap: 8px; strong { color: #b7d6ff; font-size: 12px; letter-spacing: .2px; } :deep(.el-tag) { border-color: rgba(130, 160, 203, .2); } }
  .order-name { overflow: hidden; color: #e3ecfa; font-size: 13px; text-overflow: ellipsis; white-space: nowrap; }
  .order-workshop { color: #8197b6; font-size: 11px; i { margin: 0 7px; color: #506988; font-style: normal; } }
  .order-progress { min-width: 0; }
  .progress-meta { display: flex; justify-content: space-between; gap: 8px; margin-bottom: 8px; color: #91a6c4; font-size: 11px; b { color: #dce8f9; font-size: 12px; } }
  :deep(.el-progress-bar__outer) { background-color: rgba(75, 101, 137, .3); }
  .board-chart { width: 100%; }
  .status-chart { height: 225px; }
  .defect-chart { height: 250px; }
  .status-legend { display: grid; grid-template-columns: repeat(2, 1fr); gap: 7px 10px; padding: 5px 8px 2px; color: #94a8c5; font-size: 11px; span { display: flex; align-items: center; gap: 6px; } i { width: 7px; height: 7px; border-radius: 50%; } b { margin-left: auto; color: #d4e0f1; font-weight: 600; } }
  .defect-panel { min-height: 330px; }
  .chart-empty { margin-top: -160px; position: relative; text-align: center; color: #7188a8; font-size: 12px; pointer-events: none; }
  :deep(.el-loading-mask) { background: rgba(8, 20, 38, .76); }
  :deep(.el-empty__description p) { color: #8197b6; }
  @media (max-width: 900px) { padding: 14px; .order-card { grid-template-columns: 1fr; gap: 12px; } .order-list { max-height: 520px; } }
  @media (max-width: 560px) { .board-tools { flex-wrap: wrap; } .metric-card { min-height: 108px; padding: 13px; } .metric-icon { right: 10px; top: 10px; } }
}
</style>
