<script setup lang="ts">
import { computed, onMounted } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { useLookupStore } from "@/store/modules/lookup";
import { enableDict, toOptions } from "@/utils/biz";

const lookup = useLookupStore();
onMounted(() => lookup.load());
const levelDict = {
  1: { label: "普通", type: "info" },
  2: { label: "重要", type: "warning" },
  3: { label: "VIP", type: "success" }
};
const levelOptions = Object.entries(levelDict).map(([value, item]) => ({ label: item.label, value: Number(value) }));
const salesmanOptions = computed(() => lookup.options("users"));
const salesmanDict = computed(() => Object.fromEntries(lookup.list("users").map(user => [String(user.id), { label: user.name ?? user.code ?? String(user.id) }])));
const columns = computed<CrudColumn[]>(() => [
  { prop: "code", label: "客户编码", width: 125 },
  { prop: "name", label: "客户名称", minWidth: 180 },
  { prop: "shortName", label: "简称", width: 130 },
  { prop: "contact", label: "联系人", width: 110 },
  { prop: "phone", label: "联系电话", width: 135 },
  { prop: "level", label: "客户等级", width: 100, dict: levelDict },
  { prop: "creditLimit", label: "信用额度", width: 130, money: true, align: "right" },
  { prop: "salesmanId", label: "业务员", width: 110, dict: salesmanDict.value },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
]);
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "客户编码", required: true },
  { prop: "name", label: "客户名称", required: true },
  { prop: "shortName", label: "客户简称" },
  { prop: "contact", label: "联系人" },
  { prop: "phone", label: "联系电话" },
  { prop: "email", label: "电子邮箱" },
  { prop: "level", label: "客户等级", type: "select", options: levelOptions },
  { prop: "creditLimit", label: "信用额度", type: "number", precision: 2 },
  { prop: "salesmanId", label: "业务员", type: "select", options: salesmanOptions.value },
  { prop: "taxNo", label: "纳税人识别号" },
  { prop: "address", label: "联系地址", span: 24 },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);
const searchFields: CrudField[] = [{ prop: "level", label: "客户等级", type: "select", options: levelOptions }];
</script>

<template>
  <CrudTable api="/customer" title="客户管理" :columns="columns" :form-items="formItems" :search-fields="searchFields" :default-form="{ level: 1, creditLimit: 0, status: 1 }" :actions-width="160" />
</template>
