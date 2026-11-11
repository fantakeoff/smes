<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from "vue";
import * as echarts from "echarts";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { useLookupStore } from "@/store/modules/lookup";

const lookup = useLookupStore();
const loading = ref(false);
const dateRange = ref<string[]>([]);
const reports = ref<any[]>([]);
const workOrders = ref<any[]>([]);
const chartRef = ref<HTMLDivElement>();
let chart: echarts.ECharts | null = null;

function formatDate(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}
function initialRange() {
  const end = new Date();
  const start = new Date();
  start.setDate(end.getDate() - 6);
  return [formatDate(start), formatDate(end)];
}
function dayKey(value: any) {
  return value ? String(value).slice(0, 10) : "";
}
function quantity(value: any) {
  return Number(value ?? 0).toLocaleString("zh-CN", { maximumFractionDigits: 2 });
}

const filteredReports = computed(() => {
  const [start, end] = dateRange.value;
  if (!start || !end) return [];
  return reports.value.filter(row => {
    const day = dayKey(row.reportTime);
    return day >= start && day <= end;
  });
});
const totalQty = computed(() => filteredReports.value.reduce((sum, row) => sum + Number(row.qty ?? 0), 0));
const qualifiedQty = computed(() => filteredReports.value.reduce((sum, row) => sum + Number(row.qualifiedQty ?? 0), 0));
const defectQty = computed(() => filteredReports.value.reduce((sum, row) => sum + Number(row.defectQty ?? 0), 0));
const yieldRate = computed(() => totalQty.value > 0 ? qualifiedQty.value / totalQty.value * 100 : 0);

function dateList() {
  const [start, end] = dateRange.value;
  if (!start || !end) return [];
  const current = new Date(`${start}T00:00:00`);
  const last = new Date(`${end}T00:00:00`);
  const days: string[] = [];
  while (current <= last) {
    days.push(formatDate(current));
    current.setDate(current.getDate() + 1);
  }
  return days;
}
const dailyRows = computed(() => {
  const grouped = new Map<string, { qty: number; qualifiedQty: number }>();
  for (const row of filteredReports.value) {
    const day = dayKey(row.reportTime);
    const item = grouped.get(day) ?? { qty: 0, qualifiedQty: 0 };
    item.qty += Number(row.qty ?? 0);
    item.qualifiedQty += Number(row.qualifiedQty ?? 0);
    grouped.set(day, item);
  }
  return dateList().map(date => ({ date, ...(grouped.get(date) ?? { qty: 0, qualifiedQty: 0 }) }));
});

function aggregateBy(kind: "workshop" | "product") {
  const workOrderMap = new Map(workOrders.value.map(row => [Number(row.id), row]));
  const grouped = new Map<string, any>();
  for (const report of filteredReports.value) {
    const workOrder = workOrderMap.get(Number(report.workOrderId));
    const id = kind === "workshop" ? report.workshopId : workOrder?.productId;
    const key = id == null ? "unknown" : String(id);
    let name = "未分配";
    if (kind === "workshop") name = id == null ? "未分配车间" : (lookup.name("workshops", Number(id)) || `车间 ${id}`);
    else name = id == null ? "未知产品" : (lookup.name("products", Number(id)) || workOrder?.name || `产品 ${id}`);
    const item = grouped.get(key) ?? { key, name, qty: 0, qualifiedQty: 0, defectQty: 0 };
    item.qty += Number(report.qty ?? 0);
    item.qualifiedQty += Number(report.qualifiedQty ?? 0);
    item.defectQty += Number(report.defectQty ?? 0);
    grouped.set(key, item);
  }
  return [...grouped.values()].map(item => ({
    ...item,
    yield: item.qty > 0 ? item.qualifiedQty / item.qty * 100 : 0
  })).sort((a, b) => b.qty - a.qty);
}
const workshopRows = computed(() => aggregateBy("workshop"));
const productRows = computed(() => aggregateBy("product"));

