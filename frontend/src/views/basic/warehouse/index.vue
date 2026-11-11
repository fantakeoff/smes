<script setup lang="ts">
import { computed, onMounted } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { useLookupStore } from "@/store/modules/lookup";
import { enableDict, toOptions, warehouseTypeDict } from "@/utils/biz";

const lookup = useLookupStore();
onMounted(() => lookup.load());
const managerOptions = computed(() => lookup.options("users"));
const managerDict = computed(() => Object.fromEntries(lookup.list("users").map(user => [String(user.id), { label: user.name ?? user.code ?? String(user.id) }])));
const defaultDict = { true: { label: "是", type: "success" }, false: { label: "否", type: "info" } };
const columns = computed<CrudColumn[]>(() => [
  { prop: "code", label: "仓库编码", width: 130 },
  { prop: "name", label: "仓库名称", minWidth: 150 },
  { prop: "type", label: "仓库类型", width: 110, dict: warehouseTypeDict },
  { prop: "location", label: "库位/地址", minWidth: 150 },
  { prop: "managerId", label: "负责人", width: 120, dict: managerDict.value },
  { prop: "isDefault", label: "默认仓库", width: 100, dict: defaultDict },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
]);
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "仓库编码", required: true },
  { prop: "name", label: "仓库名称", required: true },
  { prop: "type", label: "仓库类型", type: "select", options: toOptions(warehouseTypeDict), required: true },
  { prop: "location", label: "库位/地址" },
  { prop: "managerId", label: "负责人", type: "select", options: managerOptions.value },
  { prop: "isDefault", label: "设为默认", type: "switch" },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);
</script>

<template>
  <CrudTable api="/warehouse" title="仓库管理" :columns="columns" :form-items="formItems" :default-form="{ type: 1, isDefault: false, status: 1 }" />
</template>
