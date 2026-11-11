<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";

type DictEntry = { label: string; type?: string; value?: any };
export interface CrudColumn {
  prop: string;
  label: string;
  width?: number | string;
  minWidth?: number;
  align?: string;
  dict?: Record<string | number, DictEntry> | DictEntry[];
  money?: boolean;
  date?: boolean;
  percent?: boolean;
  showOverflow?: boolean;
}
export interface CrudField {
  prop: string;
  label: string;
  type?: "input" | "number" | "select" | "textarea" | "date" | "switch" | "password";
  options?: { label: string; value: any }[];
  required?: boolean;
  span?: number;
  placeholder?: string;
  disabled?: boolean;
  precision?: number;
  min?: number;
  max?: number;
}

const props = withDefaults(
  defineProps<{
    api: string;
    title?: string;
    columns: CrudColumn[];
    formItems?: CrudField[];
    searchFields?: CrudField[];
    params?: Record<string, any>;
    defaultForm?: Record<string, any>;
    pageSize?: number;
    canCreate?: boolean;
    canEdit?: boolean;
    canDelete?: boolean;
    actionsWidth?: number;
    formWidth?: string;
    labelWidth?: string;
  }>(),
  {
    title: "",
    formItems: () => [],
    searchFields: () => [],
    params: () => ({}),
    defaultForm: () => ({}),
    pageSize: 20,
    canCreate: true,
    canEdit: true,
    canDelete: true,
    actionsWidth: 150,
    formWidth: "760px",
    labelWidth: "110px"
  }
);

const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(props.pageSize);
const keyword = ref("");
const filters = reactive<Record<string, any>>({});
const selection = ref<any[]>([]);
const dialogVisible = ref(false);
const editId = ref<number | null>(null);
const formRef = ref();
const submitting = ref(false);
const form = ref<Record<string, any>>({});

const rules = computed(() => {
  const result: Record<string, any> = {};
  props.formItems.forEach(f => {
    if (f.required) result[f.prop] = [{ required: true, message: `请填写${f.label}`, trigger: "blur" }];
  });
  return result;
});

function normalizeDict(dict?: CrudColumn["dict"]) {
  const map: Record<string, DictEntry> = {};
  if (!dict) return map;
  if (Array.isArray(dict)) {
    dict.forEach(d => (map[String(d.value)] = d));
  } else {
    Object.keys(dict).forEach(k => (map[k] = dict[k]));
  }
  return map;
}

function cellText(row: any, col: CrudColumn) {
  const value = row[col.prop];
  if (value === null || value === undefined || value === "") return "-";
  if (col.dict) {
    const entry = normalizeDict(col.dict)[String(value)];
    return entry?.label ?? value;
  }
  if (col.money) return Number(value).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  if (col.percent) return `${value}%`;
  if (col.date) return String(value).slice(0, 10);
  return value;
}

function dictType(row: any, col: CrudColumn) {
  if (!col.dict) return "info";
  return normalizeDict(col.dict)[String(row[col.prop])]?.type ?? "info";
}

async function fetchList() {
  loading.value = true;
  try {
    const params: Record<string, any> = {
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value || undefined,
      ...props.params,
      ...filters
    };
    Object.keys(params).forEach(k => {
      if (params[k] === "" || params[k] === null || params[k] === undefined) delete params[k];
    });
    const res: any = await http.request("get", props.api, { params });
    if (res?.code === 0) {
      list.value = res.data?.list ?? res.data ?? [];
      total.value = res.data?.total ?? list.value.length;
    } else {
      message(res?.message || "加载失败", { type: "error" });
    }
  } catch (e: any) {
    message(e?.message || "加载失败", { type: "error" });
  } finally {
    loading.value = false;
  }
}

function search() {
  page.value = 1;
  fetchList();
}

function reset() {
  keyword.value = "";
  Object.keys(filters).forEach(k => delete filters[k]);
  page.value = 1;
  fetchList();
}

async function openCreate() {
  editId.value = null;
  form.value = JSON.parse(JSON.stringify(props.defaultForm));
  dialogVisible.value = true;
}

async function openEdit(row: any) {
  editId.value = row.id;
  form.value = { ...row };
  try {
    const res: any = await http.request("get", `${props.api}/${row.id}`);
    if (res?.code === 0 && res.data) {
      form.value = Array.isArray(res.data) ? { ...row } : { ...row, ...res.data };
    }
  } catch {
    // 明细接口不存在时直接使用行数据
  }
  dialogVisible.value = true;
}

async function save() {
  if (!formRef.value) return;
  await formRef.value.validate(async (valid: boolean) => {
    if (!valid) return;
    submitting.value = true;
    try {
      const payload = { ...form.value };
      delete payload.lineCount;
      delete payload.roleNames;
      delete payload.createdAtText;
      const res: any = editId.value
        ? await http.request("put", `${props.api}/${editId.value}`, { data: payload })
        : await http.request("post", props.api, { data: payload });
      if (res?.code === 0) {
        message(editId.value ? "修改成功" : "新增成功", { type: "success" });
        dialogVisible.value = false;
        fetchList();
      } else {
        message(res?.message || "保存失败", { type: "error" });
      }
    } catch (e: any) {
      message(e?.response?.data?.message || e?.message || "保存失败", { type: "error" });
    } finally {
      submitting.value = false;
    }
  });
}

async function remove(row: any) {
  try {
    await ElMessageBox.confirm(`确定删除「${row.name || row.code || row.id}」吗？`, "提示", { type: "warning" });
  } catch {
    return;
  }
  const res: any = await http.request("delete", `${props.api}/${row.id}`);
  if (res?.code === 0) {
    message("删除成功", { type: "success" });
    fetchList();
  } else {
    message(res?.message || "删除失败", { type: "error" });
  }
}

