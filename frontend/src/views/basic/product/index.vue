<script setup lang="ts">
import { computed, onMounted } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { useLookupStore } from "@/store/modules/lookup";
import { enableDict, productTypeDict, toOptions } from "@/utils/biz";

const lookup = useLookupStore();
onMounted(() => lookup.load());

const columns: CrudColumn[] = [
  { prop: "code", label: "产品编码", width: 130 },
  { prop: "name", label: "产品名称", minWidth: 140 },
  { prop: "spec", label: "规格型号", width: 120 },
  { prop: "productType", label: "类型", width: 90, dict: productTypeDict },
  { prop: "salePrice", label: "销售价", width: 110, money: true, align: "right" },
  { prop: "costPrice", label: "成本价", width: 110, money: true, align: "right" },
  { prop: "safetyStock", label: "安全库存", width: 100, align: "right" },
  { prop: "barcode", label: "条码", width: 140 },
  { prop: "status", label: "状态", width: 80, dict: enableDict }
];

const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "产品编码", placeholder: "留空自动生成" },
  { prop: "name", label: "产品名称", required: true },
  { prop: "spec", label: "规格型号" },
  { prop: "categoryId", label: "产品分类", type: "select", options: lookup.options("categories") },
  { prop: "brandId", label: "品牌", type: "select", options: lookup.options("brands") },
  { prop: "unitId", label: "计量单位", type: "select", options: lookup.options("units") },
  { prop: "productType", label: "产品类型", type: "select", options: toOptions(productTypeDict), required: true },
  { prop: "salePrice", label: "销售价", type: "number", precision: 2 },
  { prop: "costPrice", label: "成本价", type: "number", precision: 2 },
  { prop: "safetyStock", label: "安全库存", type: "number", precision: 0 },
  { prop: "barcode", label: "条码" },
  { prop: "drawingNo", label: "图号" },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict) },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);
</script>

<template>
  <CrudTable
    api="/product"
    title="产品管理"
    :columns="columns"
    :form-items="formItems"
    :default-form="{ status: 1, productType: 1, salePrice: 0, costPrice: 0, safetyStock: 0 }"
  />
</template>
