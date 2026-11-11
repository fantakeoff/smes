/** 业务状态字典：与后端 int 枚举保持一致，页面直接绑定 el-tag 类型 */
export type TagType = "primary" | "success" | "warning" | "danger" | "info";
export type DictMap = Record<number, { label: string; type: TagType }>;

export const enableDict: DictMap = {
  1: { label: "启用", type: "success" },
  0: { label: "停用", type: "info" }
};

export const salesOrderStatus: DictMap = {
  0: { label: "草稿", type: "info" },
  1: { label: "已确认", type: "primary" },
  2: { label: "部分出库", type: "warning" },
  3: { label: "已完成", type: "success" },
  4: { label: "已取消", type: "danger" }
};

export const purchaseOrderStatus: DictMap = {
  0: { label: "草稿", type: "info" },
  1: { label: "已确认", type: "primary" },
  2: { label: "部分收货", type: "warning" },
  3: { label: "已完成", type: "success" },
  4: { label: "已取消", type: "danger" }
};

export const planStatus: DictMap = {
  0: { label: "草稿", type: "info" },
  1: { label: "已下达", type: "primary" },
  2: { label: "执行中", type: "warning" },
  3: { label: "已完成", type: "success" },
  4: { label: "已关闭", type: "danger" }
};

export const workOrderStatus: DictMap = {
  0: { label: "未下达", type: "info" },
  1: { label: "已下达", type: "primary" },
  2: { label: "生产中", type: "warning" },
  3: { label: "已完工", type: "success" },
  4: { label: "已关闭", type: "danger" }
};

export const priorityDict: DictMap = {
  1: { label: "低", type: "info" },
  2: { label: "中", type: "primary" },
  3: { label: "高", type: "warning" },
  4: { label: "紧急", type: "danger" }
};

export const billTypeDict: DictMap = {
  1: { label: "采购入库", type: "success" },
  2: { label: "生产领料", type: "warning" },
  3: { label: "生产入库", type: "success" },
  4: { label: "销售出库", type: "primary" },
  5: { label: "其他入库", type: "success" },
  6: { label: "其他出库", type: "info" },
  7: { label: "调拨", type: "warning" },
  8: { label: "盘盈", type: "success" },
  9: { label: "盘亏", type: "danger" },
  10: { label: "采购退货", type: "danger" },
  11: { label: "销售退货", type: "danger" }
};

export const billStatusDict: DictMap = {
  0: { label: "草稿", type: "info" },
  1: { label: "已审核", type: "success" },
  2: { label: "已作废", type: "danger" }
};

export const takeStatusDict: DictMap = {
  0: { label: "草稿", type: "info" },
  1: { label: "盘点中", type: "warning" },
  2: { label: "已完成", type: "success" },
  3: { label: "已作废", type: "danger" }
};

export const inspectTypeDict: DictMap = {
  1: { label: "来料检验", type: "primary" },
  2: { label: "过程检验", type: "warning" },
  3: { label: "出货检验", type: "success" },
  4: { label: "首件检验", type: "info" }
};

export const inspectResultDict: DictMap = {
  0: { label: "待检", type: "info" },
  1: { label: "合格", type: "success" },
  2: { label: "不合格", type: "danger" },
  3: { label: "让步接收", type: "warning" }
};

export const equipmentStatusDict: DictMap = {
  1: { label: "运行", type: "success" },
  2: { label: "待机", type: "info" },
  3: { label: "维修", type: "warning" },
  4: { label: "报废", type: "danger" }
};

export const repairStatusDict: DictMap = {
  0: { label: "待受理", type: "danger" },
  1: { label: "维修中", type: "warning" },
  2: { label: "已完成", type: "success" },
  3: { label: "已关闭", type: "info" }
};

export const alarmStatusDict: DictMap = {
  0: { label: "未恢复", type: "danger" },
  1: { label: "已恢复", type: "success" },
  2: { label: "已确认", type: "primary" },
  3: { label: "已关闭", type: "info" }
};

export const alarmTypeDict: DictMap = {
  1: { label: "超上限", type: "danger" },
  2: { label: "超下限", type: "warning" },
  3: { label: "离线", type: "info" },
  4: { label: "故障", type: "danger" }
};

export const taskStatusDict: DictMap = {
  0: { label: "待执行", type: "warning" },
  1: { label: "已执行", type: "success" },
  2: { label: "异常", type: "danger" }
};

export const maintainTypeDict: DictMap = {
  1: { label: "点检", type: "primary" },
  2: { label: "保养", type: "warning" }
};

export const toolStatusDict: DictMap = {
  1: { label: "在库", type: "success" },
  2: { label: "领用中", type: "warning" },
  3: { label: "维修", type: "info" },
  4: { label: "报废", type: "danger" }
};

export const exceptionStatusDict: DictMap = {
  0: { label: "待处理", type: "danger" },
  1: { label: "处理中", type: "warning" },
  2: { label: "已关闭", type: "success" }
};

export const exceptionTypeDict: DictMap = {
  1: { label: "设备", type: "danger" },
  2: { label: "质量", type: "warning" },
  3: { label: "物料", type: "primary" },
  4: { label: "安全", type: "danger" },
  5: { label: "其他", type: "info" }
};

export const levelDict: DictMap = {
  1: { label: "一般", type: "info" },
  2: { label: "重要", type: "warning" },
  3: { label: "紧急", type: "danger" }
};

export const productTypeDict: DictMap = {
  1: { label: "成品", type: "success" },
  2: { label: "半成品", type: "warning" },
  3: { label: "原材料", type: "primary" },
  4: { label: "辅料", type: "info" }
};

export const warehouseTypeDict: DictMap = {
  1: { label: "原料仓", type: "primary" },
  2: { label: "半成品仓", type: "warning" },
  3: { label: "成品仓", type: "success" },
  4: { label: "待检仓", type: "info" },
  5: { label: "不良品仓", type: "danger" }
};

export function toOptions(dict: DictMap) {
  return Object.keys(dict).map(k => ({ label: dict[Number(k)].label, value: Number(k) }));
}