async function batchRemove() {
  const ids = selection.value.map(x => x.id);
  if (!ids.length) return;
  try {
    await ElMessageBox.confirm(`确定删除选中的 ${ids.length} 条记录吗？`, "提示", { type: "warning" });
  } catch {
    return;
  }
  const res: any = await http.request("post", `${props.api}/batch-delete`, { data: ids });
  if (res?.code === 0) {
    message("删除成功", { type: "success" });
    fetchList();
  } else {
    message(res?.message || "删除失败", { type: "error" });
  }
}

function onSelectionChange(rows: any[]) {
  selection.value = rows;
}

onMounted(() => {
  props.searchFields.forEach(f => (filters[f.prop] = ""));
  fetchList();
});

defineExpose({ refresh: fetchList, openCreate });
</script>

<template>
  <div class="crud-page">
    <el-card shadow="never" class="search-card">
      <el-form :inline="true" @submit.prevent>
        <el-form-item label="关键字">
          <el-input v-model="keyword" clearable placeholder="编码 / 名称" style="width: 220px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item v-for="f in searchFields" :key="f.prop" :label="f.label">
          <el-select v-if="f.type === 'select'" v-model="filters[f.prop]" clearable placeholder="全部" style="width: 160px">
            <el-option v-for="o in f.options" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
          <el-date-picker
            v-else-if="f.type === 'date'"
            v-model="filters[f.prop]"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="选择日期"
            style="width: 160px"
          />
          <el-input v-else v-model="filters[f.prop]" clearable :placeholder="f.placeholder || '请输入'" style="width: 160px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
          <el-button @click="reset">重置</el-button>
        </el-form-item>
        <slot name="search-extra" />
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div class="toolbar">
        <div class="title">{{ title }}</div>
        <div class="toolbar-actions">
          <el-button v-if="canCreate" type="primary" @click="openCreate">新增</el-button>
          <el-button v-if="canDelete && selection.length > 0" type="danger" plain @click="batchRemove">
            批量删除
          </el-button>
          <slot name="toolbar" />
        </div>
      </div>

      <el-table v-loading="loading" :data="list" border stripe row-key="id" @selection-change="onSelectionChange">
        <el-table-column v-if="canDelete" type="selection" width="45" fixed="left" />
        <el-table-column
          v-for="col in columns"
          :key="col.prop"
          :prop="col.prop"
          :label="col.label"
          :width="col.width"
          :min-width="col.minWidth"
          :align="col.align || 'left'"
          :show-overflow-tooltip="col.showOverflow !== false"
        >
          <template #default="{ row }">
            <el-tag v-if="col.dict" :type="dictType(row, col) as any" size="small">{{ cellText(row, col) }}</el-tag>
            <span v-else>{{ cellText(row, col) }}</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" :width="actionsWidth" fixed="right">
          <template #default="{ row }">
            <slot name="actions" :row="row" />
            <el-button v-if="canEdit" link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button v-if="canDelete" link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pager">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          :page-sizes="[10, 20, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          background
          @current-change="fetchList"
          @size-change="search"
        />
      </div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editId ? `编辑${title}` : `新增${title}`" :width="formWidth" destroy-on-close>
      <el-form ref="formRef" :model="form" :rules="rules" :label-width="labelWidth">
        <el-row :gutter="16">
          <el-col v-for="f in formItems" :key="f.prop" :span="f.span || 12">
            <el-form-item :label="f.label" :prop="f.prop">
              <el-select
                v-if="f.type === 'select'"
                v-model="form[f.prop]"
                clearable
                filterable
                :placeholder="f.placeholder || `请选择${f.label}`"
                :disabled="f.disabled"
                style="width: 100%"
              >
                <el-option v-for="o in f.options" :key="o.value" :label="o.label" :value="o.value" />
              </el-select>
              <el-input-number
                v-else-if="f.type === 'number'"
                v-model="form[f.prop]"
                :precision="f.precision ?? 0"
                :min="f.min ?? 0"
                :max="f.max"
                :disabled="f.disabled"
                controls-position="right"
                style="width: 100%"
              />
              <el-date-picker
                v-else-if="f.type === 'date'"
                v-model="form[f.prop]"
                type="date"
                value-format="YYYY-MM-DD"
                :placeholder="f.placeholder || `请选择${f.label}`"
                :disabled="f.disabled"
                style="width: 100%"
              />
              <el-switch v-else-if="f.type === 'switch'" v-model="form[f.prop]" :disabled="f.disabled" />
              <el-input
                v-else-if="f.type === 'textarea'"
                v-model="form[f.prop]"
                type="textarea"
                :rows="3"
                :placeholder="f.placeholder || `请输入${f.label}`"
                :disabled="f.disabled"
              />
              <el-input
                v-else
                v-model="form[f.prop]"
                :type="f.type === 'password' ? 'password' : 'text'"
                :show-password="f.type === 'password'"
                :placeholder="f.placeholder || `请输入${f.label}`"
                :disabled="f.disabled"
              />
            </el-form-item>
          </el-col>
        </el-row>
        <slot name="form-extra" :form="form" />
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.crud-page {
  .search-card {
    margin-bottom: 12px;
    :deep(.el-form-item) {
      margin-bottom: 0;
    }
  }
  .toolbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 12px;
    .title {
      font-size: 16px;
      font-weight: 600;
    }
  }
  .pager {
    display: flex;
    justify-content: flex-end;
    margin-top: 16px;
  }
}
</style>
