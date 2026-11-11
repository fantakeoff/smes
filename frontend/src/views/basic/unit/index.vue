<script setup lang="ts">
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { enableDict, toOptions } from "@/utils/biz";

const unitTypeDict = {
  1: { label: "计数", type: "primary" },
  2: { label: "重量", type: "success" },
  3: { label: "长度", type: "warning" },
  4: { label: "时间", type: "info" }
};
const unitTypeOptions = Object.entries(unitTypeDict).map(([value, item]) => ({ label: item.label, value: Number(value) }));
const columns: CrudColumn[] = [
  { prop: "code", label: "单位编码", width: 130 },
  { prop: "name", label: "单位名称", minWidth: 140 },
  { prop: "unitType", label: "单位类别", width: 110, dict: unitTypeDict },
  { prop: "precision", label: "小数位数", width: 100, align: "right" },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
];
const formItems: CrudField[] = [
  { prop: "code", label: "单位编码", required: true },
  { prop: "name", label: "单位名称", required: true },
  { prop: "unitType", label: "单位类别", type: "select", options: unitTypeOptions, required: true },
  { prop: "precision", label: "小数位数", type: "number", precision: 0, min: 0, max: 6 },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
];
</script>

<template>
  <CrudTable api="/unit" title="计量单位" :columns="columns" :form-items="formItems" :default-form="{ unitType: 1, precision: 0, status: 1 }" />
</template>
