<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";
import { useLookupStore } from "@/store/modules/lookup";
import { inspectTypeDict } from "@/utils/biz";

const lookup = useLookupStore();
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const inspectionType = ref<number | undefined>();
const dialogVisible = ref(false);
const editId = ref<number | null>(null);
const saving = ref(false);
const formRef = ref();
const qualityItems = ref<any[]>([]);
const form = reactive<any>({
  name: "",
  inspectionType: 1,
  productId: undefined,
  aqlLevel: "1.0",
  samplePlan: "GB/T 2828.1 一般检验水平 II",
  isDefault: true,
  remark: "",
  items: [] as any[]
});

const typeOptions = computed(() =>
  Object.keys(inspectTypeDict).map(k => ({ label: inspectTypeDict[Number(k)].label, value: Number(k) }))
);

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/quality-scheme", {
      params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined, inspectionType: inspectionType.value }
    });
    if (res?.code === 0) {
      list.value = res.data.list;
      total.value = res.data.total;
    }
  } finally {
    loading.value = false;
  }
}

async function loadItems() {
  const res: any = await http.request("get", "/quality-item/options");
  qualityItems.value = res?.data ?? [];
}

function openCreate() {
  editId.value = null;
  Object.assign(form, {
    name: "",
    inspectionType: 1,
    productId: undefined,
    aqlLevel: "1.0",
    samplePlan: "GB/T 2828.1 一般检验水平 II",
    isDefault: true,
    remark: "",
    items: [{ itemId: undefined, standard: "", minValue: null, maxValue: null }]
  });
  dialogVisible.value = true;
}

async function openEdit(row: any) {
  const res: any = await http.request("get", `/quality-scheme/${row.id}`);
  if (res?.code !== 0) return;
  const d = res.data;
  editId.value = d.id;
  Object.assign(form, {
    name: d.name,
    inspectionType: d.inspectionType,
    productId: d.productId,
    aqlLevel: d.aqlLevel,
    samplePlan: d.samplePlan,
    isDefault: d.isDefault,
    remark: d.remark,
    items: (d.items ?? []).map((x: any) => ({ itemId: x.itemId, standard: x.standard, minValue: x.minValue, maxValue: x.maxValue }))
  });
  dialogVisible.value = true;
}

function onItemChange(line: any) {
  const item = qualityItems.value.find(x => x.id === line.itemId);
  if (item && !line.standard) {
    line.standard = item.extra ?? "";
  }
}

function addLine() {
  form.items.push({ itemId: undefined, standard: "", minValue: null, maxValue: null });
}

function removeLine(index: number) {
  form.items.splice(index, 1);
}

async function save() {
  await formRef.value?.validate(async (valid: boolean) => {
    if (!valid) return;
    if (!form.items.length || form.items.some((x: any) => !x.itemId)) return message("请配置检验项目", { type: "warning" });
    saving.value = true;
    try {
      const payload = { ...form, id: editId.value ?? 0, code: editId.value ? undefined : "" };
      const res: any = editId.value
        ? await http.request("put", `/quality-scheme/${editId.value}`, { data: payload })
        : await http.request("post", "/quality-scheme", { data: payload });
      if (res?.code === 0) {
        message("保存成功", { type: "success" });
        dialogVisible.value = false;
        fetchList();
      } else {
        message(res?.message || "保存失败", { type: "error" });
      }
    } finally {
      saving.value = false;
    }
  });
}

