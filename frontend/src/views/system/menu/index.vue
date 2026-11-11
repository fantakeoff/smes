<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";

type TreeNode = { id: number; parentId: number; title: string; path?: string; type: number; sort?: number; children?: TreeNode[]; [key: string]: any };
const rows = ref<TreeNode[]>([]);
const loading = ref(false);
const keyword = ref("");
const dialogVisible = ref(false);
const submitting = ref(false);
const editId = ref<number | null>(null);
const formRef = ref<any>();
const form = reactive<any>({ parentId: 0, title: "", type: 2, path: "", component: "", icon: "", permission: "", sort: 0, visible: true, keepAlive: false, status: 1 });
const rules = { title: [{ required: true, message: "请输入菜单名称", trigger: "blur" }] };
const treeProps = { label: "title", children: "children", value: "id" };

function buildTree(source: TreeNode[]): TreeNode[] {
  const map = new Map<number, TreeNode>();
  source.forEach(row => map.set(Number(row.id), { ...row, id: Number(row.id), parentId: Number(row.parentId || 0), children: [] }));
  const roots: TreeNode[] = [];
  map.forEach(node => {
    const parent = map.get(node.parentId);
    if (parent && parent.id !== node.id) parent.children!.push(node);
    else roots.push(node);
  });
  const sortItems = (items: TreeNode[]) => {
    items.sort((a, b) => (a.sort ?? 0) - (b.sort ?? 0) || a.id - b.id);
    items.forEach(item => sortItems(item.children ?? []));
  };
  sortItems(roots);
  return roots;
}
const treeData = computed(() => buildTree(rows.value));
function filterTree(items: TreeNode[], q: string): TreeNode[] {
  return items.flatMap(node => {
    const children = filterTree(node.children ?? [], q);
    const matched = [node.title, node.path, node.component, node.permission].some(value => String(value ?? "").toLowerCase().includes(q));
    if (!matched && children.length === 0) return [];
    return [{ ...node, children: matched ? (node.children ?? []) : children }];
  });
}
const visibleTree = computed(() => keyword.value.trim() ? filterTree(treeData.value, keyword.value.trim().toLowerCase()) : treeData.value);