function renderChart() {
  if (!chartRef.value) return;
  chart = chart ?? echarts.getInstanceByDom(chartRef.value) ?? echarts.init(chartRef.value);
  const rows = dailyRows.value;
  chart.setOption({
    color: ["#4388e8", "#41b883"],
    tooltip: { trigger: "axis" },
    legend: { data: ["报工产量", "合格数量"], right: 12, top: 0 },
    grid: { left: 48, right: 24, top: 42, bottom: 36 },
    xAxis: { type: "category", data: rows.map(row => row.date.slice(5)), axisLabel: { color: "#7b8797" }, axisLine: { lineStyle: { color: "#dfe5ed" } } },
    yAxis: { type: "value", axisLabel: { color: "#7b8797" }, splitLine: { lineStyle: { color: "#edf0f4" } } },
    series: [
      { name: "报工产量", type: "bar", barMaxWidth: 30, data: rows.map(row => Number(row.qty.toFixed(2))), itemStyle: { borderRadius: [4, 4, 0, 0] } },
      { name: "合格数量", type: "line", smooth: true, symbolSize: 7, data: rows.map(row => Number(row.qualifiedQty.toFixed(2))), areaStyle: { opacity: 0.08 } }
    ]
  }, true);
}

async function load() {
  if (dateRange.value.length !== 2) return message("请选择完整的日期范围", { type: "warning" });
  loading.value = true;
  try {
    const [reportRes, workOrderRes]: any[] = await Promise.all([
      http.request("get", "/work-report", { params: {
        // WorkReportController 当前用 PageQuery 对象参数，ASP.NET 需显式对象前缀才能绑定分页字段。
        "page.Page": 1, "page.PageSize": 100,
        "page.StartDate": dateRange.value[0], "page.EndDate": dateRange.value[1],
        startDate: dateRange.value[0], endDate: dateRange.value[1]
      } }),
      http.request("get", "/work-order", { params: { "page.Page": 1, "page.PageSize": 100 } })
    ]);
    if (reportRes?.code !== 0) message(reportRes?.message || "报工数据加载失败", { type: "error" });
    else reports.value = reportRes.data?.list ?? [];
    if (workOrderRes?.code !== 0) message(workOrderRes?.message || "工单信息加载失败，产品汇总可能不完整", { type: "warning" });
    else workOrders.value = workOrderRes.data?.list ?? [];
    await nextTick();
    renderChart();
  } catch (error: any) {
    message(error?.message || "生产报表加载失败", { type: "error" });
  } finally {
    loading.value = false;
  }
}

