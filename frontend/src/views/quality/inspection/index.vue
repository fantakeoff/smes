<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";
import { useLookupStore } from "@/store/modules/lookup";
import { inspectResultDict, inspectTypeDict } from "@/utils/biz";

const lookup = useLookupStore();
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const inspectionType = ref<number | undefined>();
const result = ref<number | undefined>();
const qualityItems = ref<any[]>([]);

const dialogVisible = ref(false);
const editId = ref<number | null>(null);
const saving = ref(false);
const formRef = ref();
const form = reactive<any>({
  inspectionType: 1,
  sourceType: "采购订单",
  sourceCode: "",
  productId: undefined,
  batchNo: "",
  qty: 0,
  sampleQty: 0,
  qualifiedQty: 0,
  defectQty: 0,
  result: 0,
  remark: "",
  lines: [] as any[]
});

const judgeVisible = ref(false);
const judgeRow = ref<any>(null);
const judgeJobId = ref<number | null>(null);
const judgeJobStatus = ref("");
const judgeRequestKey = ref("");
const judgeForm = reactive<any>({ result: 1, qualifiedQty: 0, defectQty: 0, defectItemId: undefined, remark: "" });

const detailVisible = ref(false);
const detail = ref<any>(null);

const typeOptions = computed(() =>
  Object.keys(inspectTypeDict).map(k => ({ label: inspectTypeDict[Number(k)].label, value: Number(k) }))
);
const resultOptions = computed(() =>
  Object.keys(inspectResultDict).map(k => ({ label: inspectResultDict[Number(k)].label, value: Number(k) }))
);

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/quality-inspection", {
      params: {
        page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined,
        inspectionType: inspectionType.value, result: result.value
      }
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
    inspectionType: 1, sourceType: "采购订单", sourceCode: "", productId: undefined,
    batchNo: "", qty: 0, sampleQty: 0, qualifiedQty: 0, defectQty: 0, result: 0, remark: "",
    lines: [{ itemId: undefined, standard: "", actualValue: "", result: 1 }]
  });
  dialogVisible.value = true;
}

async function openEdit(row: any) {
  const res: any = await http.request("get", `/quality-inspection/${row.id}`);
  if (res?.code !== 0) return;
  const d = res.data;
  editId.value = d.id;
  Object.assign(form, {
    inspectionType: d.inspectionType, sourceType: d.sourceType, sourceCode: d.sourceCode,
    productId: d.productId, batchNo: d.batchNo, qty: d.qty, sampleQty: d.sampleQty,
    qualifiedQty: d.qualifiedQty, defectQty: d.defectQty, result: d.result, remark: d.remark,
    lines: (d.lines ?? []).map((l: any) => ({ itemId: l.itemId, standard: l.standard, actualValue: l.actualValue, result: l.result }))
  });
  dialogVisible.value = true;
}

function addLine() {
  form.lines.push({ itemId: undefined, standard: "", actualValue: "", result: 1 });
}

function removeLine(i: number) {
  form.lines.splice(i, 1);
}

