<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { http } from "@/utils/http";
import { useLookupStore } from "@/store/modules/lookup";
import { enableDict, toolStatusDict, toOptions } from "@/utils/biz";

const lookup = useLookupStore();
const categoryOptions = ref<Array<{ label: string; value: number }>>([]);
const columns: CrudColumn[] = [
  { prop: "code", label: "工装编码", width: 145 },
  { prop: "name", label: "工装名称", minWidth: 150 },
  { prop: "spec", label: "规格型号", width: 130 },
  { prop: "toolStatus", label: "工装状态", width: 100, dict: toolStatusDict },
  { prop: "totalQty", label: "总数量", width: 95, align: "right" },
  { prop: "availableQty", label: "可用数量", width: 100, align: "right" },
  { prop: "location", label: "存放位置", minWidth: 130 },
  { prop: "purchaseDate", label: "购置日期", width: 120, date: true },
  { prop: "serviceLifeDays", label: "使用寿命(天)", width: 120, align: "right" }
];
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "工装编码", required: true },
  { prop: "name", label: "工装名称", required: true },
  { prop: "categoryId", label: "工装分类", type: "select", options: categoryOptions.value },
  { prop: "spec", label: "规格型号" },
  { prop: "unitId", label: "计量单位", type: "select", options: lookup.options("units") },
  { prop: "totalQty", label: "总数量", type: "number", precision: 2, min: 0 },
  { prop: "availableQty", label: "可用数量", type: "number", precision: 2, min: 0 },
  { prop: "toolStatus", label: "工装状态", type: "select", options: toOptions(toolStatusDict), required: true },
  { prop: "location", label: "存放位置" },
  { prop: "purchaseDate", label: "购置日期", type: "date" },
  { prop: "serviceLifeDays", label: "使用寿命(天)", type: "number", precision: 0, min: 0 },
  { prop: "status", label: "资料状态", type: "select", options: toOptions(enableDict) },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);

onMounted(async () => {
  await lookup.load();
  const res: any = await http.request("get", "/tool-category/options");
  if (res?.code === 0) categoryOptions.value = (res.data ?? []).map((x: any) => ({ label: `${x.code ? `${x.code} ` : ""}${x.name}`, value: x.id }));
});
</script>

<template>
  <CrudTable
    api="/tool"
    title="工装台账"
    :columns="columns"
    :form-items="formItems"
    :default-form="{ totalQty: 0, availableQty: 0, toolStatus: 1, serviceLifeDays: 0, status: 1 }"
    :actions-width="170"
  />
</template>
