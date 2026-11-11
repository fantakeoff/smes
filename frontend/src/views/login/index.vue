<script setup lang="ts">
import "altcha";
import "altcha/i18n/all";
import Motion from "./utils/motion";
import { useRouter } from "vue-router";
import { message } from "@/utils/message";
import { loginRules } from "./utils/rule";
import { ref, reactive, toRaw } from "vue";
import { debounce } from "@pureadmin/utils";
import { useNav } from "@/layout/hooks/useNav";
import { useEventListener } from "@vueuse/core";
import type { FormInstance } from "element-plus";
import { useLayout } from "@/layout/hooks/useLayout";
import { useUserStoreHook } from "@/store/modules/user";
import { initRouter, getTopMenu } from "@/router/utils";
import { bg, avatar, illustration } from "./utils/static";
import { useRenderIcon } from "@/components/ReIcon/src/hooks";
import { useDataThemeChange } from "@/layout/hooks/useDataThemeChange";

import dayIcon from "@/assets/svg/day.svg?component";
import darkIcon from "@/assets/svg/dark.svg?component";
import Lock from "~icons/ri/lock-fill";
import User from "~icons/ri/user-3-fill";

defineOptions({
  name: "Login"
});

const router = useRouter();
const loading = ref(false);
const disabled = ref(false);
const ruleFormRef = ref<FormInstance>();
const captchaPayload = ref("");
const captchaWidget = ref<HTMLElementTagNameMap["altcha-widget"] | null>(null);

const { initStorage } = useLayout();
initStorage();

const { dataTheme, overallStyle, dataThemeChange } = useDataThemeChange();
dataThemeChange(overallStyle.value);
const { title } = useNav();

const ruleForm = reactive({
  tenantCode: "machinery",
  username: "",
  password: ""
});

const onCaptchaStateChange = (event: Event) => {
  const detail = (event as CustomEvent<{ state?: string; payload?: string }>).detail;
  captchaPayload.value =
    detail?.state === "verified" && typeof detail.payload === "string"
      ? detail.payload
      : "";
};

const onCaptchaVerified = (event: Event) => {
  const payload = (event as CustomEvent<{ payload?: string }>).detail?.payload;
  captchaPayload.value = typeof payload === "string" ? payload : "";
};

const onLogin = async (formEl: FormInstance | undefined) => {
  if (!formEl) return;
  const valid = await formEl.validate().catch(() => false);
  if (!valid) return;
  if (!captchaPayload.value) {
    message("请先完成 ALTCHA 验证", { type: "warning" });
    return;
  }

  loading.value = true;
  try {
    const res = await useUserStoreHook().loginByUsername({
      tenantCode: ruleForm.tenantCode,
      username: ruleForm.username,
      password: ruleForm.password,
      altcha: captchaPayload.value
    });
    if (!res.success) {
      message("登录失败，请重新完成验证后重试", { type: "error" });
      return;
    }

    await initRouter();
    disabled.value = true;
    try {
      await router.push(getTopMenu(true).path);
      message("登录成功", { type: "success" });
    } finally {
      disabled.value = false;
    }
  } catch {
    message("登录失败，请重新完成验证后重试", { type: "error" });
  } finally {
    loading.value = false;
    captchaPayload.value = "";
    captchaWidget.value?.reset();
  }
};

const immediateDebounce: any = debounce(
  formRef => onLogin(formRef),
  1000,
  true
);

useEventListener(document, "keydown", ({ code }) => {
  if (
    ["Enter", "NumpadEnter"].includes(code) &&
    !disabled.value &&
    !loading.value
  )
    immediateDebounce(ruleFormRef.value);
});
</script>

<template>
  <div class="select-none">
    <img :src="bg" class="wave" />
    <div class="flex-c absolute right-5 top-3">
      <!-- 主题 -->
      <el-switch
        v-model="dataTheme"
        inline-prompt
        :active-icon="dayIcon"
        :inactive-icon="darkIcon"
        @change="dataThemeChange"
      />
    </div>
    <div class="login-container">
      <div class="img">
        <component :is="toRaw(illustration)" />
      </div>
      <div class="login-box">
        <div class="login-form">
          <avatar class="avatar" />
          <Motion>
            <h2 class="outline-hidden">{{ title }}</h2>
          </Motion>

          <el-form
            ref="ruleFormRef"
            :model="ruleForm"
            :rules="loginRules"
            size="large"
          >
            <Motion :delay="75">
              <el-form-item prop="tenantCode">
                <el-input v-model="ruleForm.tenantCode" clearable placeholder="租户代码" />
              </el-form-item>
            </Motion>
            <Motion :delay="100">
              <el-form-item
                :rules="[
                  {
                    required: true,
                    message: '请输入账号',
                    trigger: 'blur'
                  }
                ]"
                prop="username"
              >
                <el-input
                  v-model="ruleForm.username"
                  clearable
                  placeholder="账号"
                  :prefix-icon="useRenderIcon(User)"
                />
              </el-form-item>
            </Motion>

            <Motion :delay="150">
              <el-form-item prop="password">
                <el-input
                  v-model="ruleForm.password"
                  clearable
                  show-password
                  placeholder="密码"
                  :prefix-icon="useRenderIcon(Lock)"
                />
              </el-form-item>
            </Motion>

            <Motion :delay="200">
              <div class="mb-3 flex justify-center">
                <altcha-widget
                  ref="captchaWidget"
                  challenge="/api/altcha/challenge"
                  name="altcha"
                  language="zh-cn"
                  type="checkbox"
                  @statechange="onCaptchaStateChange"
                  @verified="onCaptchaVerified"
                />
              </div>
            </Motion>

            <Motion :delay="250">
              <el-button
                class="w-full mt-4!"
                size="default"
                type="primary"
                :loading="loading"
                :disabled="disabled || !captchaPayload"
                @click="onLogin(ruleFormRef)"
              >
                登录
              </el-button>
            </Motion>
          </el-form>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
@import url("@/style/login.css");
</style>

<style lang="scss" scoped>
:deep(.el-input-group__append, .el-input-group__prepend) {
  padding: 0;
}
</style>