async function save() {
  await formRef.value?.validate(async (valid: boolean) => {
    if (!valid) return;
    if (!form.productId) return message("请选择产品", { type: "warning" });
    saving.value = true;
    try {
      const payload = { ...form, id: editId.value ?? 0 };
      const res: any = editId.value
        ? await http.request("put", `/quality-inspection/${editId.value}`, { data: payload })
        : await http.request("post", "/quality-inspection", { data: payload });
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

async function openDetail(row: any) {
  const res: any = await http.request("get", `/quality-inspection/${row.id}`);
  if (res?.code === 0) {
    detail.value = res.data;
    detailVisible.value = true;
  }
}

function openJudge(row: any) {
  judgeRow.value = row;
  judgeRequestKey.value = crypto.randomUUID();
  judgeJobId.value = null;
  judgeJobStatus.value = "";
  Object.assign(judgeForm, {
    result: row.result === 0 ? 1 : row.result,
    qualifiedQty: row.qualifiedQty || row.sampleQty,
    defectQty: row.defectQty || 0,
    defectItemId: row.defectItemId,
    remark: row.remark
  });
  judgeVisible.value = true;
}

async function pollJudgeJob(jobId: number) {
  for (let i = 0; i < 20; i++) {
    await new Promise(resolve => setTimeout(resolve, 1500));
    try {
      const res: any = await http.request("get", `/quality-inspection/jobs/${jobId}`);
      if (res?.code !== 0) continue;
      judgeJobStatus.value = res.data?.status || "queued";
      if (judgeJobStatus.value === "completed") {
        message("检验判定已消费完成", { type: "success" });
        fetchList();
        return;
      }
      if (judgeJobStatus.value === "failed" || judgeJobStatus.value === "dead-lettered") {
        message(res.data?.error || "质检任务已进入死信队列，请联系管理员", { type: "error" });
        return;
      }
    } catch { /* 网络断开时任务仍保留在服务端，继续查询 */ }
  }
  message(`质检任务 ${jobId} 仍在处理中，请稍后查询状态`, { type: "warning" });
}
async function submitJudge() {
  if (judgeJobId.value) return message("任务已提交，请勿重复判定", { type: "warning" });
  try {
    const res: any = await http.request("post", `/quality-inspection/${judgeRow.value.id}/judge`, {
      data: judgeForm, headers: { "Idempotency-Key": judgeRequestKey.value }
    });
    if (res?.code !== 0 || !res.data?.jobId) return message(res?.message || "任务提交失败", { type: "error" });
    judgeJobId.value = Number(res.data.jobId);
    judgeJobStatus.value = res.data.status || "queued";
    message(`质检任务 ${judgeJobId.value} 已受理，等待消费`, { type: "info" });
    judgeVisible.value = false;
    void pollJudgeJob(judgeJobId.value);
  } catch (error: any) { message(error?.message || "提交失败，请用相同任务键重试", { type: "error" }); }
}

async function remove(row: any) {
  try {
    await ElMessageBox.confirm(`确定删除检验单「${row.code}」吗？`, "提示", { type: "warning" });
  } catch {
    return;
  }
  const res: any = await http.request("delete", `/quality-inspection/${row.id}`);
  message(res?.code === 0 ? "删除成功" : res?.message || "删除失败", { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}

function typeTag(t: number) {
  return (inspectTypeDict[t]?.type as any) || "info";
}

function itemName(id: number) {
  const item = qualityItems.value.find(x => x.id === id);
  return item ? `${item.code} ${item.name}` : String(id ?? "-");
}

function resultTag(r: number) {
  return (inspectResultDict[r]?.type as any) || "info";
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
  <div class="inspection-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字">
          <el-input v-model="keyword" clearable placeholder="检验单号 / 来源单号 / 批次" style="width: 230px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="检验类型">
          <el-select v-model="inspectionType" clearable placeholder="全部" style="width: 150px">
            <el-option v-for="t in typeOptions" :key="t.value" :label="t.label" :value="t.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="结果">
          <el-select v-model="result" clearable placeholder="全部" style="width: 120px">
            <el-option v-for="t in resultOptions" :key="t.value" :label="t.label" :value="t.value" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
          <el-button @click="() => { keyword = ''; inspectionType = undefined; result = undefined; search(); }">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div class="toolbar">
        <div class="title">检验任务</div>
        <el-button type="primary" @click="openCreate">新建检验单</el-button>
      </div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="检验单号" width="150" fixed />
        <el-table-column label="类型" width="100">
          <template #default="{ row }">
            <el-tag size="small" :type="typeTag(row.inspectionType)">{{ inspectTypeDict[row.inspectionType]?.label }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="产品" min-width="170" show-overflow-tooltip>
          <template #default="{ row }">{{ lookup.name("products", row.productId) }}</template>
        </el-table-column>
        <el-table-column prop="batchNo" label="批次号" width="120" />
        <el-table-column prop="qty" label="送检数量" width="100" align="right" />
        <el-table-column prop="sampleQty" label="抽样数" width="90" align="right" />
        <el-table-column prop="defectQty" label="不良数" width="90" align="right" />
        <el-table-column label="结果" width="100">
          <template #default="{ row }">
            <el-tag size="small" :type="resultTag(row.result)">{{ inspectResultDict[row.result]?.label }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="来源" width="150" show-overflow-tooltip>
          <template #default="{ row }">{{ row.sourceType }} {{ row.sourceCode }}</template>
        </el-table-column>
        <el-table-column label="操作" width="210" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openDetail(row)">详情</el-button>
            <el-button link type="warning" @click="openJudge(row)">判定</el-button>
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

    <el-dialog v-model="dialogVisible" :title="editId ? '编辑检验单' : '新建检验单'" width="900px" destroy-on-close top="6vh">
      <el-form ref="formRef" :model="form" label-width="100px">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="检验类型" required>
              <el-select v-model="form.inspectionType" style="width: 100%">
                <el-option v-for="t in typeOptions" :key="t.value" :label="t.label" :value="t.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="来源类型">
              <el-select v-model="form.sourceType" style="width: 100%">
                <el-option label="采购订单" value="采购订单" />
                <el-option label="生产工单" value="生产工单" />
                <el-option label="销售订单" value="销售订单" />
                <el-option label="手工" value="手工" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="来源单号">
              <el-input v-model="form.sourceCode" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="产品" required>
              <el-select v-model="form.productId" filterable style="width: 100%">
                <el-option v-for="p in lookup.options('products')" :key="p.value" :label="p.label" :value="p.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="批次号">
              <el-input v-model="form.batchNo" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="送检数量">
              <el-input-number v-model="form.qty" :min="0" controls-position="right" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="抽样数量">
              <el-input-number v-model="form.sampleQty" :min="0" controls-position="right" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="合格数量">
              <el-input-number v-model="form.qualifiedQty" :min="0" controls-position="right" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="不良数量">
              <el-input-number v-model="form.defectQty" :min="0" controls-position="right" style="width: 100%" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-divider content-position="left">检验记录</el-divider>
        <el-table :data="form.lines" border size="small">
          <el-table-column label="检验项目" min-width="200">
            <template #default="{ row }">
              <el-select v-model="row.itemId" filterable style="width: 100%">
                <el-option v-for="i in qualityItems" :key="i.id" :label="`${i.code} ${i.name}`" :value="i.id" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="标准" min-width="150">
            <template #default="{ row }"><el-input v-model="row.standard" /></template>
          </el-table-column>
          <el-table-column label="实测值" min-width="140">
            <template #default="{ row }"><el-input v-model="row.actualValue" /></template>
          </el-table-column>
          <el-table-column label="判定" width="120">
            <template #default="{ row }">
              <el-select v-model="row.result">
                <el-option label="合格" :value="1" />
                <el-option label="不合格" :value="2" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column width="60" align="center">
            <template #default="{ $index }">
              <el-button link type="danger" @click="removeLine($index)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
        <div class="line-footer"><el-button size="small" @click="addLine">添加项目</el-button></div>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="judgeVisible" title="检验判定" width="560px" destroy-on-close>
      <el-form label-width="100px">
        <el-form-item label="判定结果">
          <el-radio-group v-model="judgeForm.result">
            <el-radio :value="1">合格</el-radio>
            <el-radio :value="2">不合格</el-radio>
            <el-radio :value="3">让步接收</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="合格数量">
          <el-input-number v-model="judgeForm.qualifiedQty" :min="0" controls-position="right" />
        </el-form-item>
        <el-form-item label="不良数量">
          <el-input-number v-model="judgeForm.defectQty" :min="0" controls-position="right" />
        </el-form-item>
        <el-form-item v-if="judgeForm.result !== 1" label="不良项">
          <el-select v-model="judgeForm.defectItemId" clearable filterable style="width: 100%">
            <el-option v-for="d in lookup.options('defects')" :key="d.value" :label="d.label" :value="d.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="judgeForm.remark" type="textarea" :rows="2" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="judgeVisible = false">取消</el-button>
        <el-button type="primary" @click="submitJudge">保存判定</el-button>
      </template>
    </el-dialog>

    <el-drawer v-model="detailVisible" title="检验单详情" size="55%">
      <template v-if="detail">
        <el-descriptions :column="3" border>
          <el-descriptions-item label="检验单号">{{ detail.code }}</el-descriptions-item>
          <el-descriptions-item label="类型">{{ inspectTypeDict[detail.inspectionType]?.label }}</el-descriptions-item>
          <el-descriptions-item label="结果">
            <el-tag :type="resultTag(detail.result)" size="small">{{ inspectResultDict[detail.result]?.label }}</el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="产品">{{ lookup.name("products", detail.productId) }}</el-descriptions-item>
          <el-descriptions-item label="批次">{{ detail.batchNo || "-" }}</el-descriptions-item>
          <el-descriptions-item label="送检数">{{ detail.qty }}</el-descriptions-item>
          <el-descriptions-item label="抽样数">{{ detail.sampleQty }}</el-descriptions-item>
          <el-descriptions-item label="不良数">{{ detail.defectQty }}</el-descriptions-item>
          <el-descriptions-item label="来源">{{ detail.sourceType }} {{ detail.sourceCode }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="detail.lines" border size="small" style="margin-top: 16px">
          <el-table-column label="检验项目" min-width="160">
            <template #default="{ row }">{{ itemName(row.itemId) }}</template>
          </el-table-column>
          <el-table-column prop="standard" label="标准" min-width="140" />
          <el-table-column prop="actualValue" label="实测值" width="120" />
          <el-table-column label="判定" width="90">
            <template #default="{ row }">
              <el-tag :type="row.result === 1 ? 'success' : 'danger'" size="small">{{ row.result === 1 ? "合格" : "不合格" }}</el-tag>
            </template>
          </el-table-column>
        </el-table>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped lang="scss">
.inspection-page {
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
  .line-footer { margin-top: 10px; }
}
</style>
