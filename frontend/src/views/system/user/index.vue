<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { message } from "@/utils/message";
import { http } from "@/utils/http";
import { useLookupStore } from "@/store/modules/lookup";

const lookup = useLookupStore();
const loading = ref(false);
const list = ref<any[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const keyword = ref("");
const status = ref<number | undefined>();
const deptId = ref<number | undefined>();
const depts = ref<any[]>([]);
const roles = ref<any[]>([]);
const dialogVisible = ref(false);
const editId = ref<number | null>(null);
const formRef = ref();
const form = reactive<any>({
  username: "",
  nickname: "",
  password: "",
  phone: "",
  email: "",
  deptId: undefined,
  roleIds: [],
  status: 1,
  remark: ""
});

const rules = {
  username: [{ required: true, message: "请输入账号", trigger: "blur" }],
  nickname: [{ required: true, message: "请输入姓名", trigger: "blur" }],
  password: [{ required: true, message: "请输入密码", trigger: "blur" }]
};

async function fetchList() {
  loading.value = true;
  try {
    const res: any = await http.request("get", "/user", {
      params: { page: page.value, pageSize: pageSize.value, keyword: keyword.value || undefined, status: status.value, deptId: deptId.value }
    });
    if (res?.code === 0) {
      list.value = res.data.list;
      total.value = res.data.total;
    }
  } finally {
    loading.value = false;
  }
}

async function loadMeta() {
  const [d, r] = await Promise.all([
    http.request<any>("get", "/dept"),
    http.request<any>("get", "/role", { params: { page: 1, pageSize: 100 } })
  ]);
  depts.value = d?.data ?? [];
  roles.value = r?.data?.list ?? [];
}

function openCreate() {
  editId.value = null;
  Object.assign(form, { username: "", nickname: "", password: "", phone: "", email: "", deptId: undefined, roleIds: [], status: 1, remark: "" });
  dialogVisible.value = true;
}

function openEdit(row: any) {
  editId.value = row.id;
  Object.assign(form, {
    username: row.username,
    nickname: row.nickname,
    password: "",
    phone: row.phone,
    email: row.email,
    deptId: row.deptId,
    roleIds: row.roleIds ?? [],
    status: row.status,
    remark: row.remark
  });
  dialogVisible.value = true;
}

async function save() {
  await formRef.value?.validate(async (valid: boolean) => {
    if (!valid) return;
    const payload = { ...form, id: editId.value ?? 0 };
    if (editId.value && !payload.password) delete payload.password;
    const res: any = editId.value
      ? await http.request("put", `/user/${editId.value}`, { data: payload })
      : await http.request("post", "/user", { data: payload });
    if (res?.code === 0) {
      message(editId.value ? "修改成功" : "新增成功", { type: "success" });
      dialogVisible.value = false;
      fetchList();
    } else {
      message(res?.message || "保存失败", { type: "error" });
    }
  });
}

async function resetPassword(row: any) {
  let password: string;
  try {
    const result = await ElMessageBox.prompt(`请输入「${row.username}」的新密码`, "重置密码", {
      inputType: "password",
      inputValidator: value =>
        value.length >= 12 && /[A-Z]/.test(value) && /[a-z]/.test(value) && /\d/.test(value)
          ? true
          : "密码至少 12 位，需包含大写字母、小写字母和数字"
    });
    password = result.value;
  } catch {
    return;
  }
  const res: any = await http.request("post", `/user/${row.id}/reset-password`, { data: { password } });
  message(res?.code === 0 ? "密码已重置" : res?.message || "操作失败", { type: res?.code === 0 ? "success" : "error" });
}

async function remove(row: any) {
  try {
    await ElMessageBox.confirm(`确定删除用户「${row.username}」吗？`, "提示", { type: "warning" });
  } catch {
    return;
  }
  const res: any = await http.request("delete", `/user/${row.id}`);
  message(res?.code === 0 ? "删除成功" : res?.message || "删除失败", { type: res?.code === 0 ? "success" : "error" });
  fetchList();
}

function deptName(id?: number) {
  if (!id) return "-";
  return depts.value.find(x => x.id === id)?.name ?? "-";
}

function search() {
  page.value = 1;
  fetchList();
}

onMounted(() => {
  lookup.load();
  loadMeta();
  fetchList();
});
</script>

<template>
  <div class="user-page">
    <el-card shadow="never" class="search-card">
      <el-form inline @submit.prevent>
        <el-form-item label="关键字">
          <el-input v-model="keyword" clearable placeholder="账号 / 姓名 / 手机" style="width: 220px" @keyup.enter="search" />
        </el-form-item>
        <el-form-item label="部门">
          <el-select v-model="deptId" clearable placeholder="全部" style="width: 160px">
            <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="status" clearable placeholder="全部" style="width: 120px">
            <el-option label="启用" :value="1" />
            <el-option label="停用" :value="0" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="search">查询</el-button>
          <el-button @click="() => { keyword = ''; deptId = undefined; status = undefined; search(); }">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div class="toolbar">
        <div class="title">用户管理</div>
        <el-button type="primary" @click="openCreate">新增用户</el-button>
      </div>
      <el-table v-loading="loading" :data="list" border stripe>
        <el-table-column prop="username" label="账号" width="130" />
        <el-table-column prop="nickname" label="姓名" width="110" />
        <el-table-column label="部门" width="130">
          <template #default="{ row }">{{ deptName(row.deptId) }}</template>
        </el-table-column>
        <el-table-column label="角色" min-width="180">
          <template #default="{ row }">
            <el-tag v-for="(r, i) in row.roleNames" :key="i" size="small" style="margin-right: 4px">{{ r }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="phone" label="手机号" width="130" />
        <el-table-column prop="email" label="邮箱" min-width="160" show-overflow-tooltip />
        <el-table-column label="状态" width="80">
          <template #default="{ row }">
            <el-tag :type="row.status === 1 ? 'success' : 'info'" size="small">{{ row.status === 1 ? "启用" : "停用" }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="最后登录" width="170">
          <template #default="{ row }">{{ row.lastLoginAt ? String(row.lastLoginAt).replace("T", " ").slice(0, 19) : "-" }}</template>
        </el-table-column>
        <el-table-column label="操作" width="190" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="warning" @click="resetPassword(row)">重置密码</el-button>
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

    <el-dialog v-model="dialogVisible" :title="editId ? '编辑用户' : '新增用户'" width="640px" destroy-on-close>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="账号" prop="username">
              <el-input v-model="form.username" :disabled="!!editId" placeholder="登录账号" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="姓名" prop="nickname">
              <el-input v-model="form.nickname" placeholder="真实姓名" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="密码" :prop="editId ? '' : 'password'">
              <el-input v-model="form.password" type="password" show-password :placeholder="editId ? '留空表示不修改' : '登录密码'" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="手机号">
              <el-input v-model="form.phone" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="邮箱">
              <el-input v-model="form.email" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="部门">
              <el-select v-model="form.deptId" clearable style="width: 100%">
                <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="d.id" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item label="角色">
              <el-select v-model="form.roleIds" multiple style="width: 100%">
                <el-option v-for="r in roles" :key="r.id" :label="r.name" :value="r.id" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="状态">
              <el-switch v-model="form.status" :active-value="1" :inactive-value="0" />
            </el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item label="备注">
              <el-input v-model="form.remark" type="textarea" :rows="2" />
            </el-form-item>
          </el-col>
        </el-row>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped lang="scss">
.user-page {
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
}
</style>