async function remove(row: any) {
  try {
    await ElMessageBox.confirm(`确定删除方案「${row.name}」吗？`, "提示", { type: "warning" });
  } catch {
    return;
  }
  const res: any = await http.request("delete", `/quality-scheme/${row.id}`);
  message(res?.code === 0 ? "删除成功" : res?.message || "删除失败", { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}

function typeLabel(t: number) {
  return inspectTypeDict[t]?.label ?? t;
}

function search() {
  page.value = 1;
  fetchList();
}

onMounted(async () => {
  await lookup.load();
  loadItems();
  fetchList();
});
</script>

<template>
  <div class="scheme-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字">
          <el-input v-model="keyword" clearable placeholder="方案编号 / 名称" style="width: 200px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="检验类型">
          <el-select v-model="inspectionType" clearable placeholder="全部" style="width: 150px">
            <el-option v-for="t in typeOptions" :key="t.value" :label="t.label" :value="t.value" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
          <el-button @click="() => { keyword = ''; inspectionType = undefined; search(); }">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div class="toolbar">
        <div class="title">检验方案</div>
        <el-button type="primary" @click="openCreate">新增方案</el-button>
      </div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="方案编号" width="140" />
        <el-table-column prop="name" label="方案名称" min-width="180" />
        <el-table-column label="检验类型" width="110">
          <template #default="{ row }">
            <el-tag size="small" :type="(inspectTypeDict[row.inspectionType]?.type as any) || 'info'">{{ typeLabel(row.inspectionType) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="适用产品" min-width="160">
          <template #default="{ row }">{{ row.productId ? lookup.name("products", row.productId) : "通用" }}</template>
        </el-table-column>
        <el-table-column prop="aqlLevel" label="AQL" width="80" />
        <el-table-column prop="samplePlan" label="抽样方案" min-width="180" show-overflow-tooltip />
        <el-table-column label="默认" width="80">
          <template #default="{ row }">
            <el-tag v-if="row.isDefault" type="success" size="small">默认</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="140" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="danger" @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
      <div class="pager">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          :page-sizes="[10, 20, 50]"
          layout="total, sizes, prev, pager, next"
          background
          @current-change="fetchList"
          @size-change="search"
        />
      </div>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editId ? '编辑检验方案' : '新增检验方案'" width="880px" destroy-on-close>
      <el-form ref="formRef" :model="form" label-width="100px">
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="方案名称" required>
              <el-input v-model="form.name" placeholder="如：来料检验方案" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="检验类型" required>
              <el-select v-model="form.inspectionType" style="width: 100%">
                <el-option v-for="t in typeOptions" :key="t.value" :label="t.label" :value="t.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="适用产品">
              <el-select v-model="form.productId" clearable filterable style="width: 100%">
                <el-option v-for="p in lookup.options('products')" :key="p.value" :label="p.label" :value="p.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item label="AQL">
              <el-input v-model="form.aqlLevel" />
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item label="默认">
              <el-switch v-model="form.isDefault" />
            </el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item label="抽样方案">
              <el-input v-model="form.samplePlan" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-divider content-position="left">检验项目</el-divider>
        <el-table :data="form.items" border size="small">
          <el-table-column label="检验项目" min-width="200">
            <template #default="{ row }">
              <el-select v-model="row.itemId" filterable style="width: 100%" @change="onItemChange(row)">
                <el-option v-for="i in qualityItems" :key="i.id" :label="`${i.code} ${i.name}`" :value="i.id" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="标准要求" min-width="200">
            <template #default="{ row }">
              <el-input v-model="row.standard" placeholder="标准要求" />
            </template>
          </el-table-column>
          <el-table-column label="下限" width="120">
            <template #default="{ row }">
              <el-input-number v-model="row.minValue" :precision="3" controls-position="right" style="width: 100%" />
            </template>
          </el-table-column>
          <el-table-column label="上限" width="120">
            <template #default="{ row }">
              <el-input-number v-model="row.maxValue" :precision="3" controls-position="right" style="width: 100%" />
            </template>
          </el-table-column>
          <el-table-column width="60" align="center">
            <template #default="{ $index }">
              <el-button link type="danger" @click="removeLine($index)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
        <div class="line-footer">
          <el-button size="small" @click="addLine">添加项目</el-button>
        </div>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.scheme-page {
  .search-card {
    margin-bottom: 12px;
    :deep(.el-form-item) { margin-bottom: 0; }
  }
  .toolbar {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 12px;
    .title { font-size: 16px; font-weight: 600; }
  }
  .pager {
    display: flex;
    justify-content: flex-end;
    margin-top: 16px;
  }
  .line-footer {
    margin-top: 10px;
  }
}
</style>
