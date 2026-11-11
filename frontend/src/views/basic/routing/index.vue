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
const processOptions = computed(() => lookup.options("processes"));
const workshopOptions = computed(() => lookup.options("workshops"));
const productDict = computed(() => Object.fromEntries(lookup.list("products").map(item => [String(item.id), { label: `${item.code ? `${item.code} ` : ""}${item.name ?? item.id}` }])));
const columns = computed<CrudColumn[]>(() => [
  { prop: "code", label: "路线编码", width: 145 },
  { prop: "name", label: "路线名称", minWidth: 170 },
  { prop: "productId", label: "产品", minWidth: 190, dict: productDict.value },
  { prop: "version", label: "版本", width: 100 },
  { prop: "effectiveDate", label: "生效日期", width: 120, date: true },
  { prop: "isDefault", label: "默认路线", width: 105, dict: isDefaultDict },
  { prop: "status", label: "状态", width: 90, dict: enableDict }
]);
const formItems = computed<CrudField[]>(() => [
  { prop: "code", label: "路线编码", placeholder: "留空自动生成" },
  { prop: "name", label: "路线名称", placeholder: "留空按产品名称生成" },
  { prop: "productId", label: "产品", type: "select", options: productOptions.value, required: true },
  { prop: "version", label: "版本", required: true },
  { prop: "effectiveDate", label: "生效日期", type: "date", required: true },
  { prop: "isDefault", label: "默认路线", type: "switch" },
  { prop: "status", label: "状态", type: "select", options: toOptions(enableDict), required: true },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
]);
function addStep(form: any) {
  if (!Array.isArray(form.steps)) form.steps = [];
  form.steps.push({ processId: null, workshopId: null, standardHours: 0, isKey: false, description: "" });
}
function validateSteps(_rule: any, value: any, callback: (error?: Error) => void) {
  if (!Array.isArray(value) || value.length === 0) return callback(new Error("请至少添加一条工序"));
  if (value.some((step: any) => !step.processId)) return callback(new Error("请选择每一行的工序"));
  callback();
}
</script>

<template>
  <CrudTable
    api="/routing"
    title="工艺路线"
    :columns="columns"
    :form-items="formItems"
    :default-form="{ version: 'V1.0', effectiveDate: today, isDefault: true, status: 1, steps: [{ processId: null, workshopId: null, standardHours: 0, isKey: false, description: '' }] }"
    :form-width="'960px'"
    :actions-width="170"
  >
    <template #form-extra="{ form }">
      <el-form-item label="工序明细" prop="steps" :rules="[{ validator: validateSteps, trigger: 'change' }]">
        <div class="detail-editor">
          <el-table :data="form.steps || []" border stripe size="small">
            <el-table-column label="序号" width="64" align="center">
              <template #default="{ $index }">{{ $index + 1 }}</template>
            </el-table-column>
            <el-table-column label="工序" min-width="170">
              <template #default="{ row, $index }">
                <el-form-item :prop="`steps.${$index}.processId`" :rules="[{ required: true, message: '请选择工序', trigger: 'change' }]" class="line-field">
                  <el-select v-model="row.processId" filterable placeholder="请选择工序" style="width: 100%">
                    <el-option v-for="item in processOptions" :key="item.value" :label="item.label" :value="item.value" />
                  </el-select>
                </el-form-item>
              </template>
            </el-table-column>
            <el-table-column label="生产车间" min-width="160">
              <template #default="{ row }">
                <el-select v-model="row.workshopId" clearable filterable placeholder="可选" style="width: 100%">
                  <el-option v-for="item in workshopOptions" :key="item.value" :label="item.label" :value="item.value" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="标准工时(h)" width="150" align="right">
              <template #default="{ row }">
                <el-input-number v-model="row.standardHours" :min="0" :precision="2" controls-position="right" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="关键工序" width="110" align="center">
              <template #default="{ row }"><el-switch v-model="row.isKey" /></template>
            </el-table-column>
            <el-table-column label="说明" min-width="150">
              <template #default="{ row }"><el-input v-model="row.description" placeholder="工序说明" /></template>
            </el-table-column>
            <el-table-column label="操作" width="70" align="center">
              <template #default="{ $index }"><el-button link type="danger" @click="form.steps.splice($index, 1)">删除</el-button></template>
            </el-table-column>
          </el-table>
          <el-button class="add-line" size="small" @click="addStep(form)">添加工序</el-button>
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
