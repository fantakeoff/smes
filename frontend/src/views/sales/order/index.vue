<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";
import { useLookupStore } from "@/store/modules/lookup";
import { salesOrderStatus } from "@/utils/biz";

const lookup = useLookupStore();
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const orderStatus = ref<number | undefined>();
const customerId = ref<number | undefined>();

const dialogVisible = ref(false);
const editId = ref<number | null>(null);
const formRef = ref();
const saving = ref(false);
const form = reactive<any>({
  customerId: undefined,
  orderDate: new Date().toISOString().slice(0, 10),
  deliveryDate: "",
  paymentMethod: "月结30天",
  contact: "",
  phone: "",
  address: "",
  remark: "",
  lines: [] as any[]
});

const outboundVisible = ref(false);
const outboundOrder = ref<any>(null);
const outboundWarehouseId = ref<number | undefined>();
const outboundLines = ref<any[]>([]);

const detailVisible = ref(false);
const detail = ref<any>(null);

const customers = computed(() => lookup.options("customers"));
const products = computed(() => lookup.options("products"));
const warehouses = computed(() => lookup.options("warehouses"));

const totalAmount = computed(() => form.lines.reduce((sum: number, l: any) => sum + (Number(l.qty) || 0) * (Number(l.price) || 0), 0));

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/sales-order", {
      params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined, orderStatus: orderStatus.value, customerId: customerId.value }
    });
    if (res?.code === 0) {
      list.value = res.data.list;
      total.value = res.data.total;
    }
  } finally {
    loading.value = false;
  }
}

function statusTag(status: number) {
  const s = salesOrderStatus[status];
  return { type: (s?.type ?? "info") as any, label: s?.label ?? status };
}

