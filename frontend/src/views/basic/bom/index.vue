<script setup lang="ts">
import { computed, onMounted } from "vue";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { useLookupStore } from "@/store/modules/lookup";
import { enableDict, toOptions } from "@/utils/biz";

const lookup = useLookupStore();
onMounted(() => lookup.load());
const today = new Date().toISOString().slice(0, 10);
const isDefaultDict = { true: { label: "是", type: "success" }, false: { label: "否", type: "info" } };
const productOptions = computed(() => lookup.options("products"));
const unitOptions = computed(() => lookup.options("units"));
const productDict = computed(() => Object.fromEntries(lookup.list("products").map(item => [String(item.id), { label: `${item.code ? `${item.code} ` : ""}${item.name ?? item.id}` }])));
const unitDict = computed(() => Object.fromEntries(lookup.list("units").map(item => [String(item.id), { label: item.name ?? item.code ?? String(item.id) }])));
const columns = computed<CrudColumn[]>(() => [
  { prop: "code", label: "BOM编码", width: 145 },
  { prop: "name", label: "BOM名称", minWidth: 170 },
  { prop: "productId", label: "成品/父项", minWidth: 190, dict: productDict.value },
  { prop: "version", label: "版本", width: 100 },
  { prop: "effectiveDate", label: "生效日期", width: 120, date: true },
  { prop: "isDefault", label: "默认BOM", width: 100, dict: isDefaultDict },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
]);
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "BOM编码", placeholder: "留空自动生成" },
  { prop: "name", label: "BOM名称", placeholder: "留空按产品名称生成" },
  { prop: "productId", label: "成品/父项", type: "select", options: productOptions.value, required: true },
  { prop: "version", label: "版本", required: true },
  { prop: "effectiveDate", label: "生效日期", type: "date", required: true },
  { prop: "isDefault", label: "默认BOM", type: "switch" },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);
function addLine(form: any) {
  if (!Array.isArray(form.lines)) form.lines = [];
  form.lines.push({ materialId: null, qty: 1, unitId: null, lossRate: 0, remark2: "" });
}
function validateLines(_rule: any, value: any, callback: (error?: Error) => void) {
  if (!Array.isArray(value) || value.length === 0) return callback(new Error("请至少添加一条用料明细"));
  if (value.some((line: any) => !line.materialId || Number(line.qty) <= 0)) return callback(new Error("请为每行选择物料并填写大于 0 的用量"));
  callback();
}
</script>

<template>
  <CrudTable
    api="/bom"
    title="物料清单"
    :columns="columns"
    :form-items="formItems"
    :default-form="{ version: 'V1.0', effectiveDate: today, isDefault: true, status: 1, lines: [{ materialId: null, qty: 1, unitId: null, lossRate: 0, remark2: '' }] }"
    :form-width="'960px'"
    :actions-width="170"
  >
    <template #form-extra="{ form }">
      <el-form-item label="用料明细" prop="lines" :rules="[{ validator: validateLines, trigger: 'change' }]">
        <div class="detail-editor">
          <el-table :data="form.lines || []" border stripe size="small">
            <el-table-column label="序号" width="64" align="center">
              <template #default="{ $index }">{{ $index + 1 }}</template>
            </el-table-column>
            <el-table-column label="物料" min-width="220">
              <template #default="{ row, $index }">
                <el-form-item :prop="`lines.${$index}.materialId`" :rules="[{ required: true, message: '请选择物料', trigger: 'change' }]" class="line-field">
                  <el-select v-model="row.materialId" filterable placeholder="请选择物料" style="width: 100%">
                    <el-option v-for="item in productOptions" :key="item.value" :label="item.label" :value="item.value" />
                  </el-select>
                </el-form-item>
              </template>
            </el-table-column>
            <el-table-column label="用量" width="150" align="right">
              <template #default="{ row }">
                <el-input-number v-model="row.qty" :min="0.0001" :precision="4" controls-position="right" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="单位" width="150">
              <template #default="{ row }">
                <el-select v-model="row.unitId" clearable filterable placeholder="可选" style="width: 100%">
                  <el-option v-for="item in unitOptions" :key="item.value" :label="item.label" :value="item.value" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="损耗率(%)" width="140" align="right">
              <template #default="{ row }"><el-input-number v-model="row.lossRate" :min="0" :precision="2" controls-position="right" style="width: 100%" /></template>
            </el-table-column>
            <el-table-column label="备注" min-width="150">
              <template #default="{ row }"><el-input v-model="row.remark2" placeholder="用料备注" /></template>
            </el-table-column>
            <el-table-column label="操作" width="70" align="center">
              <template #default="{ $index }"><el-button link type="danger" @click="form.lines.splice($index, 1)">删除</el-button></template>
            </el-table-column>
          </el-table>
          <el-button class="add-line" size="small" @click="addLine(form)">添加物料</el-button>
        </div>
      </el-form-item>
    </template>
  </CrudTable>
</template>

<style scoped lang="scss">
.detail-editor { width: 100%; }
.add-line { margin-top: 10px; }
.line-field { margin-bottom: 0; }
</style>
