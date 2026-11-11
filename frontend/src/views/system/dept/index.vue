<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";

type DeptNode = { id: number; parentId: number; name: string; code?: string; sort?: number; children?: DeptNode[]; [key: string]: any };
const rows = ref<DeptNode[]>([]);
const loading = ref(false);
const keyword = ref("");
const dialogVisible = ref(false);
const submitting = ref(false);
const editId = ref<number | null>(null);
const formRef = ref<any>();
const form = reactive<any>({ parentId: 0, name: "", code: "", leader: "", phone: "", sort: 0, status: 1 });
const rules = { name: [{ required: true, message: "请输入部门名称", trigger: "blur" }] };
const treeProps = { label: "name", children: "children", value: "id" };

function buildTree(source: DeptNode[]): DeptNode[] {
  const map = new Map<number, DeptNode>();
  source.forEach(row => map.set(Number(row.id), { ...row, id: Number(row.id), parentId: Number(row.parentId || 0), children: [] }));
  const roots: DeptNode[] = [];
  map.forEach(node => {
    const parent = map.get(node.parentId);
    if (parent && parent.id !== node.id) parent.children!.push(node);
    else roots.push(node);
  });
  const sortItems = (items: DeptNode[]) => {
    items.sort((a, b) => (a.sort ?? 0) - (b.sort ?? 0) || a.id - b.id);
    items.forEach(item => sortItems(item.children ?? []));
  };
  sortItems(roots);
  return roots;
}
const treeData = computed(() => buildTree(rows.value));
function filterTree(items: DeptNode[], q: string): DeptNode[] {
  return items.flatMap(node => {
    const children = filterTree(node.children ?? [], q);
    const matched = [node.name, node.code, node.leader, node.phone].some(value => String(value ?? "").toLowerCase().includes(q));
    if (!matched && children.length === 0) return [];
    return [{ ...node, children: matched ? (node.children ?? []) : children }];
  });
}
const visibleTree = computed(() => keyword.value.trim() ? filterTree(treeData.value, keyword.value.trim().toLowerCase()) : treeData.value);
function deptName(id: number) { return rows.value.find(row => Number(row.id) === Number(id))?.name ?? "根部门"; }

async function fetchRows() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/dept");
    if (res?.code === 0) rows.value = res.data ?? [];
    else message(res?.message || "部门加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "部门加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function resetForm(parentId = 0) { Object.assign(form, { parentId, name: "", code: "", leader: "", phone: "", sort: 0, status: 1 }); }
function openCreate(parentId = 0) { editId.value = null; resetForm(parentId); dialogVisible.value = true; }
function openEdit(row: any) {
  editId.value = Number(row.id);
  Object.assign(form, { parentId: Number(row.parentId || 0), name: row.name ?? "", code: row.code ?? "", leader: row.leader ?? "", phone: row.phone ?? "", sort: Number(row.sort ?? 0), status: Number(row.status ?? 1) });
  dialogVisible.value = true;
}
async function save() {
  const valid = await formRef.value?.validate().catch(() => false);
  if (!valid) return;
  submitting.value = true;
  try {
    const payload = { parentId: Number(form.parentId || 0), name: form.name.trim(), code: form.code || null, leader: form.leader || null, phone: form.phone || null, sort: Number(form.sort || 0), status: Number(form.status) };
    const res: any = editId.value
      ? await http.request("put", `/dept/${editId.value}`, { data: payload })
      : await http.request("post", "/dept", { data: payload });
    if (res?.code === 0) {
      message(editId.value ? "部门修改成功" : "部门新增成功", { type: "success" });
      dialogVisible.value = false;
      await fetchRows();
    } else message(res?.message || "保存失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "保存失败", { type: "error" }); }
  finally { submitting.value = false; }
}
async function remove(row: any) {
  try { await ElMessageBox.confirm(`确定删除部门「${row.name}」吗？`, "删除确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("delete", `/dept/${row.id}`);
    if (res?.code === 0) { message("部门已删除", { type: "success" }); await fetchRows(); }
    else message(res?.message || "删除失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "删除失败", { type: "error" }); }
}

onMounted(fetchRows);
</script>

<template>
  <div class="dept-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="部门关键字"><el-input v-model="keyword" clearable placeholder="名称 / 编码 / 负责人" style="width: 260px" @keyup.enter="fetchRows" /></el-form-item>
        <el-form-item><el-button type="primary" @click="fetchRows">刷新</el-button><el-button @click="keyword = ''">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><div class="title">组织部门树</div><el-button type="primary" @click="openCreate()">新增部门</el-button></div>
      <el-table v-loading="loading" :data="visibleTree" row-key="id" border stripe default-expand-all :tree-props="{ children: 'children' }">
        <el-table-column prop="name" label="部门名称" min-width="190" show-overflow-tooltip />
        <el-table-column prop="code" label="部门编码" min-width="140" show-overflow-tooltip />
        <el-table-column label="上级部门" min-width="150"><template #default="{ row }">{{ row.parentId ? deptName(row.parentId) : "-" }}</template></el-table-column>
        <el-table-column prop="leader" label="负责人" width="120" />
        <el-table-column prop="phone" label="联系电话" width="140" />
        <el-table-column prop="sort" label="排序" width="80" align="center" />
        <el-table-column label="状态" width="85" align="center"><template #default="{ row }"><el-tag :type="row.status === 1 ? 'success' : 'info'" size="small">{{ row.status === 1 ? "启用" : "停用" }}</el-tag></template></el-table-column>
        <el-table-column label="操作" width="180" fixed="right" align="center">
          <template #default="{ row }"><el-button link type="primary" @click="openCreate(row.id)">新增下级</el-button><el-button link type="primary" @click="openEdit(row)">编辑</el-button><el-button link type="danger" @click="remove(row)">删除</el-button></template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editId ? '编辑部门' : '新增部门'" width="680px" destroy-on-close>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="100px">
        <el-row :gutter="16">
          <el-col :span="12"><el-form-item label="上级部门"><el-tree-select v-model="form.parentId" :data="treeData" :props="treeProps" node-key="id" check-strictly clearable default-expand-all placeholder="根部门" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="部门名称" prop="name"><el-input v-model="form.name" maxlength="80" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="部门编码"><el-input v-model="form.code" maxlength="50" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="负责人"><el-input v-model="form.leader" maxlength="50" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="联系电话"><el-input v-model="form.phone" maxlength="30" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="排序"><el-input-number v-model="form.sort" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="状态"><el-switch v-model="form.status" :active-value="1" :inactive-value="0" active-text="启用" inactive-text="停用" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="submitting" @click="save">保存</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.dept-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
}
</style>
