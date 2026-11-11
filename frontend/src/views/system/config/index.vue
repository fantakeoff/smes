<script setup lang="ts">
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";

const columns: CrudColumn[] = [
  { prop: "code", label: "参数键", minWidth: 170 },
  { prop: "name", label: "参数名称", minWidth: 170 },
  { prop: "value", label: "参数值", minWidth: 220, showOverflow: true },
  { prop: "group", label: "参数分组", width: 140 },
  { prop: "description", label: "说明", minWidth: 180 },
  { prop: "status", label: "状态", width: 90, dict: { 1: { label: "启用", type: "success" }, 0: { label: "停用", type: "info" } } }
];
const formItems: CrudField[] = [
  { prop: "code", label: "参数键", required: true, placeholder: "唯一配置键" },
  { prop: "name", label: "参数名称", required: true },
  { prop: "group", label: "参数分组", placeholder: "如 system / upload" },
  { prop: "status", label: "状态", type: "select", options: [{ label: "启用", value: 1 }, { label: "停用", value: 0 }] },
  { prop: "value", label: "参数值", type: "textarea", required: true, span: 24 },
  { prop: "description", label: "说明", type: "textarea", span: 24 },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
];
const defaultForm = { status: 1, group: "" };
</script>

<template>
  <CrudTable
    api="/config"
    title="系统参数"
    :columns="columns"
    :form-items="formItems"
    :default-form="defaultForm"
    form-width="800px"
  />
</template>
