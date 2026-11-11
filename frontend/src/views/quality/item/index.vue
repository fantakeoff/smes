<script setup lang="ts">
import { computed } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { enableDict, toOptions } from "@/utils/biz";

const itemTypeDict = {
  1: { label: "定量", type: "primary" as const },
  2: { label: "定性", type: "warning" as const },
  3: { label: "外观", type: "info" as const }
};

const columns: CrudColumn[] = [
  { prop: "code", label: "项目编码", width: 120 },
  { prop: "name", label: "检验项目", minWidth: 150 },
  { prop: "itemType", label: "类型", width: 90, dict: itemTypeDict },
  { prop: "method", label: "检验方法", width: 130 },
  { prop: "standard", label: "标准要求", minWidth: 170 },
  { prop: "unit", label: "单位", width: 80 },
  { prop: "instrument", label: "检测仪器", width: 130 },
  { prop: "status", label: "状态", width: 80, dict: enableDict }
];

const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "项目编码", required: true },
  { prop: "name", label: "检验项目", required: true },
  { prop: "itemType", label: "项目类型", type: "select", options: toOptions(itemTypeDict), required: true },
  { prop: "unit", label: "单位" },
  { prop: "method", label: "检验方法" },
  { prop: "instrument", label: "检测仪器" },
  { prop: "standard", label: "标准要求", span: 24 },
  { prop: "minValue", label: "下限值", type: "number", precision: 3 },
  { prop: "maxValue", label: "上限值", type: "number", precision: 3 },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict) },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);
</script>

<template>
  <CrudTable
    api="/quality-item"
    title="检验项目"
    :columns="columns"
    :form-items="formItems"
    :default-form="{ status: 1, itemType: 1 }"
  />
</template>
