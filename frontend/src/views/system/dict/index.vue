<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";

const typeLoading = ref(false);
const itemLoading = ref(false);
const types = ref<any[]>([]);
const items = ref<any[]>([]);
const selectedTypeId = ref<number | null>(null);
const typeKeyword = ref("");
const itemKeyword = ref("");
const typeDialogVisible = ref(false);
const itemDialogVisible = ref(false);
const typeSubmitting = ref(false);
const itemSubmitting = ref(false);
const typeEditId = ref<number | null>(null);
const itemEditId = ref<number | null>(null);
const typeFormRef = ref<any>();
const itemFormRef = ref<any>();
const typeForm = reactive<any>({ name: "", code: "", description: "", status: 1 });
const itemForm = reactive<any>({ label: "", value: "", sort: 0, tagType: "info", status: 1, remark: "" });
const typeRules = { name: [{ required: true, message: "请输入字典名称", trigger: "blur" }], code: [{ required: true, message: "请输入字典编码", trigger: "blur" }] };
const itemRules = { label: [{ required: true, message: "请输入字典标签", trigger: "blur" }], value: [{ required: true, message: "请输入字典值", trigger: "blur" }] };
const filteredTypes = computed(() => {
  const q = typeKeyword.value.trim().toLowerCase();
  return q ? types.value.filter(row => `${row.name ?? ""} ${row.code ?? ""}`.toLowerCase().includes(q)) : types.value;
});
const filteredItems = computed(() => {
  const q = itemKeyword.value.trim().toLowerCase();
  return q ? items.value.filter(row => `${row.label ?? ""} ${row.value ?? ""}`.toLowerCase().includes(q)) : items.value;
});
const selectedType = computed(() => types.value.find(row => Number(row.id) === Number(selectedTypeId.value)));