function exportCsv() {
  const workOrderMap = new Map(workOrders.value.map(row => [Number(row.id), row]));
  const rows = [
    ["报工单号", "报工日期", "工单编号", "车间", "产品", "报工数量", "合格数量", "不良数量"],
    ...filteredReports.value.map(report => {
      const order = workOrderMap.get(Number(report.workOrderId));
      return [
        report.code ?? "", dayKey(report.reportTime), order?.code ?? report.workOrderId ?? "",
        lookup.name("workshops", Number(report.workshopId)) || "未分配车间",
        lookup.name("products", Number(order?.productId)) || order?.name || "未知产品",
        report.qty ?? 0, report.qualifiedQty ?? 0, report.defectQty ?? 0
      ];
    })
  ];
  const csv = "\uFEFF" + rows.map(row => row.map(value => `"${String(value ?? "").replace(/"/g, '""')}"`).join(",")).join("\r\n");
  const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = `生产报表_${dateRange.value[0]}_${dateRange.value[1]}.csv`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
function resize() { chart?.resize(); }

onMounted(async () => {
  dateRange.value = initialRange();
  await lookup.load();
  await load();
  window.addEventListener("resize", resize);
});
onBeforeUnmount(() => {
  window.removeEventListener("resize", resize);
  chart?.dispose();
  chart = null;
});
</script>

<template>
  <div class="production-report">
    <el-card shadow="never" class="filter-card">
      <div class="filter-bar">
        <div class="filter-label"><strong>生产数据筛选</strong><span>按报工日期统计生产数据</span></div>
        <div class="filter-actions">
          <el-date-picker v-model="dateRange" type="daterange" value-format="YYYY-MM-DD" range-separator="至" start-placeholder="开始日期" end-placeholder="结束日期" :clearable="false" />
          <el-button type="primary" :loading="loading" @click="load">查询</el-button>
          <el-button :disabled="filteredReports.length === 0" @click="exportCsv">导出 CSV</el-button>
        </div>
      </div>
    </el-card>

    <el-row :gutter="12" class="kpi-row">
      <el-col :xs="24" :sm="8"><el-card shadow="never" class="kpi"><div class="kpi-label">报工产量</div><div class="kpi-value">{{ quantity(totalQty) }} <small>件</small></div><div class="kpi-note">{{ dateRange[0] }} 至 {{ dateRange[1] }}</div></el-card></el-col>
      <el-col :xs="24" :sm="8"><el-card shadow="never" class="kpi"><div class="kpi-label">生产良率</div><div class="kpi-value green">{{ yieldRate.toFixed(1) }}<small>%</small></div><div class="kpi-note">合格数量 {{ quantity(qualifiedQty) }}</div></el-card></el-col>
      <el-col :xs="24" :sm="8"><el-card shadow="never" class="kpi"><div class="kpi-label">不良数量</div><div class="kpi-value red">{{ quantity(defectQty) }} <small>件</small></div><div class="kpi-note">共 {{ filteredReports.length }} 条报工记录</div></el-card></el-col>
    </el-row>

    <el-card shadow="never" class="panel" v-loading="loading">
      <template #header><div class="panel-title"><strong>日产量趋势</strong><span>按报工日期汇总</span></div></template>
      <div ref="chartRef" class="trend-chart" />
      <el-empty v-if="!loading && filteredReports.length === 0" description="所选日期范围内暂无报工数据" />
    </el-card>

    <el-row :gutter="12" class="summary-row">
      <el-col :xs="24" :lg="12">
        <el-card shadow="never" class="panel" v-loading="loading">
          <template #header><div class="panel-title"><strong>按车间汇总</strong><span>{{ workshopRows.length }} 个车间</span></div></template>
          <el-table :data="workshopRows" border stripe size="small">
            <el-table-column prop="name" label="车间" min-width="150" />
            <el-table-column label="产量" width="100" align="right"><template #default="{ row }">{{ quantity(row.qty) }}</template></el-table-column>
            <el-table-column label="合格数" width="100" align="right"><template #default="{ row }">{{ quantity(row.qualifiedQty) }}</template></el-table-column>
            <el-table-column label="不良数" width="95" align="right"><template #default="{ row }">{{ quantity(row.defectQty) }}</template></el-table-column>
            <el-table-column label="良率" width="90" align="right"><template #default="{ row }">{{ row.yield.toFixed(1) }}%</template></el-table-column>
          </el-table>
          <el-empty v-if="!loading && workshopRows.length === 0" description="暂无汇总数据" :image-size="72" />
        </el-card>
      </el-col>
      <el-col :xs="24" :lg="12">
        <el-card shadow="never" class="panel" v-loading="loading">
          <template #header><div class="panel-title"><strong>按产品汇总</strong><span>{{ productRows.length }} 个产品</span></div></template>
          <el-table :data="productRows" border stripe size="small">
            <el-table-column prop="name" label="产品" min-width="150" show-overflow-tooltip />
            <el-table-column label="产量" width="100" align="right"><template #default="{ row }">{{ quantity(row.qty) }}</template></el-table-column>
            <el-table-column label="合格数" width="100" align="right"><template #default="{ row }">{{ quantity(row.qualifiedQty) }}</template></el-table-column>
            <el-table-column label="不良数" width="95" align="right"><template #default="{ row }">{{ quantity(row.defectQty) }}</template></el-table-column>
            <el-table-column label="良率" width="90" align="right"><template #default="{ row }">{{ row.yield.toFixed(1) }}%</template></el-table-column>
          </el-table>
          <el-empty v-if="!loading && productRows.length === 0" description="暂无汇总数据" :image-size="72" />
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<style scoped lang="scss">
.production-report {
  .filter-card { margin-bottom: 12px; }
  .filter-bar { display: flex; align-items: center; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
  .filter-label { display: flex; flex-direction: column; gap: 5px; strong { font-size: 15px; } span { color: var(--el-text-color-secondary); font-size: 12px; } }
  .filter-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .kpi-row { margin-bottom: 12px; }
  .kpi { height: 100%; .kpi-label { color: var(--el-text-color-secondary); font-size: 13px; } .kpi-value { margin: 9px 0 5px; font-size: 27px; line-height: 1.15; font-weight: 700; color: #3479d7; small { font-size: 13px; font-weight: 400; } &.green { color: #2da779; } &.red { color: #e15d62; } } .kpi-note { color: #9099a7; font-size: 12px; } }
  .panel { margin-bottom: 12px; }
  .panel-title { display: flex; align-items: baseline; justify-content: space-between; gap: 10px; strong { font-size: 15px; } span { color: var(--el-text-color-secondary); font-size: 12px; } }
  .trend-chart { height: 320px; }
  .summary-row { margin-top: 0; }
  @media (max-width: 768px) { .filter-bar { align-items: stretch; flex-direction: column; } .filter-actions :deep(.el-date-editor) { width: 100%; } .trend-chart { height: 260px; } }
}
</style>
