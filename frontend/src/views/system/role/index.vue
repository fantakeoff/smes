<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";

type MenuNode = { id: number; parentId: number; title: string; type: number; sort?: number; children?: MenuNode[] };

const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const menuLoading = ref(false);
const menuTree = ref<MenuNode[]>([]);
const treeRef = ref<any>();
const dialogVisible = ref(false);
const submitting = ref(false);
const editId = ref<number | null>(null);
const formRef = ref<any>();
const form = reactive<any>({ name: "", code: "", sort: 0, dataScope: 1, status: 1, remark: "" });
const rules = {
  name: [{ required: true, message: "请输入角色名称", trigger: "blur" }],
  code: [{ required: true, message: "请输入角色编码", trigger: "blur" }]
};
const treeProps = { label: "title", children: "children", disabled: "disabled" };
const menuCount = computed(() => menuTree.value.length);

function toMenuTree(rows: any[]): MenuNode[] {
  const nodes = new Map<number, MenuNode>();
  rows.forEach(row => nodes.set(Number(row.id), { ...row, id: Number(row.id), parentId: Number(row.parentId || 0), children: [] }));
  const roots: MenuNode[] = [];
  nodes.forEach(node => {
    const parent = nodes.get(node.parentId);
    if (parent && parent.id !== node.id) parent.children!.push(node);
    else roots.push(node);
  });
  const sortNodes = (items: MenuNode[]) => {
    items.sort((a, b) => (a.sort ?? 0) - (b.sort ?? 0) || a.id - b.id);
    items.forEach(item => sortNodes(item.children ?? []));
  };
  sortNodes(roots);
  return roots;
}

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/role", { params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined } });
    if (res?.code === 0) {
      list.value = res.data?.list ?? [];
      total.value = res.data?.total ?? 0;
    } else message(res?.message || "角色加载失败", { type: "error" });
  } catch (error: any) {
    message(error?.message || "角色加载失败", { type: "error" });
  } finally {
    loading.value = false;
  }
}

async function fetchMenus() {
  menuLoading.value = true;
  try {
    const res: any = await http.request("get", "/menu");
    if (res?.code === 0) menuTree.value = toMenuTree(res.data ?? []);
    else message(res?.message || "菜单权限加载失败", { type: "error" });
  } catch (error: any) {
    message(error?.message || "菜单权限加载失败", { type: "error" });
  } finally {
    menuLoading.value = false;
  }
}

function resetForm() {
  Object.assign(form, { name: "", code: "", sort: 0, dataScope: 1, status: 1, remark: "" });
}

async function openCreate() {
  editId.value = null;
  resetForm();
  dialogVisible.value = true;
  await nextTick();
  treeRef.value?.setCheckedKeys([]);
}

async function openEdit(row: any) {
  try {
    const res: any = await http.request("get", `/role/${row.id}`);
    if (res?.code !== 0 || !res.data) {
      message(res?.message || "角色详情加载失败", { type: "error" });
      return;
    }
    const detail = res.data;
    editId.value = Number(row.id);
    Object.assign(form, { name: detail.name ?? "", code: detail.code ?? "", sort: detail.sort ?? 0, dataScope: detail.dataScope ?? 1, status: detail.status ?? 1, remark: detail.remark ?? "" });
    dialogVisible.value = true;
    await nextTick();
    treeRef.value?.setCheckedKeys((detail.menuIds ?? []).map((id: any) => Number(id)));
  } catch (error: any) {
    message(error?.message || "角色详情加载失败", { type: "error" });
  }
}

function selectedMenuIds(): number[] {
  const checked = treeRef.value?.getCheckedKeys(false) ?? [];
  const halfChecked = treeRef.value?.getHalfCheckedKeys() ?? [];
  return [...new Set([...checked, ...halfChecked].map((id: any) => Number(id)))];
}

async function save() {
  const valid = await formRef.value?.validate().catch(() => false);
  if (!valid) return;
  submitting.value = true;
  try {
    const payload = { name: form.name, code: form.code, sort: Number(form.sort || 0), dataScope: Number(form.dataScope), status: Number(form.status), remark: form.remark || "", menuIds: selectedMenuIds() };
    const res: any = editId.value
      ? await http.request("put", `/role/${editId.value}`, { data: payload })
      : await http.request("post", "/role", { data: payload });
    if (res?.code === 0) {
      message(editId.value ? "角色修改成功" : "角色新增成功", { type: "success" });
      dialogVisible.value = false;
      await fetchList();
    } else message(res?.message || "保存失败", { type: "error" });
  } catch (error: any) {
    message(error?.response?.data?.message || error?.message || "保存失败", { type: "error" });
  } finally {
    submitting.value = false;
  }
}

