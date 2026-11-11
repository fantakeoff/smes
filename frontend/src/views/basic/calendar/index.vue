<script setup lang="ts">
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";

const dayTypeDict = {
  1: { label: "工作日", type: "success" },
  2: { label: "休息日", type: "info" },
  3: { label: "节假日", type: "warning" }
};
const dayTypeOptions = Object.entries(dayTypeDict).map(([value, item]) => ({ label: item.label, value: Number(value) }));
const columns: CrudColumn[] = [
  { prop: "date", label: "日期", width: 150, date: true },
  { prop: "dayType", label: "日期类型", width: 120, dict: dayTypeDict },
  { prop: "shift", label: "班次", width: 140 },
  { prop: "name", label: "日历说明", minWidth: 180 },
  { prop: "remark", label: "备注", minWidth: 200 }
];
const formItems: CrudField[] = [
  { prop: "date", label: "日期", type: "date", required: true },
  { prop: "dayType", label: "日期类型", type: "select", options: dayTypeOptions, required: true },
  { prop: "shift", label: "班次", placeholder: "例如：白班、夜班" },
  { prop: "name", label: "日历说明", placeholder: "例如：春节、调休" },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
];
const searchFields: CrudField[] = [{ prop: "date", label: "按日期筛选", type: "date" }];
</script>

<template>
  <CrudTable api="/work-calendar" title="工厂日历" :columns="columns" :form-items="formItems" :search-fields="searchFields" :default-form="{ dayType: 1 }" :actions-width="160" />
</template>
