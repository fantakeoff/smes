<script setup lang="ts">
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { enableDict, toOptions } from "@/utils/biz";

const columns: CrudColumn[] = [
  { prop: "code", label: "品牌编码", width: 140 },
  { prop: "name", label: "品牌名称", minWidth: 160 },
  { prop: "logo", label: "品牌标识", minWidth: 180 },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
];
const formItems: CrudField[] = [
  { prop: "code", label: "品牌编码", placeholder: "请输入品牌编码" },
  { prop: "name", label: "品牌名称", required: true },
  { prop: "logo", label: "品牌标识", span: 24 },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
];
</script>

<template>
  <CrudTable api="/product-brand" title="品牌管理" :columns="columns" :form-items="formItems" :default-form="{ status: 1 }" />
</template>
