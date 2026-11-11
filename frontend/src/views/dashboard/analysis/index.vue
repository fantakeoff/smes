<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from "vue";
import * as echarts from "echarts";
import { http } from "@/utils/http";
import { useUserStoreHook } from "@/store/modules/user";

const overview = ref<any>({});
const recent = ref<any[]>([]);
const defects = ref<any[]>([]);
const alarms = ref<any>({ byType: {}, byStatus: {}, top: [], total: 0 });
const workOrderStatus = ref<any[]>([]);
const trendRef = ref<HTMLDivElement>();
const orderRef = ref<HTMLDivElement>();
const statusRef = ref<HTMLDivElement>();
let charts: echarts.ECharts[] = [];

const statusLabels: Record<number, string> = { 0: "未下达", 1: "已下达", 2: "生产中", 3: "已完工", 4: "已关闭" };
const statusColors = ["#909399", "#409eff", "#e6a23c", "#67c23a", "#f56c6c"];

function initChart(el: HTMLDivElement | undefined, option: echarts.EChartsOption) {
  if (!el) return;
  const chart = echarts.init(el);
  chart.setOption(option);
  charts.push(chart);
}

async function load() {
  const [ov, rc, df, al, ws, tr, ot] = await Promise.all([
    http.request<any>("get", "/dashboard/overview"),
    http.request<any>("get", "/dashboard/recent-work-orders", { params: { limit: 6 } }),
    http.request<any>("get", "/dashboard/defect-rank"),
    http.request<any>("get", "/dashboard/equipment-alarm"),
    http.request<any>("get", "/dashboard/work-order-status"),
    http.request<any>("get", "/dashboard/production-trend", { params: { days: 7 } }),
    http.request<any>("get", "/dashboard/order-trend", { params: { days: 14 } })
  ]);
  overview.value = ov?.data ?? {};
  recent.value = rc?.data ?? [];
  defects.value = df?.data ?? [];
  alarms.value = al?.data ?? { byType: {}, byStatus: {}, top: [] };
  workOrderStatus.value = ws?.data ?? [];

  const trend = tr?.data ?? [];
  initChart(trendRef.value, {
    tooltip: { trigger: "axis" },
    legend: { data: ["报工数量", "合格数量"], right: 10 },
    grid: { left: 40, right: 20, top: 40, bottom: 30 },
    xAxis: { type: "category", data: trend.map((x: any) => x.date) },
    yAxis: { type: "value" },
    series: [
      { name: "报工数量", type: "bar", data: trend.map((x: any) => x.qty), itemStyle: { color: "#409eff", borderRadius: [4, 4, 0, 0] } },
      { name: "合格数量", type: "line", smooth: true, data: trend.map((x: any) => x.qualified), itemStyle: { color: "#67c23a" }, areaStyle: { opacity: 0.12 } }
    ]
  });

  const orders = ot?.data ?? [];
  initChart(orderRef.value, {
    tooltip: { trigger: "axis" },
    grid: { left: 50, right: 20, top: 30, bottom: 30 },
    xAxis: { type: "category", data: orders.map((x: any) => x.date) },
    yAxis: { type: "value" },
    series: [
      {
        name: "订单金额",
        type: "line",
        smooth: true,
        symbolSize: 6,
        data: orders.map((x: any) => x.amount),
        itemStyle: { color: "#e6a23c" },
        areaStyle: {
          color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
            { offset: 0, color: "rgba(230,162,60,.35)" },
            { offset: 1, color: "rgba(230,162,60,0)" }
          ])
        }
      }
    ]
  });

  initChart(statusRef.value, {
    tooltip: { trigger: "item" },
    legend: { bottom: 0 },
    series: [
      {
        type: "pie",
        radius: ["45%", "70%"],
        center: ["50%", "45%"],
        data: workOrderStatus.value.map((x: any) => ({
          name: statusLabels[x.status] ?? x.status,
          value: x.count,
          itemStyle: { color: statusColors[x.status] }
        })),
        label: { formatter: "{b}\n{c}" }
      }
    ]
  });
}

