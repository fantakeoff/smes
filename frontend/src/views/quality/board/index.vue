<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from "vue";
import * as echarts from "echarts";
import { http } from "@/utils/http";
import { useLookupStore } from "@/store/modules/lookup";
import { inspectResultDict, inspectTypeDict } from "@/utils/biz";

const lookup = useLookupStore();
const summary = ref<any>({});
const defects = ref<any[]>([]);
const latest = ref<any[]>([]);
const typeRef = ref<HTMLDivElement>();
const defectRef = ref<HTMLDivElement>();
let charts: echarts.ECharts[] = [];

async function load() {
  const [s, d, l] = await Promise.all([
    http.request<any>("get", "/dashboard/quality-summary"),
    http.request<any>("get", "/dashboard/defect-rank", { params: { days: 30 } }),
    http.request<any>("get", "/quality-inspection", { params: { page: 1, pageSize: 8 } })
  ]);
  summary.value = s?.data ?? {};
  defects.value = d?.data ?? [];
  latest.value = l?.data?.list ?? [];

  if (typeRef.value) {
    const chart = echarts.init(typeRef.value);
    const byType = summary.value.byType ?? [];
    chart.setOption({
      tooltip: { trigger: "axis" },
      legend: { data: ["检验数", "合格数"], right: 10 },
      grid: { left: 50, right: 20, top: 40, bottom: 30 },
      xAxis: { type: "category", data: byType.map((x: any) => x.type) },
      yAxis: { type: "value" },
      series: [
        { name: "检验数", type: "bar", barWidth: 26, itemStyle: { color: "#409eff", borderRadius: [4, 4, 0, 0] }, data: byType.map((x: any) => x.count) },
        { name: "合格数", type: "bar", barWidth: 26, itemStyle: { color: "#67c23a", borderRadius: [4, 4, 0, 0] }, data: byType.map((x: any) => x.passed) }
      ]
    });
    charts.push(chart);
  }

  if (defectRef.value) {
    const chart = echarts.init(defectRef.value);
    chart.setOption({
      tooltip: { trigger: "item" },
      legend: { bottom: 0 },
      series: [
        {
          type: "pie",
          radius: ["40%", "68%"],
          center: ["50%", "44%"],
          data: defects.value.map((x: any) => ({ name: x.name, value: x.qty })),
          label: { formatter: "{b}\n{c}" }
        }
      ]
    });
    charts.push(chart);
  }
}

function resize() {
  charts.forEach(c => c.resize());
}

function resultTag(r: number) {
  return (inspectResultDict[r]?.type as any) || "info";
}

onMounted(async () => {
  await lookup.load();
  await load();
  window.addEventListener("resize", resize);
});

onBeforeUnmount(() => {
  window.removeEventListener("resize", resize);
  charts.forEach(c => c.dispose());
  charts = [];
});
</script>

<template>
  <div class="quality-board">
    <el-row :gutter="12">
      <el-col :xs="12" :sm="6">
        <el-card shadow="hover" class="stat"><div class="t">检验总数</div><div class="v">{{ summary.total ?? 0 }}</div></el-card>
      </el-col>
      <el-col :xs="12" :sm="6">
        <el-card shadow="hover" class="stat"><div class="t">合格</div><div class="v success">{{ summary.passed ?? 0 }}</div></el-card>
      </el-col>
      <el-col :xs="12" :sm="6">
        <el-card shadow="hover" class="stat"><div class="t">不合格</div><div class="v danger">{{ summary.failed ?? 0 }}</div></el-card>
      </el-col>
      <el-col :xs="12" :sm="6">
        <el-card shadow="hover" class="stat"><div class="t">合格率</div><div class="v primary">{{ summary.passRate ?? 0 }}%</div></el-card>
      </el-col>
    </el-row>

    <el-row :gutter="12">
      <el-col :xs="24" :lg="14">
        <el-card shadow="never" class="panel">
          <template #header>各类检验完成情况</template>
          <div ref="typeRef" class="chart" />
        </el-card>
      </el-col>
      <el-col :xs="24" :lg="10">
        <el-card shadow="never" class="panel">
          <template #header>不良项分布（近 30 天）</template>
          <div ref="defectRef" class="chart" />
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="panel">
      <template #header>最近检验</template>
      <el-table :data="latest" border stripe size="small">
        <el-table-column prop="code" label="检验单号" width="150" />
        <el-table-column label="类型" width="100">
          <template #default="{ row }">{{ inspectTypeDict[row.inspectionType]?.label }}</template>
        </el-table-column>
        <el-table-column label="产品" min-width="160">
          <template #default="{ row }">{{ lookup.name("products", row.productId) }}</template>
        </el-table-column>
        <el-table-column prop="batchNo" label="批次" width="110" />
        <el-table-column prop="qty" label="送检数" width="90" align="right" />
        <el-table-column prop="defectQty" label="不良数" width="90" align="right" />
        <el-table-column label="结果" width="100">
          <template #default="{ row }">
            <el-tag size="small" :type="resultTag(row.result)">{{ inspectResultDict[row.result]?.label }}</el-tag>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<style scoped lang="scss">
.quality-board {
  .stat {
    .t { color: var(--el-text-color-secondary); font-size: 13px; }
    .v { font-size: 26px; font-weight: 700; margin-top: 6px; }
    .success { color: #67c23a; }
    .danger { color: #f56c6c; }
    .primary { color: #409eff; }
  }
  .panel {
    margin-top: 12px;
    .chart { height: 300px; }
  }
}
</style>