async function remove(row: any) {
  try {
    await ElMessageBox.confirm(`确定删除角色「${row.name}」吗？`, "删除确认", { type: "warning" });
  } catch { return; }
  try {
    const res: any = await http.request("delete", `/role/${row.id}`);
    if (res?.code === 0) {
      message("角色已删除", { type: "success" });
      if (list.value.length === 1 && page.value > 1) page.value -= 1;
      await fetchList();
    } else message(res?.message || "删除失败", { type: "error" });
  } catch (error: any) {
    message(error?.response?.data?.message || error?.message || "删除失败", { type: "error" });
  }
}

function search() { page.value = 1; fetchList(); }
function resetSearch() { keyword.value = ""; search(); }

onMounted(() => { fetchList(); fetchMenus(); });
</script>

<template>
  <div class="role-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字">
          <el-input v-model="keyword" clearable placeholder="角色名称 / 编码" style="width: 240px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
          <el-button @click="resetSearch">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div class="toolbar">
        <div class="toolbar-title">角色列表 <span class="subtle">共 {{ total }} 个角色</span></div>
        <el-button type="primary" @click="openCreate">新增角色</el-button>
      </div>
      <el-table v-loading="loading" :data="list" border stripe row-key="id">
        <el-table-column prop="name" label="角色名称" min-width="150" show-overflow-tooltip />
        <el-table-column prop="code" label="角色编码" min-width="150" show-overflow-tooltip />
        <el-table-column prop="sort" label="排序" width="80" align="center" />
        <el-table-column label="数据范围" width="130">
          <template #default="{ row }">{{ ({ 1: "全部数据", 2: "本部门", 3: "仅本人" } as any)[row.dataScope] || "自定义" }}</template>
        </el-table-column>
        <el-table-column label="状态" width="90" align="center">
          <template #default="{ row }"><el-tag :type="row.status === 1 ? 'success' : 'info'" size="small">{{ row.status === 1 ? "启用" : "停用" }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="remark" label="备注" min-width="170" show-overflow-tooltip />
        <el-table-column label="操作" width="140" fixed="right" align="center">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑权限</el-button>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="pager">
        <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next, jumper" background @current-change="fetchList" @size-change="search" />
      </div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editId ? '编辑角色与菜单权限' : '新增角色与菜单权限'" width="860px" destroy-on-close>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="100px">
        <el-row :gutter="16">
          <el-col :span="12"><el-form-item label="角色名称" prop="name"><el-input v-model="form.name" maxlength="50" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="角色编码" prop="code"><el-input v-model="form.code" maxlength="50" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="排序"><el-input-number v-model="form.sort" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="数据范围"><el-select v-model="form.dataScope" style="width: 100%"><el-option label="全部数据" :value="1" /><el-option label="本部门数据" :value="2" /><el-option label="仅本人数据" :value="3" /></el-select></el-form-item></el-col>
          <el-col :span="8"><el-form-item label="状态"><el-switch v-model="form.status" :active-value="1" :inactive-value="0" active-text="启用" inactive-text="停用" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item label="备注"><el-input v-model="form.remark" type="textarea" :rows="2" maxlength="200" show-word-limit /></el-form-item></el-col>
        </el-row>
        <el-form-item label="菜单权限">
          <div class="permission-box" v-loading="menuLoading">
            <div class="permission-hint">勾选目录、菜单或按钮权限（{{ menuCount }} 个顶层目录）；选择子项时会一并保存其上级节点。</div>
            <el-tree ref="treeRef" :data="menuTree" :props="treeProps" node-key="id" show-checkbox check-on-click-node default-expand-all :expand-on-click-node="false" empty-text="暂无菜单数据" />
          </div>
        </el-form-item>
      </el-form>
      <template #footer><el-button @click="dialogVisible = false">取消</el-button><el-button type="primary" :loading="submitting" @click="save">保存</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.role-page {
  .search-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .toolbar-title { font-size: 16px; font-weight: 600; }
  .subtle { margin-left: 8px; color: var(--el-text-color-secondary); font-size: 13px; font-weight: 400; }
  .pager { display: flex; justify-content: flex-end; margin-top: 16px; }
  .permission-box { width: 100%; max-height: 430px; overflow: auto; border: 1px solid var(--el-border-color); border-radius: 4px; padding: 10px 12px; }
  .permission-hint { margin-bottom: 8px; color: var(--el-text-color-secondary); font-size: 12px; }
}
</style>
