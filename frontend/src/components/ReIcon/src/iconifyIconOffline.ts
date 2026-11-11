import { h, defineComponent, type Component, type PropType } from "vue";
import { Icon as IconifyIcon, addIcon, type IconifyIcon as IconifyData } from "@iconify/vue/dist/offline";

// Iconify Icon在Vue里本地使用（用于内网环境）
function iconDataName(icon: IconifyData) {
  const text = JSON.stringify(icon);
  let hash = 2166136261;
  for (let i = 0; i < text.length; i++) hash = Math.imul(hash ^ text.charCodeAt(i), 16777619);
  return `smes-offline-${(hash >>> 0).toString(16)}`;
}

export default defineComponent({
  name: "IconifyIconOffline",
  components: { IconifyIcon },
  props: {
    icon: {
      type: [String, Object, Function] as PropType<string | Component | IconifyData | null>,
      default: null
    }
  },
  render() {
    const icon = this.icon;
    if (!icon) return;
    const attrs = this.$attrs;
    const iconAttrs = {
      "aria-hidden": false,
      style: attrs?.style
        ? Object.assign(attrs.style, { outline: "none" })
        : { outline: "none" },
      ...attrs
    };

    if (typeof icon === "string") {
      return h(
        IconifyIcon,
        { icon, ...iconAttrs },
        { default: () => [] }
      );
    }

    if (typeof icon === "object" && ("render" in icon || "setup" in icon)) {
      return h(icon as Component, iconAttrs, { default: () => [] });
    }

    if (typeof icon === "object" && "body" in icon && typeof icon.body === "string") {
      const name = iconDataName(icon as IconifyData);
      addIcon(name, icon as IconifyData);
      return h(IconifyIcon, { icon: name, ...iconAttrs }, { default: () => [] });
    }

    return h(icon as Component, iconAttrs, { default: () => [] });
  }
});
