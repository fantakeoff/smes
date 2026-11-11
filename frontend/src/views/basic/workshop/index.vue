<script setup lang="ts">
import { computed, onMounted } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { useLookupStore } from "@/store/modules/lookup";
import { enableDict, toOptions } from "@/utils/biz";

const lookup = useLookupStore();
onMounted(() => lookup.load());
const workshopTypeDict = {
  1: { label: "车间", type: "primary" },
  2: { label: "产线", type: "success" },
  3: { label: "工作中心", type: "warning" }
};
const typeOptions = Object.entries(workshopTypeDict).map(([value, item]) => ({ label: item.label, value: Number(value) }));
const parentOptions = computed(() => [{ label: "无上级", value: 0 }, ...lookup.options("workshops")]);
const parentDict = computed(() => Object.fromEntries([["0", { label: "无上级", type: "info" }], ...lookup.list("workshops").map(item => [String(item.id), { label: item.name ?? item.code ?? String(item.id) }])]));
const columns = computed<CrudColumn[]>(() => [
  { prop: "code", label: "车间编码", width: 130 },
  { prop: "name", label: "车间/产线名称", minWidth: 170 },
  { prop: "workshopType", label: "类型", width: 110, dict: workshopTypeDict },
  { prop: "parentId", label: "上级车间", minWidth: 150, dict: parentDict.value },
  { prop: "manager", label: "负责人", width: 120 },
  { prop: "sort", label: "排序", width: 80, align: "right" },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
]);
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "车间编码", required: true },
  { prop: "name", label: "车间/产线名称", required: true },
  { prop: "workshopType", label: "类型", type: "select", options: typeOptions, required: true },
  { prop: "parentId", label: "上级车间", type: "select", options: parentOptions.value },
  { prop: "manager", label: "负责人" },
  { prop: "sort", label: "排序", type: "number", precision: 0, min: 0 },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);
</script>

<template>
  <CrudTable api="/workshop" title="车间管理" :columns="columns" :form-items="formItems" :default-form="{ workshopType: 1, parentId: 0, sort: 0, status: 1 }" />
</template>
