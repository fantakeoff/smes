<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ElMessageBox } from "element-plus";
import CrudTable, { type CrudColumn, type CrudField } from "@/components/CrudTable/index.vue";
import { message } from "@/utils/message";
import { http } from "@/utils/http";

const loading = ref(false);
const rows = ref<any[]>([]);
const dialogVisible = ref(false);
const saving = ref(false);
const deletingId = ref<number | null>(null);
const activeTab = ref("tree");
const formRef = ref();
const editId = ref<number | null>(null);
const form = ref<any>({});
const flatColumns: CrudColumn[] = [
  { prop: "code", label: "分类编码", width: 150 },
  { prop: "name", label: "分类名称", minWidth: 180 },
  { prop: "parentId", label: "上级分类ID", width: 120, align: "right" },
  { prop: "sort", label: "排序", width: 90, align: "right" },
  { prop: "status", label: "状态", width: 90, dict: { 1: { label: "启用", type: "success" }, 0: { label: "停用", type: "info" } } }
];
const flatFormItems: CrudField[] = [
  { prop: "code", label: "分类编码" },
  { prop: "name", label: "分类名称", required: true },
  { prop: "sort", label: "排序", type: "number", precision: 0, min: 0 },
  { prop: "status", label: "状态", type: "select", options: [{ label: "启用", value: 1 }, { label: "停用", value: 0 }] },
  { prop: "remark", label: "备注", type: "textarea", span: 24 }
];
const formRules = {
  name: [{ required: true, message: "请输入分类名称", trigger: "blur" }],
  code: [{ max: 50, message: "分类编码不能超过 50 个字符", trigger: "blur" }]
};
const parentOptions = computed(() => {
  const excluded = new Set<number>();
  if (editId.value != null) {
    const queue = [editId.value];
    while (queue.length) {
      const id = queue.pop()!;
      excluded.add(id);
      rows.value.filter(item => Number(item.parentId) === id).forEach(item => queue.push(Number(item.id)));
    }
  }
  return [{ label: "无上级（根分类）", value: 0 }, ...rows.value.filter(item => !excluded.has(Number(item.id))).map(item => ({ label: `${item.code ? `${item.code} ` : ""}${item.name}`, value: Number(item.id) }))];
});
const treeRows = computed(() => {
  const byParent = new Map<number, any[]>();
  rows.value.forEach(row => {
    const parentId = Number(row.parentId || 0);
    if (!byParent.has(parentId)) byParent.set(parentId, []);
    byParent.get(parentId)!.push({ ...row, children: [] });
  });
  const sortRows = (items: any[]) => items.sort((a, b) => Number(a.sort || 0) - Number(b.sort || 0) || Number(a.id) - Number(b.id));
  const roots = sortRows([...(byParent.get(0) ?? []), ...rows.value.filter(row => Number(row.parentId) !== 0 && !rows.value.some(parent => Number(parent.id) === Number(row.parentId))).map(row => ({ ...row, children: [] }))]);
  const attach = (parent: any) => {
    parent.children = sortRows(byParent.get(Number(parent.id)) ?? []);
    parent.children.forEach(attach);
  };
  roots.forEach(attach);
  return roots;
});
function parentName(row: any) {
  if (!row.parentId) return "根分类";
  const parent = rows.value.find(item => Number(item.id) === Number(row.parentId));
  return parent?.name ?? "根分类";
}
async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/product-category", { params: { page: 1, pageSize: 500 } });
    if (res?.code === 0) rows.value = res.data?.list ?? [];
    else message(res?.message || "分类加载失败", { type: "error" });
  } catch (error: any) {
    message(error?.message || "分类加载失败", { type: "error" });
  } finally {
    loading.value = false;
  }
}
function openCreate(parentId = 0) {
  editId.value = null;
  form.value = { code: "", name: "", parentId, sort: 0, status: 1, remark: "" };
  dialogVisible.value = true;
}
async function openEdit(row: any) {
  const res: any = await http.request("get", `/product-category/${row.id}`);
  if (res?.code !== 0) return message(res?.message || "分类加载失败", { type: "error" });
  editId.value = Number(row.id);
  form.value = { ...res.data };
  dialogVisible.value = true;
}
async function save() {
  if (!formRef.value) return;
  try { await formRef.value.validate(); } catch { return; }
  saving.value = true;
  const payload = { ...form.value, parentId: Number(form.value.parentId || 0), sort: Number(form.value.sort || 0) };
  try {
    const res: any = editId.value
      ? await http.request("put", `/product-category/${editId.value}`, { data: payload })
      : await http.request("post", "/product-category", { data: payload });
    if (res?.code === 0) {
      message(editId.value ? "修改成功" : "新增成功", { type: "success" });
      dialogVisible.value = false;
      await fetchList();
    } else message(res?.message || "保存失败", { type: "error" });
  } catch (error: any) {
    message(error?.response?.data?.message || error?.message || "保存失败", { type: "error" });
  } finally {
    saving.value = false;
  }
}
async function remove(row: any) {
  if (rows.value.some(item => Number(item.parentId) === Number(row.id))) {
    return message("该分类仍有子分类，请先删除或调整子分类", { type: "warning" });
  }
  try { await ElMessageBox.confirm(`确定删除分类「${row.name}」吗？`, "提示", { type: "warning" }); } catch { return; }
  deletingId.value = Number(row.id);
  try {
    const res: any = await http.request("delete", `/product-category/${row.id}`);
    if (res?.code === 0) {
      message("删除成功", { type: "success" });
      await fetchList();
    } else message(res?.message || "删除失败", { type: "error" });
  } finally { deletingId.value = null; }
}

