<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { http } from "@/utils/http";
import { enableDict, toOptions } from "@/utils/biz";

const parentOptions = ref<Array<{ label: string; value: number }>>([{ label: "顶级分类", value: 0 }]);
const columns: CrudColumn[] = [
  { prop: "code", label: "分类编码", width: 150 },
  { prop: "name", label: "分类名称", minWidth: 180 },
  { prop: "parentId", label: "上级分类ID", width: 120, align: "right" },
  { prop: "sort", label: "排序", width: 90, align: "right" },
  { prop: "status", label: "状态", width: 100, dict: enableDict }
];
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "分类编码", placeholder: "留空自动生成" },
  { prop: "name", label: "分类名称", required: true },
  { prop: "parentId", label: "上级分类", type: "select", options: parentOptions.value },
  { prop: "sort", label: "排序", type: "number", precision: 0, min: 0 },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict) },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);

onMounted(async () => {
  const res: any = await http.request("get", "/equipment-category/options");
  if (res?.code === 0) {
    parentOptions.value = [
      { label: "顶级分类", value: 0 },
      ...(res.data ?? []).map((x: any) => ({ label: `${x.code ? `${x.code} ` : ""}${x.name}`, value: x.id }))
    ];
  }
});
</script>

<template>
  <CrudTable
    api="/equipment-category"
    title="设备分类"
    :columns="columns"
    :form-items="formItems"
    :default-form="{ parentId: 0, sort: 0, status: 1 }"
  />
</template>