async function fetchRows() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/menu");
    if (res?.code === 0) rows.value = res.data ?? [];
    else message(res?.message || "菜单加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "菜单加载失败", { type: "error" }); }
  finally { loading.value = false; }
}
function resetForm(parentId = 0) {
  Object.assign(form, { parentId, title: "", type: 2, path: "", component: "", icon: "", permission: "", sort: 0, visible: true, keepAlive: false, status: 1 });
}
function openCreate(parentId = 0) { editId.value = null; resetForm(parentId); dialogVisible.value = true; }
function openEdit(row: any) {
  editId.value = Number(row.id);
  Object.assign(form, { parentId: Number(row.parentId || 0), title: row.title ?? "", type: Number(row.type ?? 2), path: row.path ?? "", component: row.component ?? "", icon: row.icon ?? "", permission: row.permission ?? "", sort: Number(row.sort ?? 0), visible: row.visible !== false, keepAlive: row.keepAlive === true, status: Number(row.status ?? 1) });
  dialogVisible.value = true;
}
async function save() {
  const valid = await formRef.value?.validate().catch(() => false);
  if (!valid) return;
  submitting.value = true;
  try {
    const payload = { parentId: Number(form.parentId || 0), title: form.title.trim(), type: Number(form.type), path: form.path || null, component: form.component || null, icon: form.icon || null, permission: form.permission || null, sort: Number(form.sort || 0), visible: Boolean(form.visible), keepAlive: Boolean(form.keepAlive), status: Number(form.status) };
    const res: any = editId.value
      ? await http.request("put", `/menu/${editId.value}`, { data: payload })
      : await http.request("post", "/menu", { data: payload });
    if (res?.code === 0) {
      message(editId.value ? "菜单修改成功" : "菜单新增成功", { type: "success" });
      dialogVisible.value = false;
      await fetchRows();
    } else message(res?.message || "保存失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "保存失败", { type: "error" }); }
  finally { submitting.value = false; }
}
async function remove(row: any) {
  try { await ElMessageBox.confirm(`确定删除「${row.title}」吗？`, "删除确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("delete", `/menu/${row.id}`);
    if (res?.code === 0) { message("菜单已删除", { type: "success" }); await fetchRows(); }
    else message(res?.message || "删除失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "删除失败", { type: "error" }); }
}
function typeText(type: number) { return ({ 1: "目录", 2: "菜单", 3: "按钮" } as Record<number, string>)[type] ?? "未知"; }
function typeTag(type: number) { return type === 1 ? "warning" : type === 2 ? "primary" : "info"; }

onMounted(fetchRows);
</script>

<template>
  <div class="menu-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="菜单名称">
          <el-input v-model="keyword" clearable placeholder="名称 / 路径 / 权限标识" style="width: 280px" @keyup.enter="fetchRows" />
        </el-form-item>
        <el-form-item><el-button type="primary" @click="fetchRows">刷新</el-button><el-button @click="keyword = ''">重置</el-button></el-form-item>
      </el-form>
    </el-card>
    <el-card shadow="never">
      <div class="toolbar"><div class="title">菜单与权限树</div><el-button type="primary" @click="openCreate()">新增菜单</el-button></div>
      <el-table v-loading="loading" :data="visibleTree" row-key="id" border stripe default-expand-all :tree-props="{ children: 'children' }">
        <el-table-column prop="title" label="菜单名称" min-width="190" show-overflow-tooltip />
        <el-table-column label="类型" width="90" align="center"><template #default="{ row }"><el-tag :type="typeTag(row.type) as any" size="small">{{ typeText(row.type) }}</el-tag></template></el-table-column>
        <el-table-column prop="path" label="路由地址" min-width="170" show-overflow-tooltip />
        <el-table-column prop="component" label="组件路径" min-width="190" show-overflow-tooltip />
        <el-table-column prop="permission" label="权限标识" min-width="150" show-overflow-tooltip />
        <el-table-column prop="sort" label="排序" width="70" align="center" />
        <el-table-column label="可见" width="75" align="center"><template #default="{ row }"><el-tag :type="row.visible ? 'success' : 'info'" size="small">{{ row.visible ? "显示" : "隐藏" }}</el-tag></template></el-table-column>
        <el-table-column label="状态" width="75" align="center"><template #default="{ row }"><el-tag :type="row.status === 1 ? 'success' : 'info'" size="small">{{ row.status === 1 ? "启用" : "停用" }}</el-tag></template></el-table-column>
        <el-table-column label="操作" width="180" fixed="right" align="center">
          <template #default="{ row }"><el-button link type="primary" @click="openCreate(row.id)">新增下级</el-button><el-button link type="primary" @click="openEdit(row)">编辑</el-button><el-button link type="danger" @click="remove(row)">删除</el-button></template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editId ? '编辑菜单' : '新增菜单'" width="820px" destroy-on-close>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="100px">
        <el-row :gutter="16">
          <el-col :span="12"><el-form-item label="上级菜单"><el-tree-select v-model="form.parentId" :data="treeData" :props="treeProps" node-key="id" check-strictly clearable default-expand-all placeholder="根目录" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="菜单类型"><el-select v-model="form.type" style="width: 100%"><el-option label="目录" :value="1" /><el-option label="菜单" :value="2" /><el-option label="按钮" :value="3" /></el-select></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="菜单名称" prop="title"><el-input v-model="form.title" maxlength="50" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="路由地址"><el-input v-model="form.path" placeholder="如 /system/role" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="组件路径"><el-input v-model="form.component" placeholder="如 system/role/index" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="图标"><el-input v-model="form.icon" placeholder="如 ep:setting" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="权限标识"><el-input v-model="form.permission" placeholder="如 system:role:edit" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="排序"><el-input-number v-model="form.sort" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="状态"><el-switch v-model="form.status" :active-value="1" :inactive-value="0" active-text="启用" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="菜单可见"><el-switch v-model="form.visible" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="缓存页面"><el-switch v-model="form.keepAlive" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="submitting" @click="save">保存</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.menu-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .title { font-size: 16px; font-weight: 600; }
}
</style>
