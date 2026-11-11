<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { http } from "@/utils/http";
import { enableDict, maintainTypeDict, toOptions } from "@/utils/biz";

const equipmentOptions = ref<Array<{ label: string; value: number }>>([]);
const userOptions = ref<Array<{ label: string; value: number }>>([]);
const localToday = (() => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
})();
const columns: CrudColumn[] = [
  { prop: "code", label: "计划编号", width: 145 },
  { prop: "name", label: "计划名称", minWidth: 150 },
  { prop: "equipmentId", label: "设备ID", width: 95, align: "right" },
  { prop: "maintenanceType", label: "计划类型", width: 105, dict: maintainTypeDict },
  { prop: "cycleDays", label: "周期(天)", width: 100, align: "right" },
  { prop: "nextDate", label: "下次执行日期", width: 135, date: true },
  { prop: "ownerId", label: "负责人ID", width: 100, align: "right" },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
];
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "计划编号", placeholder: "留空自动生成" },
  { prop: "name", label: "计划名称", required: true },
  { prop: "equipmentId", label: "设备", type: "select", options: equipmentOptions.value, required: true },
  { prop: "maintenanceType", label: "计划类型", type: "select", options: toOptions(maintainTypeDict), required: true },
  { prop: "cycleDays", label: "执行周期(天)", type: "number", precision: 0, min: 1, required: true },
  { prop: "nextDate", label: "下次执行日期", type: "date", required: true },
  { prop: "ownerId", label: "负责人", type: "select", options: userOptions.value },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict) },
  { prop: "items", label: "点检/保养项目", type: "textarea", span: 24 },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);

onMounted(async () => {
  const [equipmentRes, userRes]: any[] = await Promise.all([
    http.request("get", "/equipment/options"),
    http.request("get", "/user/options")
  ]);
  if (equipmentRes?.code === 0) equipmentOptions.value = (equipmentRes.data ?? []).map((x: any) => ({ label: `${x.code ? `${x.code} ` : ""}${x.name}`, value: x.id }));
  if (userRes?.code === 0) userOptions.value = (userRes.data ?? []).map((x: any) => ({ label: x.name || x.code || String(x.id), value: x.id }));
});
</script>

<template>
  <CrudTable
    api="/maintenance-plan"
    title="点检保养计划"
    :columns="columns"
    :form-items="formItems"
    :default-form="{ maintenanceType: 1, cycleDays: 30, nextDate: localToday, status: 1 }"
    :actions-width="170"
  />
</template>
