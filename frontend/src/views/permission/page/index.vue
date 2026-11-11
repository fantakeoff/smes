<script setup lang="ts">
import { type CSSProperties, computed } from "vue";
import { useUserStoreHook } from "@/store/modules/user";

defineOptions({
  name: "PermissionPage"
});

const elStyle = computed((): CSSProperties => {
  return {
    width: "85vw",
    justifyContent: "start"
  };
});

const username = computed(() => useUserStoreHook().username);
const roles = computed(() => useUserStoreHook().roles.join(", ") || "无");
</script>

<template>
  <div>
    <p class="mb-2!">
      页面与按钮权限由服务端当前账号角色决定。验证其他角色时，请使用对应账号重新登录；前端不模拟或切换身份。
    </p>
    <el-card shadow="never" :style="elStyle">
      <template #header>
        <div class="card-header">
          <span>当前账号：{{ username }}；角色：{{ roles }}</span>
        </div>
      </template>
      <span>退出登录并使用对应账号，可验证不同角色的菜单与操作权限。</span>
    </el-card>
  </div>
</template>