function money(v: any) {
  return Number(v ?? 0).toLocaleString("zh-CN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function openCreate() {
  editId.value = null;
  Object.assign(form, {
    customerId: undefined,
    orderDate: new Date().toISOString().slice(0, 10),
    deliveryDate: "",
    paymentMethod: "月结30天",
    contact: "",
    phone: "",
    address: "",
    remark: "",
    lines: [{ productId: undefined, qty: 1, price: 0, deliveryDate: "" }]
  });
  dialogVisible.value = true;
}

async function openEdit(row: any) {
  const res: any = await http.request("get", `/sales-order/${row.id}`);
  if (res?.code !== 0) return message("订单加载失败", { type: "error" });
  const order = res.data;
  editId.value = order.id;
  Object.assign(form, {
    customerId: order.customerId,
    orderDate: String(order.orderDate).slice(0, 10),
    deliveryDate: order.deliveryDate ? String(order.deliveryDate).slice(0, 10) : "",
    paymentMethod: order.paymentMethod,
    contact: order.contact,
    phone: order.phone,
    address: order.address,
    remark: order.remark,
    lines: (order.lines ?? []).map((l: any) => ({
      productId: l.productId,
      qty: l.qty,
      price: l.price,
      deliveryDate: l.deliveryDate ? String(l.deliveryDate).slice(0, 10) : "",
      deliveredQty: l.deliveredQty
    }))
  });
  dialogVisible.value = true;
}

function onCustomerChange(id: number) {
  const c = lookup.list("customers").find(x => x.id === id);
  if (c) {
    form.contact = form.contact || "";
  }
}

function onProductChange(line: any) {
  const p = lookup.list("products").find(x => x.id === line.productId);
  if (p) {
    const product = list.value.find(() => false);
  }
}

function addLine() {
  form.lines.push({ productId: undefined, qty: 1, price: 0, deliveryDate: "" });
}

function removeLine(index: number) {
  form.lines.splice(index, 1);
}

async function save() {
  if (!formRef.value) return;
  await formRef.value.validate(async (valid: boolean) => {
    if (!valid) return;
    if (!form.lines.length) return message("请至少添加一行明细", { type: "warning" });
    if (form.lines.some((l: any) => !l.productId || !l.qty)) return message("明细中的产品和数量不能为空", { type: "warning" });
    saving.value = true;
    try {
      const payload = {
        id: editId.value ?? 0,
        customerId: form.customerId,
        orderDate: form.orderDate,
        deliveryDate: form.deliveryDate || null,
        paymentMethod: form.paymentMethod,
        contact: form.contact,
        phone: form.phone,
        address: form.address,
        remark: form.remark,
        lines: form.lines.map((l: any) => ({ productId: l.productId, qty: l.qty, price: l.price, deliveryDate: l.deliveryDate || null }))
      };
      const res: any = editId.value
        ? await http.request("put", `/sales-order/${editId.value}`, { data: payload })
        : await http.request("post", "/sales-order", { data: payload });
      if (res?.code === 0) {
        message(editId.value ? "修改成功" : "创建成功", { type: "success" });
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

async function confirm(row: any) {
  try {
    await ElMessageBox.confirm(`确认销售订单「${row.code}」吗？`, "提示", { type: "warning" });
  } catch {
    return;
  }
  const res: any = await http.request("post", `/sales-order/${row.id}/confirm`);
  message(res?.code === 0 ? "订单已确认" : res?.message || "操作失败", { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}

async function cancel(row: any) {
  try {
    await ElMessageBox.confirm(`确定取消订单「${row.code}」吗？`, "提示", { type: "warning" });
  } catch {
    return;
  }
  const res: any = await http.request("post", `/sales-order/${row.id}/cancel`);
  message(res?.code === 0 ? "订单已取消" : res?.message || "操作失败", { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}

async function openDetail(row: any) {
  const res: any = await http.request("get", `/sales-order/${row.id}`);
  if (res?.code === 0) {
    detail.value = res.data;
    detailVisible.value = true;
  }
}

async function openOutbound(row: any) {
  const res: any = await http.request("get", `/sales-order/${row.id}`);
  if (res?.code !== 0) return;
  outboundOrder.value = res.data;
  outboundWarehouseId.value = lookup.list("warehouses").find(w => w.code === "WH02")?.id ?? warehouses.value[0]?.value;
  outboundLines.value = (res.data.lines ?? []).map((l: any) => ({
    lineId: l.id,
    productId: l.productId,
    productName: lookup.name("products", l.productId),
    qty: Math.max(0, l.qty - (l.deliveredQty ?? 0)),
    maxQty: Math.max(0, l.qty - (l.deliveredQty ?? 0)),
    price: l.price
  }));
  outboundVisible.value = true;
}

async function submitOutbound() {
  const items = outboundLines.value.filter(l => l.qty > 0).map(l => ({ lineId: l.lineId, qty: l.qty }));
  if (!items.length) return message("请填写出库数量", { type: "warning" });
  if (!outboundWarehouseId.value) return message("请选择出库仓库", { type: "warning" });
  const res: any = await http.request("post", `/sales-order/${outboundOrder.value.id}/outbound`, {
    data: { warehouseId: outboundWarehouseId.value, items }
  });
  message(res?.code === 0 ? "出库成功，已生成出库单" : res?.message || "出库失败", { type: res?.code === 0 ? "success" : "error" });
  if (res?.code === 0) {
    outboundVisible.value = false;
    fetchList();
  }
}

function search() {
  page.value = 1;
  fetchList();
}

onMounted(async () => {
  await lookup.load();
  fetchList();
});
</script>

<template>
  <div class="order-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字">
          <el-input v-model="keyword" clearable placeholder="订单号 / 客户" style="width: 200px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="客户">
          <el-select v-model="customerId" clearable filterable placeholder="全部" style="width: 220px">
            <el-option v-for="c in customers" :key="c.value" :label="c.label" :value="c.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="orderStatus" clearable placeholder="全部" style="width: 140px">
            <el-option v-for="(v, k) in salesOrderStatus" :key="k" :label="v.label" :value="Number(k)" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
          <el-button @click="() => { keyword = ''; customerId = undefined; orderStatus = undefined; search(); }">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div class="toolbar">
        <div class="title">销售订单</div>
        <el-button type="primary" @click="openCreate">新建订单</el-button>
      </div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="code" label="订单编号" width="170" fixed />
        <el-table-column label="客户名称" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">{{ lookup.name("customers", row.customerId) }}</template>
        </el-table-column>
        <el-table-column prop="orderDate" label="下单日期" width="110">
          <template #default="{ row }">{{ String(row.orderDate).slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column label="交货日期" width="110">
          <template #default="{ row }">{{ row.deliveryDate ? String(row.deliveryDate).slice(0, 10) : "-" }}</template>
        </el-table-column>
        <el-table-column prop="totalQty" label="数量" width="90" align="right" />
        <el-table-column label="金额" width="130" align="right">
          <template #default="{ row }">￥{{ money(row.totalAmount) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="statusTag(row.orderStatus).type" size="small">{{ statusTag(row.orderStatus).label }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="230" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openDetail(row)">详情</el-button>
            <el-button v-if="row.orderStatus === 0" link type="success" @click="confirm(row)">确认</el-button>
            <el-button v-if="row.orderStatus <= 1" link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button v-if="[1, 2].includes(row.orderStatus)" link type="warning" @click="openOutbound(row)">出库</el-button>
            <el-button v-if="[0, 1].includes(row.orderStatus)" link type="danger" @click="cancel(row)">取消</el-button>
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

    <!-- 新建/编辑 -->
    <el-dialog v-model="dialogVisible" :title="editId ? '编辑销售订单' : '新建销售订单'" width="960px" destroy-on-close top="6vh">
      <el-form ref="formRef" :model="form" label-width="90px">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="客户" required>
              <el-select v-model="form.customerId" filterable style="width: 100%" @change="onCustomerChange">
                <el-option v-for="c in customers" :key="c.value" :label="c.label" :value="c.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="下单日期" required>
              <el-date-picker v-model="form.orderDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="交货日期">
              <el-date-picker v-model="form.deliveryDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="联系人">
              <el-input v-model="form.contact" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="联系电话">
              <el-input v-model="form.phone" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="付款方式">
              <el-select v-model="form.paymentMethod" style="width: 100%">
                <el-option label="月结30天" value="月结30天" />
                <el-option label="月结60天" value="月结60天" />
                <el-option label="预付款" value="预付款" />
                <el-option label="货到付款" value="货到付款" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item label="收货地址">
              <el-input v-model="form.address" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-divider content-position="left">订单明细</el-divider>
        <el-table :data="form.lines" border size="small">
          <el-table-column label="产品" min-width="220">
            <template #default="{ row }">
              <el-select v-model="row.productId" filterable style="width: 100%" @change="onProductChange(row)">
                <el-option v-for="p in products" :key="p.value" :label="`${lookup.code('products', p.value)} ${p.label}`" :value="p.value" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="数量" width="130">
            <template #default="{ row }">
              <el-input-number v-model="row.qty" :min="1" :precision="0" controls-position="right" style="width: 100%" />
            </template>
          </el-table-column>
          <el-table-column label="单价" width="140">
            <template #default="{ row }">
              <el-input-number v-model="row.price" :min="0" :precision="2" controls-position="right" style="width: 100%" />
            </template>
          </el-table-column>
          <el-table-column label="金额" width="120" align="right">
            <template #default="{ row }">{{ money((row.qty || 0) * (row.price || 0)) }}</template>
          </el-table-column>
          <el-table-column label="交货日期" width="150">
            <template #default="{ row }">
              <el-date-picker v-model="row.deliveryDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
            </template>
          </el-table-column>
          <el-table-column width="60" align="center">
            <template #default="{ $index }">
              <el-button link type="danger" @click="removeLine($index)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
        <div class="line-footer">
          <el-button size="small" @click="addLine">添加明细</el-button>
          <span class="amount">合计：￥{{ money(totalAmount) }}</span>
        </div>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>

    <!-- 出库 -->
    <el-dialog v-model="outboundVisible" title="销售出库" width="720px" destroy-on-close>
      <el-form label-width="90px">
        <el-form-item label="出库仓库" required>
          <el-select v-model="outboundWarehouseId" style="width: 240px">
            <el-option v-for="w in warehouses" :key="w.value" :label="w.label" :value="w.value" />
          </el-select>
        </el-form-item>
        <el-table :data="outboundLines" border size="small">
          <el-table-column label="产品" min-width="200">
            <template #default="{ row }">{{ row.productName }}</template>
          </el-table-column>
          <el-table-column label="待出库" width="100" align="right">
            <template #default="{ row }">{{ row.maxQty }}</template>
          </el-table-column>
          <el-table-column label="本次出库" width="160">
            <template #default="{ row }">
              <el-input-number v-model="row.qty" :min="0" :max="row.maxQty" :precision="0" controls-position="right" style="width: 100%" />
            </template>
          </el-table-column>
        </el-table>
      </el-form>
      <template #footer>
        <el-button @click="outboundVisible = false">取消</el-button>
        <el-button type="primary" @click="submitOutbound">确认出库</el-button>
      </template>
    </el-dialog>

    <!-- 详情 -->
    <el-drawer v-model="detailVisible" title="销售订单详情" size="60%">
      <template v-if="detail">
        <el-descriptions :column="3" border>
          <el-descriptions-item label="订单编号">{{ detail.code }}</el-descriptions-item>
          <el-descriptions-item label="客户">{{ lookup.name("customers", detail.customerId) }}</el-descriptions-item>
          <el-descriptions-item label="状态">
            <el-tag :type="statusTag(detail.orderStatus).type" size="small">{{ statusTag(detail.orderStatus).label }}</el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="下单日期">{{ String(detail.orderDate).slice(0, 10) }}</el-descriptions-item>
          <el-descriptions-item label="交货日期">{{ detail.deliveryDate ? String(detail.deliveryDate).slice(0, 10) : "-" }}</el-descriptions-item>
          <el-descriptions-item label="金额">￥{{ money(detail.totalAmount) }}</el-descriptions-item>
          <el-descriptions-item label="联系人">{{ detail.contact || "-" }}</el-descriptions-item>
          <el-descriptions-item label="电话">{{ detail.phone || "-" }}</el-descriptions-item>
          <el-descriptions-item label="地址">{{ detail.address || "-" }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="detail.lines" border size="small" style="margin-top: 16px">
          <el-table-column label="产品" min-width="200">
            <template #default="{ row }">{{ lookup.name("products", row.productId) }}</template>
          </el-table-column>
          <el-table-column prop="qty" label="数量" width="100" align="right" />
          <el-table-column prop="price" label="单价" width="110" align="right" />
          <el-table-column prop="amount" label="金额" width="120" align="right" />
          <el-table-column prop="deliveredQty" label="已出库" width="100" align="right" />
        </el-table>
      </template>
    </el-drawer>
  </div>
</template>

<style scoped lang="scss">
.order-page {
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
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-top: 10px;
    .amount { font-size: 16px; font-weight: 600; color: var(--el-color-danger); }
  }
}
</style>
