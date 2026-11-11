<script setup lang="ts">
import { computed, onMounted } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { useLookupStore } from "@/store/modules/lookup";
import { enableDict, toOptions } from "@/utils/biz";

const lookup = useLookupStore();
onMounted(() => lookup.load());
const processTypeDict = {
  1: { label: "加工", type: "primary" },
  2: { label: "装配", type: "success" },
  3: { label: "检验", type: "warning" },
  4: { label: "包装", type: "info" }
};
const typeOptions = Object.entries(processTypeDict).map(([value, item]) => ({ label: item.label, value: Number(value) }));
const outsourcedDict = { true: { label: "是", type: "warning" }, false: { label: "否", type: "info" } };
const workshopOptions = computed(() => lookup.options("workshops"));
const workshopDict = computed(() => Object.fromEntries(lookup.list("workshops").map(item => [String(item.id), { label: item.name ?? item.code ?? String(item.id) }])));
const columns = computed<CrudColumn[]>(() => [
  { prop: "code", label: "工序编码", width: 125 },
  { prop: "name", label: "工序名称", minWidth: 150 },
  { prop: "processType", label: "工序类型", width: 110, dict: processTypeDict },
  { prop: "workshopId", label: "所属车间", minWidth: 140, dict: workshopDict.value },
  { prop: "standardHours", label: "标准工时(h)", width: 120, align: "right" },
  { prop: "price", label: "工序单价", width: 120, money: true, align: "right" },
  { prop: "isOutsourced", label: "是否外协", width: 100, dict: outsourcedDict },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
]);
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "工序编码", required: true },
  { prop: "name", label: "工序名称", required: true },
  { prop: "processType", label: "工序类型", type: "select", options: typeOptions, required: true },
  { prop: "workshopId", label: "所属车间", type: "select", options: workshopOptions.value },
  { prop: "standardHours", label: "标准工时(h)", type: "number", precision: 2, min: 0 },
  { prop: "price", label: "工序单价", type: "number", precision: 2, min: 0 },
  { prop: "isOutsourced", label: "外协工序", type: "switch" },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);
</script>

<template>
  <CrudTable api="/process" title="工序管理" :columns="columns" :form-items="formItems" :default-form="{ processType: 1, standardHours: 0, price: 0, isOutsourced: false, status: 1 }" />
</template>