onMounted(() => fetchList());
</script>

<template>
  <div class="category-page">
    <el-tabs v-model="activeTab">
      <el-tab-pane label="树形分类" name="tree">
        <el-card shadow="never">
      <div class="toolbar">
        <div>
          <div class="title">产品分类</div>
          <div class="hint">支持层级分类，可在任一分类下新增子分类</div>
        </div>
        <el-button type="primary" @click="openCreate()">新增根分类</el-button>
      </div>
      <el-table v-loading="loading" :data="treeRows" row-key="id" :tree-props="{ children: 'children' }" default-expand-all border stripe>
        <el-table-column prop="code" label="分类编码" width="150" />
        <el-table-column prop="name" label="分类名称" min-width="200" />
        <el-table-column label="上级分类" min-width="160"><template #default="{ row }">{{ parentName(row) }}</template></el-table-column>
        <el-table-column prop="sort" label="排序" width="90" align="right" />
        <el-table-column prop="status" label="状态" width="90">
          <template #default="{ row }"><el-tag :type="Number(row.status) === 1 ? 'success' : 'info'" size="small">{{ Number(row.status) === 1 ? "启用" : "停用" }}</el-tag></template>
        </el-table-column>
        <el-table-column label="操作" width="230" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openCreate(Number(row.id))">新增子分类</el-button>
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="danger" :loading="deletingId === Number(row.id)" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
        </el-card>
      </el-tab-pane>
      <el-tab-pane label="列表维护" name="list">
        <CrudTable v-if="activeTab === 'list'" api="/product-category" title="分类列表维护" :columns="flatColumns" :form-items="flatFormItems" :default-form="{ parentId: 0, sort: 0, status: 1 }" />
      </el-tab-pane>
    </el-tabs>

    <el-dialog v-model="dialogVisible" :title="editId ? '编辑分类' : (form.parentId ? '新增子分类' : '新增根分类')" width="640px" destroy-on-close>
      <el-form ref="formRef" :model="form" :rules="formRules" label-width="100px">
        <el-row :gutter="16">
          <el-col :span="12"><el-form-item label="分类编码" prop="code"><el-input v-model="form.code" placeholder="选填" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="分类名称" prop="name"><el-input v-model="form.name" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="上级分类" prop="parentId"><el-select v-model="form.parentId" filterable style="width: 100%"><el-option v-for="option in parentOptions" :key="option.value" :label="option.label" :value="option.value" /></el-select></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="排序" prop="sort"><el-input-number v-model="form.sort" :min="0" :precision="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="状态" prop="status"><el-select v-model="form.status" style="width: 100%"><el-option label="启用" :value="1" /><el-option label="停用" :value="0" /></el-select></el-form-item></el-col>
          <el-col :span="24"><el-form-item label="备注" prop="remark"><el-input v-model="form.remark" type="textarea" :rows="3" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.category-page {
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 14px; }
  .title { font-size: 16px; font-weight: 600; }
  .hint { margin-top: 4px; color: var(--el-text-color-secondary); font-size: 12px; }
}
</style>
