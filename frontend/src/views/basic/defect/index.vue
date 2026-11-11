<script setup lang="ts">
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { enableDict, toOptions } from "@/utils/biz";

const columns: CrudColumn[] = [
  { prop: "code", label: "不良项编码", width: 135 },
  { prop: "name", label: "不良项名称", minWidth: 170 },
  { prop: "category", label: "不良类别", width: 130 },
  { prop: "description", label: "不良描述", minWidth: 200 },
  { prop: "disposal", label: "处理方式", minWidth: 180 },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
];
const formItems: CrudField[] = [
  { prop: "code", label: "不良项编码", required: true },
  { prop: "name", label: "不良项名称", required: true },
  { prop: "category", label: "不良类别" },
  { prop: "description", label: "不良描述", type: "textarea", span: 24 },
  { prop: "disposal", label: "处理方式", type: "textarea", span: 24 },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
];
</script>

<template>
  <CrudTable api="/defect-item" title="不良项管理" :columns="columns" :form-items="formItems" :default-form="{ status: 1 }" />
</template>