function money(v: number | undefined) {
  return Number(v ?? 0).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function resize() {
  charts.forEach(c => c.resize());
}

onMounted(() => {
  load();
  window.addEventListener("resize", resize);
});

onBeforeUnmount(() => {
  window.removeEventListener("resize", resize);
  charts.forEach(c => c.dispose());
  charts = [];
});
</script>

<template>
  <div class="dashboard">
    <el-card shadow="never" class="hero">
      <div class="hero-left">
        <div class="hello">您好，{{ useUserStoreHook().nickname || "管理员" }}</div>
        <div class="sub">今天是 {{ new Date().toLocaleDateString("zh-CN", { year: "numeric", month: "long", day: "numeric", weekday: "long" }) }}，祝您工作顺利</div>
      </div>
      <div class="hero-right">
        <div class="badge"><span>在制工单</span><b>{{ overview.inProgress ?? 0 }}</b></div>
        <div class="badge"><span>设备运行</span><b>{{ overview.equipmentRunning ?? 0 }}/{{ overview.equipmentTotal ?? 0 }}</b></div>
        <div class="badge"><span>库存总值</span><b>￥{{ money(overview.stockValue) }}</b></div>
      </div>
    </el-card>

    <el-row :gutter="12" class="stat-row">
      <el-col :xs="12" :sm="6">
        <el-card shadow="hover" class="stat">
          <div class="stat-title">今日工单</div>
          <div class="stat-value">{{ overview.todayWorkOrders ?? 0 }}</div>
          <div class="stat-foot">累计 {{ overview.totalWorkOrders ?? 0 }} 张</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6">
        <el-card shadow="hover" class="stat">
          <div class="stat-title">生产中工单</div>
          <div class="stat-value warning">{{ overview.inProgress ?? 0 }}</div>
          <div class="stat-foot">今日产量 {{ overview.todayOutput ?? 0 }}</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6">
        <el-card shadow="hover" class="stat">
          <div class="stat-title">今日完成率</div>
          <div class="stat-value success">{{ overview.completionRate ?? 0 }}%</div>
          <div class="stat-foot">今日良率 {{ overview.todayYield ?? 0 }}%</div>
        </el-card>
      </el-col>
      <el-col :xs="12" :sm="6">
        <el-card shadow="hover" class="stat">
          <div class="stat-title">设备利用率</div>
          <div class="stat-value primary">{{ overview.equipmentUtilization ?? 0 }}%</div>
          <div class="stat-foot">运行 {{ overview.equipmentRunning ?? 0 }} / {{ overview.equipmentTotal ?? 0 }} 台</div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="12">
      <el-col :xs="24" :lg="16">
        <el-card shadow="never" class="panel">
          <template #header><div class="panel-title">近 7 天生产趋势</div></template>
          <div ref="trendRef" class="chart" />
        </el-card>
      </el-col>
      <el-col :xs="24" :lg="8">
        <el-card shadow="never" class="panel">
          <template #header><div class="panel-title">工单状态分布</div></template>
          <div ref="statusRef" class="chart" />
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="12">
      <el-col :xs="24" :lg="8">
        <el-card shadow="never" class="panel">
          <template #header><div class="panel-title">工序不良率排名</div></template>
          <div v-if="defects.length === 0" class="empty">暂无不良数据</div>
          <div v-for="(d, i) in defects" :key="d.id" class="rank-item">
            <span class="rank-index">{{ i + 1 }}</span>
            <span class="rank-name">{{ d.name }}</span>
            <el-progress :percentage="d.percent" :stroke-width="8" :show-text="false" style="flex: 1; margin: 0 10px" />
            <span class="rank-percent">{{ d.percent }}%</span>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :lg="8">
        <el-card shadow="never" class="panel">
          <template #header><div class="panel-title">设备报警</div></template>
          <div class="alarm-summary">
            <div><span>未恢复</span><b class="danger">{{ alarms.byStatus?.active ?? 0 }}</b></div>
            <div><span>已恢复</span><b class="success">{{ alarms.byStatus?.recovered ?? 0 }}</b></div>
            <div><span>已确认</span><b>{{ alarms.byStatus?.confirmed ?? 0 }}</b></div>
            <div><span>已关闭</span><b>{{ alarms.byStatus?.closed ?? 0 }}</b></div>
          </div>
          <el-divider content-position="left">高发设备</el-divider>
          <div v-for="t in alarms.top" :key="t.equipmentId" class="top-item">
            <span>{{ t.name }}</span>
            <el-tag type="danger" size="small">{{ t.count }} 次</el-tag>
          </div>
          <div v-if="!alarms.top?.length" class="empty">暂无报警</div>
        </el-card>
      </el-col>
      <el-col :xs="24" :lg="8">
        <el-card shadow="never" class="panel">
          <template #header><div class="panel-title">近期生产工单</div></template>
          <div v-for="wo in recent" :key="wo.id" class="wo-item">
            <div class="wo-code">{{ wo.code }}</div>
            <div class="wo-name">{{ wo.productName }} <span class="muted">{{ wo.spec }}</span></div>
            <div class="wo-foot">
              <el-tag :type="wo.workOrderStatus === 3 ? 'success' : wo.workOrderStatus === 2 ? 'warning' : 'info'" size="small">
                {{ statusLabels[wo.workOrderStatus] }}
              </el-tag>
              <span>{{ wo.completedQty }} / {{ wo.qty }}</span>
            </div>
            <el-progress :percentage="wo.qty ? Math.min(100, Math.round((wo.completedQty / wo.qty) * 100)) : 0" :stroke-width="6" :show-text="false" />
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="panel">
      <template #header><div class="panel-title">近 14 天订单金额</div></template>
      <div ref="orderRef" class="chart" style="height: 260px" />
    </el-card>
  </div>
</template>

<style scoped lang="scss">
.dashboard {
  .hero {
    margin-bottom: 12px;
    background: linear-gradient(120deg, #1f6feb 0%, #3b8cff 55%, #64a8ff 100%);
    color: #fff;
    border: none;
    :deep(.el-card__body) {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 16px;
    }
    .hello {
      font-size: 22px;
      font-weight: 700;
    }
    .sub {
      margin-top: 8px;
      opacity: 0.88;
      font-size: 13px;
    }
    .hero-right {
      display: flex;
      gap: 28px;
      flex-wrap: wrap;
    }
    .badge {
      display: flex;
      flex-direction: column;
      span {
        font-size: 12px;
        opacity: 0.8;
      }
      b {
        font-size: 20px;
        margin-top: 2px;
      }
    }
  }
  .stat-row {
    margin-bottom: 12px;
    .stat {
      .stat-title {
        color: var(--el-text-color-secondary);
        font-size: 13px;
      }
      .stat-value {
        font-size: 26px;
        font-weight: 700;
        margin: 6px 0;
        &.warning { color: #e6a23c; }
        &.success { color: #67c23a; }
        &.primary { color: #409eff; }
      }
      .stat-foot {
        font-size: 12px;
        color: var(--el-text-color-secondary);
      }
    }
  }
  .panel {
    margin-bottom: 12px;
    .panel-title {
      font-weight: 600;
    }
    .chart {
      height: 300px;
    }
  }
  .rank-item,
  .top-item,
  .wo-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 0;
  }
  .rank-index {
    width: 20px;
    height: 20px;
    border-radius: 50%;
    background: var(--el-fill-color);
    font-size: 12px;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .rank-name {
    width: 130px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .rank-percent {
    font-size: 12px;
    color: var(--el-text-color-secondary);
  }
  .alarm-summary {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 10px;
    div {
      background: var(--el-fill-color-lighter);
      border-radius: 6px;
      padding: 8px 12px;
      display: flex;
      justify-content: space-between;
      span { color: var(--el-text-color-secondary); font-size: 13px; }
      b.danger { color: #f56c6c; }
      b.success { color: #67c23a; }
    }
  }
  .top-item {
    justify-content: space-between;
  }
  .wo-item {
    flex-wrap: wrap;
    .wo-code {
      width: 100%;
      font-size: 12px;
      color: var(--el-text-color-secondary);
    }
    .wo-name {
      flex: 1;
      font-weight: 500;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .wo-foot {
      width: 100%;
      display: flex;
      justify-content: space-between;
      font-size: 12px;
      color: var(--el-text-color-secondary);
    }
    :deep(.el-progress) {
      width: 100%;
    }
  }
  .empty {
    color: var(--el-text-color-secondary);
    text-align: center;
    padding: 24px 0;
  }
  .muted {
    color: var(--el-text-color-secondary);
    font-weight: 400;
    font-size: 12px;
  }
}
</style>
