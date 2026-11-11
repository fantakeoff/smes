<script setup lang="ts">
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { enableDict, toOptions } from "@/utils/biz";

const supplierTypeDict = {
  1: { label: "原材料", type: "primary" },
  2: { label: "外协", type: "warning" },
  3: { label: "设备", type: "success" },
  4: { label: "服务", type: "info" }
};
const levelDict = {
  1: { label: "普通", type: "info" },
  2: { label: "重要", type: "warning" },
  3: { label: "核心", type: "success" }
};
const supplierTypeOptions = Object.entries(supplierTypeDict).map(([value, item]) => ({ label: item.label, value: Number(value) }));
const levelOptions = Object.entries(levelDict).map(([value, item]) => ({ label: item.label, value: Number(value) }));
const columns: CrudColumn[] = [
  { prop: "code", label: "供应商编码", width: 135 },
  { prop: "name", label: "供应商名称", minWidth: 180 },
  { prop: "shortName", label: "简称", width: 130 },
  { prop: "supplierType", label: "供应商类型", width: 110, dict: supplierTypeDict },
  { prop: "contact", label: "联系人", width: 110 },
  { prop: "phone", label: "联系电话", width: 135 },
  { prop: "level", label: "供应等级", width: 100, dict: levelDict },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
];
const formItems: CrudField[] = [
  { prop: "code", label: "供应商编码", required: true },
  { prop: "name", label: "供应商名称", required: true },
  { prop: "shortName", label: "供应商简称" },
  { prop: "supplierType", label: "供应商类型", type: "select", options: supplierTypeOptions, required: true },
  { prop: "level", label: "供应等级", type: "select", options: levelOptions },
  { prop: "contact", label: "联系人" },
  { prop: "phone", label: "联系电话" },
  { prop: "email", label: "电子邮箱" },
  { prop: "taxNo", label: "纳税人识别号" },
  { prop: "address", label: "联系地址", span: 24 },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
];
const searchFields: CrudField[] = [{ prop: "supplierType", label: "供应商类型", type: "select", options: supplierTypeOptions }];
</script>

<template>
  <CrudTable api="/supplier" title="供应商管理" :columns="columns" :form-items="formItems" :search-fields="searchFields" :default-form="{ supplierType: 1, level: 1, status: 1 }" :actions-width="160" />
</template>
