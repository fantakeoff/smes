import { defineStore } from "pinia";
import { http } from "@/utils/http";

export type OptionItem = { id: number; code?: string; name?: string; extra?: string };

type LookupState = {
  loaded: boolean;
  loading: boolean;
  data: Record<string, OptionItem[]>;
};

export const useLookupStore = defineStore("lookup", {
  state: (): LookupState => ({
    loaded: false,
    loading: false,
    data: {}
  }),
  actions: {
    async load(force = false) {
      if (this.loading) return;
      if (this.loaded && !force) return;
      this.loading = true;
      try {
        const res: any = await http.request("get", "/lookup");
        if (res?.code === 0) {
          this.data = res.data ?? {};
          this.loaded = true;
        }
      } finally {
        this.loading = false;
      }
    },
    list(type: string): OptionItem[] {
      return this.data[type] ?? [];
    },
    name(type: string, id?: number | null): string {
      if (id == null) return "";
      const item = this.list(type).find(x => x.id === id);
      return item?.name ?? item?.code ?? String(id);
    },
    code(type: string, id?: number | null): string {
      if (id == null) return "";
      const item = this.list(type).find(x => x.id === id);
      return item?.code ?? "";
    },
    options(type: string) {
      return this.list(type).map(x => ({ label: x.name ?? x.code, value: x.id }));
    }
  }
});
