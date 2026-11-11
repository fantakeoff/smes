import { reactive } from "vue";
import type { FormRules } from "element-plus";

/** 登录校验仅要求非空；密码策略由服务端负责。 */
const loginRules = reactive<FormRules>({
  password: [
    {
      validator: (_rule, value, callback) => {
        if (typeof value !== "string" || value.length === 0) {
          callback(new Error("请输入密码"));
        } else {
          callback();
        }
      },
      trigger: "blur"
    }
  ]
});

export { loginRules };