async function fetchItems() {
  if (!selectedTypeId.value) { items.value = []; return; }
  itemLoading.value = true;
  try {
    const res: any = await http.request("get", "/dict/item", { params: { dictTypeId: selectedTypeId.value } });
    if (res?.code === 0) items.value = res.data ?? [];
    else message(res?.message || "字典项加载失败", { type: "error" });
  } catch (error: any) { message(error?.message || "字典项加载失败", { type: "error" }); }
  finally { itemLoading.value = false; }
}
async function fetchTypes() {
  typeLoading.value = true;
  try {
    const res: any = await http.request("get", "/dict/type");
    if (res?.code !== 0) { message(res?.message || "字典类型加载失败", { type: "error" }); return; }
    types.value = res.data ?? [];
    const stillExists = types.value.some(row => Number(row.id) === Number(selectedTypeId.value));
    if (!stillExists) selectedTypeId.value = types.value.length ? Number(types.value[0].id) : null;
    await fetchItems();
  } catch (error: any) { message(error?.message || "字典类型加载失败", { type: "error" }); }
  finally { typeLoading.value = false; }
}
function selectType(row: any) { selectedTypeId.value = Number(row.id); itemKeyword.value = ""; fetchItems(); }
function openCreateType() {
  typeEditId.value = null;
  Object.assign(typeForm, { name: "", code: "", description: "", status: 1 });
  typeDialogVisible.value = true;
}
function openEditType(row: any) {
  typeEditId.value = Number(row.id);
  Object.assign(typeForm, { name: row.name ?? "", code: row.code ?? "", description: row.description ?? "", status: Number(row.status ?? 1) });
  typeDialogVisible.value = true;
}
async function saveType() {
  const valid = await typeFormRef.value?.validate().catch(() => false);
  if (!valid) return;
  typeSubmitting.value = true;
  try {
    const payload = { name: typeForm.name.trim(), code: typeForm.code.trim(), description: typeForm.description || "", status: Number(typeForm.status) };
    const res: any = typeEditId.value
      ? await http.request("put", `/dict/type/${typeEditId.value}`, { data: payload })
      : await http.request("post", "/dict/type", { data: payload });
    if (res?.code === 0) {
      message(typeEditId.value ? "字典类型已修改" : "字典类型已新增", { type: "success" });
      typeDialogVisible.value = false;
      await fetchTypes();
    } else message(res?.message || "保存失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "保存失败", { type: "error" }); }
  finally { typeSubmitting.value = false; }
}
async function removeType(row: any) {
  try { await ElMessageBox.confirm(`确定删除字典类型「${row.name}」吗？`, "删除确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("delete", `/dict/type/${row.id}`);
    if (res?.code === 0) {
      message("字典类型已删除", { type: "success" });
      if (Number(selectedTypeId.value) === Number(row.id)) selectedTypeId.value = null;
      await fetchTypes();
    } else message(res?.message || "删除失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "删除失败", { type: "error" }); }
}
function openCreateItem() {
  if (!selectedTypeId.value) return;
  itemEditId.value = null;
  Object.assign(itemForm, { label: "", value: "", sort: 0, tagType: "info", status: 1, remark: "" });
  itemDialogVisible.value = true;
}
function openEditItem(row: any) {
  itemEditId.value = Number(row.id);
  Object.assign(itemForm, { label: row.label ?? "", value: row.value ?? "", sort: Number(row.sort ?? 0), tagType: row.tagType || "info", status: Number(row.status ?? 1), remark: row.remark ?? "" });
  itemDialogVisible.value = true;
}
async function saveItem() {
  const valid = await itemFormRef.value?.validate().catch(() => false);
  if (!valid || !selectedTypeId.value) return;
  itemSubmitting.value = true;
  try {
    const payload = { dictTypeId: selectedTypeId.value, label: itemForm.label.trim(), value: itemForm.value.trim(), sort: Number(itemForm.sort || 0), tagType: itemForm.tagType || null, status: Number(itemForm.status), remark: itemForm.remark || "" };
    const res: any = itemEditId.value
      ? await http.request("put", `/dict/item/${itemEditId.value}`, { data: payload })
      : await http.request("post", "/dict/item", { data: payload });
    if (res?.code === 0) {
      message(itemEditId.value ? "字典项已修改" : "字典项已新增", { type: "success" });
      itemDialogVisible.value = false;
      await fetchItems();
    } else message(res?.message || "保存失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "保存失败", { type: "error" }); }
  finally { itemSubmitting.value = false; }
}
async function removeItem(row: any) {
  try { await ElMessageBox.confirm(`确定删除字典项「${row.label}」吗？`, "删除确认", { type: "warning" }); }
  catch { return; }
  try {
    const res: any = await http.request("delete", `/dict/item/${row.id}`);
    if (res?.code === 0) { message("字典项已删除", { type: "success" }); await fetchItems(); }
    else message(res?.message || "删除失败", { type: "error" });
  } catch (error: any) { message(error?.response?.data?.message || error?.message || "删除失败", { type: "error" }); }
}

onMounted(fetchTypes);
</script>

<template>
  <div class="dict-page">
    <el-row :gutter="12" class="dict-columns">
      <el-col :xs="24" :md="9" :lg="8">
        <el-card shadow="never" class="panel-card">
          <div class="panel-toolbar"><div class="panel-title">字典类型</div><el-button type="primary" size="small" @click="openCreateType">新增类型</el-button></div>
          <el-input v-model="typeKeyword" clearable placeholder="搜索名称 / 编码" class="panel-search" />
          <el-table v-loading="typeLoading" :data="filteredTypes" row-key="id" border stripe highlight-current-row @row-click="selectType">
            <el-table-column prop="name" label="字典名称" min-width="110" show-overflow-tooltip />
            <el-table-column prop="code" label="编码" min-width="120" show-overflow-tooltip />
            <el-table-column label="操作" width="100" fixed="right" align="center">
              <template #default="{ row }"><el-button link type="primary" @click.stop="openEditType(row)">编辑</el-button><el-button link type="danger" @click.stop="removeType(row)">删除</el-button></template>
            </el-table-column>
            <template #empty><el-empty description="暂无字典类型" :image-size="60" /></template>
          </el-table>
        </el-card>
      </el-col>
      <el-col :xs="24" :md="15" :lg="16">
        <el-card shadow="never" class="panel-card">
          <div class="panel-toolbar">
            <div class="panel-title">{{ selectedType ? `${selectedType.name} - 字典项` : "字典项" }}</div>
            <el-button type="primary" size="small" :disabled="!selectedTypeId" @click="openCreateItem">新增字典项</el-button>
          </div>
          <div class="item-search"><el-input v-model="itemKeyword" clearable placeholder="搜索标签 / 字典值" style="max-width: 300px" /></div>
          <el-table v-loading="itemLoading" :data="filteredItems" row-key="id" border stripe>
            <el-table-column prop="label" label="字典标签" min-width="140" />
            <el-table-column prop="value" label="字典值" min-width="130" />
            <el-table-column label="标签样式" width="110"><template #default="{ row }"><el-tag :type="row.tagType || 'info'" size="small">{{ row.tagType || "默认" }}</el-tag></template></el-table-column>
            <el-table-column prop="sort" label="排序" width="75" align="center" />
            <el-table-column label="状态" width="80" align="center"><template #default="{ row }"><el-tag :type="row.status === 1 ? 'success' : 'info'" size="small">{{ row.status === 1 ? "启用" : "停用" }}</el-tag></template></el-table-column>
            <el-table-column label="操作" width="120" fixed="right" align="center"><template #default="{ row }"><el-button link type="primary" @click="openEditItem(row)">编辑</el-button><el-button link type="danger" @click="removeItem(row)">删除</el-button></template></el-table-column>
            <template #empty><el-empty :description="selectedType ? '该类型暂无字典项' : '请先选择字典类型'" :image-size="70" /></template>
          </el-table>
        </el-card>
      </el-col>
    </el-row>

    <el-dialog v-model="typeDialogVisible" :title="typeEditId ? '编辑字典类型' : '新增字典类型'" width="600px" destroy-on-close>
      <el-form ref="typeFormRef" :model="typeForm" :rules="typeRules" label-width="100px">
        <el-form-item label="字典名称" prop="name"><el-input v-model="typeForm.name" maxlength="80" /></el-form-item>
        <el-form-item label="字典编码" prop="code"><el-input v-model="typeForm.code" maxlength="80" /></el-form-item>
        <el-form-item label="描述"><el-input v-model="typeForm.description" type="textarea" :rows="3" /></el-form-item>
        <el-form-item label="状态"><el-switch v-model="typeForm.status" :active-value="1" :inactive-value="0" active-text="启用" /></el-form-item>
      </el-form>
      <template #footer><el-button @click="typeDialogVisible = false">取消</el-button><el-button type="primary" :loading="typeSubmitting" @click="saveType">保存</el-button></template>
    </el-dialog>

    <el-dialog v-model="itemDialogVisible" :title="itemEditId ? '编辑字典项' : '新增字典项'" width="640px" destroy-on-close>
      <el-form ref="itemFormRef" :model="itemForm" :rules="itemRules" label-width="100px">
        <el-row :gutter="12">
          <el-col :span="12"><el-form-item label="字典标签" prop="label"><el-input v-model="itemForm.label" maxlength="100" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="字典值" prop="value"><el-input v-model="itemForm.value" maxlength="100" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="标签样式"><el-select v-model="itemForm.tagType" style="width: 100%"><el-option label="默认" value="info" /><el-option label="主要" value="primary" /><el-option label="成功" value="success" /><el-option label="警告" value="warning" /><el-option label="危险" value="danger" /></el-select></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="排序"><el-input-number v-model="itemForm.sort" :min="0" controls-position="right" style="width: 100%" /></el-form-item></el-col>
          <el-col :span="12"><el-form-item label="状态"><el-switch v-model="itemForm.status" :active-value="1" :inactive-value="0" active-text="启用" /></el-form-item></el-col>
          <el-col :span="24"><el-form-item label="备注"><el-input v-model="itemForm.remark" type="textarea" :rows="2" /></el-form-item></el-col>
        </el-row>
      </el-form>
      <template #footer><el-button @click="itemDialogVisible = false">取消</el-button><el-button type="primary" :loading="itemSubmitting" @click="saveItem">保存</el-button></template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.dict-page {
  .dict-columns { align-items: stretch; }
  .panel-card { min-height: 520px; }
  .panel-toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
  .panel-title { font-size: 16px; font-weight: 600; }
  .panel-search { margin-bottom: 12px; }
  .item-search { display: flex; justify-content: flex-end; margin-bottom: 12px; }
}
</style>
