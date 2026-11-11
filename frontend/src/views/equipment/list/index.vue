<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { http } from "@/utils/http";
import { useLookupStore } from "@/store/modules/lookup";
import { equipmentStatusDict, toOptions } from "@/utils/biz";

const lookup = useLookupStore();
const categoryOptions = ref<Array<{ label: string; value: number }>>([]);
const columns: CrudColumn[] = [
  { prop: "code", label: "设备编码", width: 145 },
  { prop: "name", label: "设备名称", minWidth: 150 },
  { prop: "model", label: "型号", width: 130 },
  { prop: "serialNo", label: "出厂编号", width: 140 },
  { prop: "equipmentStatus", label: "设备状态", width: 100, dict: equipmentStatusDict },
  { prop: "location", label: "安装位置", minWidth: 130 },
  { prop: "runHours", label: "运行时长(h)", width: 115, align: "right" },
  { prop: "lastMaintainAt", label: "最近保养", width: 120, date: true }
];
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "设备编码", required: true },
  { prop: "name", label: "设备名称", required: true },
  { prop: "categoryId", label: "设备分类", type: "select", options: categoryOptions.value },
  { prop: "model", label: "规格型号" },
  { prop: "serialNo", label: "出厂编号" },
  { prop: "workshopId", label: "所属车间", type: "select", options: lookup.options("workshops") },
  { prop: "equipmentStatus", label: "设备状态", type: "select", options: toOptions(equipmentStatusDict), required: true },
  { prop: "purchaseDate", label: "购置日期", type: "date" },
  { prop: "vendor", label: "供应商" },
  { prop: "location", label: "安装位置" },
  { prop: "ipAddress", label: "设备IP" },
  { prop: "runHours", label: "累计运行时长(h)", type: "number", precision: 1 },
  { prop: "lastMaintainAt", label: "最近保养日期", type: "date" },
  { prop: "status", label: "资料状态", type: "select", options: [{ label: "启用", value: 1 }, { label: "停用", value: 0 }] },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);

onMounted(async () => {
  await lookup.load();
  const res: any = await http.request("get", "/equipment-category/options");
  if (res?.code === 0) categoryOptions.value = (res.data ?? []).map((x: any) => ({ label: `${x.code ? `${x.code} ` : ""}${x.name}`, value: x.id }));
});
</script>

<template>
  <CrudTable
    api="/equipment"
    title="设备台账"
    :columns="columns"
    :form-items="formItems"
    :default-form="{ equipmentStatus: 1, runHours: 0, status: 1 }"
    :actions-width="170"
  />
</template>
