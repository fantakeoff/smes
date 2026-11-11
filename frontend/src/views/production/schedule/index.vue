<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from "vue";
import { ElMessageBox } from "element-plus";
import { http } from "@/utils/http";
import { message } from "@/utils/message";
import { useLookupStore } from "@/store/modules/lookup";
import { productTypeDict, toOptions, workOrderStatus } from "@/utils/biz";

type ScheduleId = number | string;
type ViewMode = "month" | "week" | "day";
type DragMode = "move" | "start" | "end";
interface TimeRange { start: Date; end: Date }
interface ScheduleOrder {
  id: ScheduleId;
  code?: string;
  name?: string;
  planId?: ScheduleId | null;
  productId?: ScheduleId | null;
  productCode?: string;
  productType?: number | string;
  productTypeName?: string;
  sourceType?: number;
  startDate?: string | Date | null;
  endDate?: string | Date | null;
  workOrderStatus?: number;
  qty?: number;
  completedQty?: number;
  workshopId?: ScheduleId | null;
}
interface SchedulePlan {
  id: ScheduleId;
  code?: string;
  name?: string;
  sourceType?: number;
  startDate?: string | Date | null;
  endDate?: string | Date | null;
  planStatus?: number;
  workshopId?: ScheduleId | null;
  orders?: ScheduleOrder[];
}
interface PendingChange {
  id: ScheduleId;
  expectedStart: Date;
  expectedEnd: Date;
  targetStart: Date;
  targetEnd: Date;
}
interface DragState {
  id: ScheduleId;
  mode: DragMode;
  originalStart: Date;
  originalEnd: Date;
  targetStart: Date;
  targetEnd: Date;
  pointerId: number;
  pointerStartX: number;
  lastClientX: number;
  startScrollLeft: number;
}
interface OrderRow {
  order: ScheduleOrder;
  plan?: SchedulePlan;
  sourceType?: number;
  workshopId: ScheduleId | null;
  range: TimeRange;
  pending: boolean;
}
interface LineGroup {
  key: string;
  id: ScheduleId | null;
  name: string;
  rows: OrderRow[];
}

const HOUR_MS = 60 * 60 * 1000;
const DAY_MS = 24 * HOUR_MS;
const FACTORY_TIME_ZONE = "Asia/Shanghai";
const LABEL_WIDTH = 252;
const lookup = useLookupStore();
const loading = ref(false);
const saving = ref(false);
const plans = ref<SchedulePlan[]>([]);
const unplannedOrders = ref<ScheduleOrder[]>([]);
const viewMode = ref<ViewMode>("month");
const viewAnchor = ref(factoryToday());
const showToday = ref(true);
const drag = ref<DragState | null>(null);
const scrollContainer = ref<HTMLElement | null>(null);
const pendingChanges = ref(new Map<string, PendingChange>());
const saveBlocked = ref(false);
let pointerCaptureElement: HTMLElement | null = null;
let autoScrollFrame = 0;

const filters = reactive({
  productCode: "",
  sourceType: undefined as number | undefined,
  workOrderStatus: undefined as number | undefined,
  workshopId: undefined as number | undefined,
  month: formatMonth(factoryToday())
});
const activeFilters = ref({ ...filters });
const workshops = computed(() => lookup.options("workshops"));
const sourceTypes = [
  { label: "销售订单", value: 1 },
  { label: "预测", value: 2 },
  { label: "手工", value: 3 }
];
const pendingCount = computed(() => pendingChanges.value.size);
const controlsLocked = computed(() => pendingCount.value > 0 || loading.value || saving.value || !!drag.value);

function parseWallClock(value: unknown): Date | null {
  if (value instanceof Date) {
    if (Number.isNaN(value.getTime())) return null;
    return new Date(Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate(), value.getUTCHours(), value.getUTCMinutes(), value.getUTCSeconds(), value.getUTCMilliseconds()));
  }
  const match = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.(\d+))?)?(?:Z|[+-]\d{2}:?\d{2})?)?$/i.exec(String(value ?? "").trim());
  if (!match) return null;
  const [year, month, day, hour, minute, second] = match.slice(1, 7).map((part, index) => Number(part ?? (index < 3 ? 0 : 0)));
  const millisecond = match[7] ? Math.floor(Number(`0.${match[7]}`) * 1000) : 0;
  const date = new Date(Date.UTC(year, month - 1, day, hour || 0, minute || 0, second || 0, millisecond));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day || date.getUTCHours() !== (hour || 0) || date.getUTCMinutes() !== (minute || 0) || date.getUTCSeconds() !== (second || 0)) return null;
  return date;
}

function factoryNow() {
  const parts = new Intl.DateTimeFormat("en-US", {
    timeZone: FACTORY_TIME_ZONE,
    year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23"
  }).formatToParts(new Date());
  const number = (type: string) => Number(parts.find(part => part.type === type)?.value ?? 0);
  return new Date(Date.UTC(number("year"), number("month") - 1, number("day"), number("hour"), number("minute")));
}
function factoryToday() {
  const now = factoryNow();
  now.setUTCHours(0, 0, 0, 0);
  return now;
}
function formatDate(value: Date) {
  return `${value.getUTCFullYear()}-${String(value.getUTCMonth() + 1).padStart(2, "0")}-${String(value.getUTCDate()).padStart(2, "0")}`;
}
function formatMonth(value: Date) {
  return `${value.getUTCFullYear()}-${String(value.getUTCMonth() + 1).padStart(2, "0")}`;
}
function formatDateTime(value: Date | null | undefined) {
  if (!value) return "-";
  return `${formatDate(value)} ${String(value.getUTCHours()).padStart(2, "0")}:${String(value.getUTCMinutes()).padStart(2, "0")}`;
}
function formatApiDateTime(value: Date) {
  return `${formatDate(value)}T${String(value.getUTCHours()).padStart(2, "0")}:${String(value.getUTCMinutes()).padStart(2, "0")}:${String(value.getUTCSeconds()).padStart(2, "0")}`;
}
function addHours(value: Date, hours: number) {
  return new Date(value.getTime() + hours * HOUR_MS);
}
function cloneRange(startValue: unknown, endValue: unknown): TimeRange | null {
  const start = parseWallClock(startValue);
  const end = parseWallClock(endValue);
  return start && end && end.getTime() >= start.getTime() ? { start, end } : null;
}
function sameId(left: ScheduleId | null | undefined, right: ScheduleId | null | undefined) {
  return left != null && right != null && String(left) === String(right);
}
function idKey(id: ScheduleId) {
  return String(id);
}

const periodStart = computed(() => {
  const anchor = new Date(viewAnchor.value);
  if (viewMode.value === "month") return new Date(Date.UTC(anchor.getUTCFullYear(), anchor.getUTCMonth(), 1));
  anchor.setUTCHours(0, 0, 0, 0);
  if (viewMode.value === "week") {
    const mondayOffset = (anchor.getUTCDay() + 6) % 7;
    anchor.setUTCDate(anchor.getUTCDate() - mondayOffset);
  }
  return anchor;
});
const periodEnd = computed(() => {
  const start = periodStart.value;
  if (viewMode.value === "month") return new Date(Date.UTC(start.getUTCFullYear(), start.getUTCMonth() + 1, 1));
  return new Date(start.getTime() + (viewMode.value === "week" ? 7 : 1) * DAY_MS);
});
const visibleDays = computed(() => {
  const count = Math.round((periodEnd.value.getTime() - periodStart.value.getTime()) / DAY_MS);
  return Array.from({ length: count }, (_, index) => new Date(periodStart.value.getTime() + index * DAY_MS));
});
const cellWidth = computed(() => viewMode.value === "day" ? 34 : 42);
const cellCount = computed(() => viewMode.value === "day" ? 24 : visibleDays.value.length);
const pixelPerHour = computed(() => cellWidth.value / (viewMode.value === "day" ? 1 : 24));
const timelineWidth = computed(() => cellWidth.value * cellCount.value);
const axisStyle = computed(() => ({
  "--label-width": `${LABEL_WIDTH}px`,
  "--cell-width": `${cellWidth.value}px`,
  "--cell-count": String(cellCount.value),
  "--timeline-width": `${timelineWidth.value}px`
} as Record<string, string>));
const timelineCells = computed(() => {
  if (viewMode.value === "day") {
    const day = periodStart.value;
    return Array.from({ length: 24 }, (_, hour) => ({
      key: `h-${hour}`,
      label: `${String(hour).padStart(2, "0")}:00`,
      title: `${formatDate(day)} ${String(hour).padStart(2, "0")}:00`,
      weekend: day.getUTCDay() === 0 || day.getUTCDay() === 6,
      today: showToday.value && formatDate(day) === formatDate(factoryToday()) && hour === factoryNow().getUTCHours()
    }));
  }
  return visibleDays.value.map(day => ({
    key: formatDate(day),
    label: `${String(day.getUTCMonth() + 1).padStart(2, "0")}/${String(day.getUTCDate()).padStart(2, "0")}`,
    title: `${formatDate(day)} ${new Intl.DateTimeFormat("zh-CN", { weekday: "long", timeZone: "UTC" }).format(day)}`,
    weekend: day.getUTCDay() === 0 || day.getUTCDay() === 6,
    today: showToday.value && formatDate(day) === formatDate(factoryToday())
  }));
});
const todayMarkerOffset = computed(() => {
  if (!showToday.value) return null;
  const now = factoryNow();
  if (now < periodStart.value || now >= periodEnd.value) return null;
  return ((now.getTime() - periodStart.value.getTime()) / HOUR_MS) * pixelPerHour.value;
});
const periodLabel = computed(() => `${formatDate(periodStart.value)} 至 ${formatDate(new Date(periodEnd.value.getTime() - DAY_MS))}`);
const currentMonthLabel = computed(() => formatMonth(viewAnchor.value));

function pendingFor(order: ScheduleOrder) {
  return order.id == null ? undefined : pendingChanges.value.get(idKey(order.id));
}
function orderRange(order: ScheduleOrder): TimeRange | null {
  const pending = pendingFor(order);
  return pending ? { start: pending.targetStart, end: pending.targetEnd } : cloneRange(order.startDate, order.endDate);
}
function progress(order: ScheduleOrder) {
  const qty = Number(order.qty ?? 0);
  return qty > 0 ? Math.max(0, Math.min(100, Math.round((Number(order.completedQty ?? 0) / qty) * 100))) : 0;
}
function productInfo(order: ScheduleOrder) {
  const item = order.productId == null ? undefined : lookup.list("products").find(row => String(row.id) === String(order.productId));
  const typeValue = order.productType ?? (item as any)?.productType;
  const typeLabel = order.productTypeName || (typeValue != null ? productTypeDict[Number(typeValue)]?.label : "") || "";
  const name = item?.name || order.name || "未关联产品";
  const spec = item?.extra;
  return { code: order.productCode || item?.code || "", label: typeLabel || name, spec: spec || "" };
}
function orderStatus(order: ScheduleOrder) {
  return workOrderStatus[Number(order.workOrderStatus)]?.label ?? "未知状态";
}
function sourceTypeOf(order: ScheduleOrder, plan?: SchedulePlan) {
  const value = order.sourceType ?? plan?.sourceType;
  return value == null ? undefined : Number(value);
}
function sourceTypeLabel(value?: number) {
  return sourceTypes.find(item => item.value === value)?.label || "来源待同步";
}
function orderColor(order: ScheduleOrder, range: TimeRange) {
  const status = Number(order.workOrderStatus);
  if (status === 3) return "#39a56a";
  if (status === 4) return "#8a929d";
  if (range.end.getTime() < factoryNow().getTime()) return "#e36b6b";
  if (status === 1 || status === 2) return "#e69a38";
  return "#4388db";
}
function canEditOrder(row: OrderRow) {
  const orderStatusValue = Number(row.order.workOrderStatus);
  const planStatusValue = Number(row.plan?.planStatus);
  return !saveBlocked.value && orderStatusValue !== 3 && orderStatusValue !== 4 &&
    !(row.plan && planStatusValue >= 3) && row.order.id != null && String(row.order.id).trim() !== "";
}
function lineName(id: ScheduleId | null) {
  return id == null ? "未指定生产线" : lookup.name("workshops", Number(id)) || `生产线 #${id}`;
}
function rowMatches(order: ScheduleOrder, plan: SchedulePlan | undefined, range: TimeRange) {
  const active = activeFilters.value;
  const code = (order.productCode || lookup.code("products", Number(order.productId)) || "").toLocaleLowerCase();
  if (active.productCode && !code.includes(active.productCode.trim().toLocaleLowerCase())) return false;
  if (active.workOrderStatus != null && Number(order.workOrderStatus) !== active.workOrderStatus) return false;
  const sourceType = sourceTypeOf(order, plan);
  if (active.sourceType != null && sourceType !== active.sourceType) return false;
  const workshopId = order.workshopId ?? plan?.workshopId ?? null;
  if (active.workshopId != null && !sameId(workshopId, active.workshopId)) return false;
  const visibleEnd = Math.max(range.end.getTime(), range.start.getTime() + HOUR_MS);
  return range.start.getTime() < periodEnd.value.getTime() && visibleEnd > periodStart.value.getTime();
}
const lineGroups = computed<LineGroup[]>(() => {
  const byLine = new Map<string, LineGroup>();
  const planById = new Map(plans.value.map(plan => [idKey(plan.id), plan]));
  const append = (order: ScheduleOrder, plan?: SchedulePlan) => {
    const range = orderRange(order);
    if (!range || !rowMatches(order, plan, range)) return;
    const workshopId = order.workshopId ?? plan?.workshopId ?? null;
    const key = workshopId == null ? "unassigned" : idKey(workshopId);
    let group = byLine.get(key);
    if (!group) {
      group = { key, id: workshopId, name: lineName(workshopId), rows: [] };
      byLine.set(key, group);
    }
    group.rows.push({ order, plan, sourceType: sourceTypeOf(order, plan), workshopId, range, pending: !!pendingFor(order) });
  };
  for (const plan of plans.value) for (const order of plan.orders || []) append(order, plan);
  for (const order of unplannedOrders.value) append(order, order.planId == null ? undefined : planById.get(idKey(order.planId)));
  return [...byLine.values()].map(group => ({
    ...group,
    rows: group.rows.sort((left, right) => left.range.start.getTime() - right.range.start.getTime() || String(left.order.code || left.order.id).localeCompare(String(right.order.code || right.order.id)))
  })).sort((left, right) => left.name.localeCompare(right.name, "zh-CN"));
});
const hasRows = computed(() => lineGroups.value.some(group => group.rows.length > 0));

function barStyle(range: TimeRange, laneIndex: number) {
  const visibleStart = periodStart.value.getTime();
  const visibleEnd = periodEnd.value.getTime();
  const displayEnd = Math.max(range.end.getTime(), range.start.getTime() + HOUR_MS);
  if (displayEnd <= visibleStart || range.start.getTime() >= visibleEnd) return { display: "none" };
  const left = Math.max(0, ((range.start.getTime() - visibleStart) / HOUR_MS) * pixelPerHour.value);
  const right = Math.min(timelineWidth.value, ((displayEnd - visibleStart) / HOUR_MS) * pixelPerHour.value);
  const maxWidth = Math.max(0, timelineWidth.value - left);
  const width = Math.min(maxWidth, Math.max(16, right - left));
  return { left: `${left}px`, width: `${width}px`, top: `${10 + laneIndex * 42}px` };
}
function lineGroupHeight(group: LineGroup) {
  return `${Math.max(70, 20 + group.rows.length * 42)}px`;
}
function isDragging(row: OrderRow) {
  return !!drag.value && sameId(drag.value.id, row.order.id);
}
function dateDisabled() {
  return controlsLocked.value;
}
function updateAnchor(date: Date) {
  viewAnchor.value = date;
  filters.month = formatMonth(date);
  activeFilters.value = { ...activeFilters.value, month: formatMonth(date) };
}
function shiftView(direction: -1 | 1) {
  if (controlsLocked.value) return;
  const next = new Date(viewAnchor.value);
  if (viewMode.value === "month") next.setUTCMonth(next.getUTCMonth() + direction, 1);
  else next.setUTCDate(next.getUTCDate() + direction * (viewMode.value === "week" ? 7 : 1));
  updateAnchor(next);
}
function goToday() {
  if (controlsLocked.value) return;
  updateAnchor(factoryToday());
}
function changeViewMode(mode: ViewMode) {
  if (!controlsLocked.value) viewMode.value = mode;
}

function beginDrag(event: PointerEvent, row: OrderRow) {
  if (event.button !== 0 || !event.isPrimary || loading.value || saving.value || drag.value || !canEditOrder(row)) return;
  const range = orderRange(row.order);
  if (!range) return;
  const idText = String(row.order.id ?? "").trim();
  if (!idText) {
    message("工单缺少有效编号，无法写入排程", { type: "warning" });
    return;
  }
  event.preventDefault();
  const handle = (event.target as HTMLElement).closest<HTMLElement>("[data-resize]");
  const mode = (handle?.dataset.resize || "move") as DragMode;
  const element = event.currentTarget as HTMLElement;
  try {
    element.setPointerCapture(event.pointerId);
    pointerCaptureElement = element;
  } catch {
    pointerCaptureElement = null;
  }
  drag.value = {
    id: row.order.id,
    mode,
    originalStart: range.start,
    originalEnd: range.end,
    targetStart: range.start,
    targetEnd: range.end,
    pointerId: event.pointerId,
    pointerStartX: event.clientX,
    lastClientX: event.clientX,
    startScrollLeft: scrollContainer.value?.scrollLeft ?? 0
  };
}
function updateDragTarget(state: DragState, clientX: number) {
  const scrollDelta = (scrollContainer.value?.scrollLeft ?? state.startScrollLeft) - state.startScrollLeft;
  const hours = Math.round((clientX - state.pointerStartX + scrollDelta) / pixelPerHour.value);
  if (state.mode === "move") {
    state.targetStart = addHours(state.originalStart, hours);
    state.targetEnd = addHours(state.originalEnd, hours);
  } else if (state.mode === "start") {
    const nextStart = addHours(state.originalStart, hours);
    if (nextStart < state.originalEnd) {
      state.targetStart = nextStart;
      state.targetEnd = state.originalEnd;
    }
  } else {
    const nextEnd = addHours(state.originalEnd, hours);
    if (nextEnd > state.originalStart) {
      state.targetStart = state.originalStart;
      state.targetEnd = nextEnd;
    }
  }
}
function moveDrag(event: PointerEvent) {
  const state = drag.value;
  if (!state || state.pointerId !== event.pointerId || saving.value) return;
  state.lastClientX = event.clientX;
  updateDragTarget(state, event.clientX);
  updateAutoScroll();
}
function updateAutoScroll() {
  const container = scrollContainer.value;
  const state = drag.value;
  if (!container || !state || saving.value) return;
  const bounds = container.getBoundingClientRect();
  const edge = Math.min(72, bounds.width * 0.16);
  if (state.lastClientX < bounds.left + edge || state.lastClientX > bounds.right - edge) {
    if (!autoScrollFrame) autoScrollFrame = window.requestAnimationFrame(autoScrollTick);
  } else if (autoScrollFrame) {
    window.cancelAnimationFrame(autoScrollFrame);
    autoScrollFrame = 0;
  }
}
function autoScrollTick() {
  autoScrollFrame = 0;
  const container = scrollContainer.value;
  const state = drag.value;
  if (!container || !state || saving.value) return;
  const bounds = container.getBoundingClientRect();
  const edge = Math.min(72, bounds.width * 0.16);
  const left = state.lastClientX < bounds.left + edge;
  const right = state.lastClientX > bounds.right - edge;
  if (!left && !right) return;
  const intensity = left ? (bounds.left + edge - state.lastClientX) / edge : (state.lastClientX - (bounds.right - edge)) / edge;
  const before = container.scrollLeft;
  container.scrollLeft += (left ? -1 : 1) * Math.max(5, Math.min(26, 26 * intensity));
  if (before !== container.scrollLeft) {
    updateDragTarget(state, state.lastClientX);
    autoScrollFrame = window.requestAnimationFrame(autoScrollTick);
  }
}
function stopAutoScroll() {
  if (autoScrollFrame) window.cancelAnimationFrame(autoScrollFrame);
  autoScrollFrame = 0;
}
function clearDrag() {
  stopAutoScroll();
  const element = pointerCaptureElement;
  const state = drag.value;
  if (element && state && element.hasPointerCapture?.(state.pointerId)) {
    try { element.releasePointerCapture(state.pointerId); } catch { /* pointer 已由浏览器释放 */ }
  }
  pointerCaptureElement = null;
  drag.value = null;
}
function cancelDrag(event: PointerEvent) {
  if (!drag.value || drag.value.pointerId !== event.pointerId) return;
  clearDrag();
}
function stopDrag(event: PointerEvent) {
  const state = drag.value;
  if (!state || state.pointerId !== event.pointerId) return;
  if (state.targetStart.getTime() !== state.originalStart.getTime() || state.targetEnd.getTime() !== state.originalEnd.getTime()) {
    const key = idKey(state.id);
    const previous = pendingChanges.value.get(key);
    const expectedStart = previous?.expectedStart ?? state.originalStart;
    const expectedEnd = previous?.expectedEnd ?? state.originalEnd;
    const next = new Map(pendingChanges.value);
    if (state.targetStart.getTime() === expectedStart.getTime() && state.targetEnd.getTime() === expectedEnd.getTime()) {
      next.delete(key);
    } else {
      next.set(key, { id: state.id, expectedStart, expectedEnd, targetStart: state.targetStart, targetEnd: state.targetEnd });
    }
    pendingChanges.value = next;
    if (!next.size) saveBlocked.value = false;
  }
  clearDrag();
}

function errorText(error: any) {
  return error?.response?.data?.message || error?.message || "请求失败";
}
async function loadSchedule() {
  loading.value = true;
  try {
    const workshopId = activeFilters.value.workshopId;
    const res: any = await http.request("get", "/production-schedule", {
      params: workshopId == null ? {} : { workshopId }
    });
    if (res?.code !== 0) throw new Error(res?.message || "生产计划排程加载失败");
    plans.value = Array.isArray(res.data?.plans) ? res.data.plans : [];
    unplannedOrders.value = Array.isArray(res.data?.unplannedOrders) ? res.data.unplannedOrders : [];
  } catch (error: any) {
    message(errorText(error) || "生产计划排程加载失败", { type: "error" });
  } finally {
    loading.value = false;
  }
}
async function confirmDiscardPending(action: string) {
  if (!pendingCount.value) return true;
  try {
    await ElMessageBox.confirm(`仍有 ${pendingCount.value} 张工单的排程未保存。${action}将放弃这些修改，是否继续？`, "未保存的排程", {
      type: "warning", confirmButtonText: "放弃并继续", cancelButtonText: "返回"
    });
    pendingChanges.value = new Map();
    saveBlocked.value = false;
    return true;
  } catch {
    return false;
  }
}
async function applyQuery() {
  if (loading.value || saving.value || drag.value) return;
  if (!(await confirmDiscardPending("查询/刷新"))) return;
  const requestedMonth = /^\d{4}-\d{2}$/.test(filters.month) ? filters.month : formatMonth(factoryToday());
  if (requestedMonth !== formatMonth(viewAnchor.value)) {
    const [year, month] = requestedMonth.split("-").map(Number);
    viewAnchor.value = new Date(Date.UTC(year, month - 1, 1));
  }
  filters.month = requestedMonth;
  activeFilters.value = { ...filters, productCode: filters.productCode.trim() };
  await loadSchedule();
}
async function resetFilters() {
  if (loading.value || saving.value || drag.value) return;
  if (!(await confirmDiscardPending("重置"))) return;
  Object.assign(filters, { productCode: "", sourceType: undefined, workOrderStatus: undefined, workshopId: undefined, month: formatMonth(factoryToday()) });
  viewAnchor.value = factoryToday();
  viewMode.value = "month";
  activeFilters.value = { ...filters };
  await loadSchedule();
}
async function refreshAfterDiscard() {
  if (loading.value || saving.value || drag.value) return;
  if (!(await confirmDiscardPending("刷新"))) return;
  await loadSchedule();
}
async function savePending() {
  if (!pendingCount.value || saving.value || loading.value || saveBlocked.value) return;
  saving.value = true;
  const batch = [...pendingChanges.value.values()];
  let saved = 0;
  try {
    for (const change of batch) {
      if (change.id == null || String(change.id).trim() === "") throw new Error("待保存工单缺少有效编号，已停止提交");
      const expectedStart = formatApiDateTime(change.expectedStart);
      const expectedEnd = formatApiDateTime(change.expectedEnd);
      const newStart = formatApiDateTime(change.targetStart);
      const newEnd = formatApiDateTime(change.targetEnd);
      const pureMove = change.targetEnd.getTime() - change.targetStart.getTime() === change.expectedEnd.getTime() - change.expectedStart.getTime();
      const route = `/production-schedule/orders/${encodeURIComponent(String(change.id))}/${pureMove ? "move" : "range"}`;
      const data = pureMove
        ? { expectedStart, expectedEnd, newStart }
        : { expectedStart, expectedEnd, newStart, newEnd };
      const res: any = await http.request("put", route, { data });
      if (res?.code !== 0) throw new Error(res?.message || `工单 ${change.id} 保存失败`);
      const remaining = new Map(pendingChanges.value);
      remaining.delete(idKey(change.id));
      pendingChanges.value = remaining;
      saved += 1;
    }
    pendingChanges.value = new Map();
    saveBlocked.value = false;
    message(`已保存 ${saved} 张工单排程`, { type: "success" });
  } catch (error: any) {
    saveBlocked.value = pendingCount.value > 0;
    message(`已保存 ${saved} 张；${pendingCount.value} 张未保存。${errorText(error)}${pendingCount.value ? " 未提交项已保留，可放弃后刷新再重新排程。" : ""}`, { type: "error" });
  } finally {
    await loadSchedule();
    saving.value = false;
  }
}

onMounted(() => {
  void lookup.load().catch((error: any) => message(error?.message || "产品和生产线信息加载失败", { type: "warning" }));
  void loadSchedule();
});
onBeforeUnmount(() => clearDrag());
</script>

<template>
  <div class="production-schedule-page">
    <el-card shadow="never" class="filter-card">
      <el-form inline @submit.prevent>
        <el-form-item label="产品编码">
          <el-input v-model="filters.productCode" clearable placeholder="输入产品编码" style="width: 170px" :disabled="controlsLocked" @keyup.enter="applyQuery" />
        </el-form-item>
        <el-form-item label="来源类型">
          <el-select v-model="filters.sourceType" clearable placeholder="全部来源" style="width: 145px" :disabled="controlsLocked">
            <el-option v-for="item in sourceTypes" :key="item.value" :label="item.label" :value="item.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="工单状态">
          <el-select v-model="filters.workOrderStatus" clearable placeholder="全部状态" style="width: 135px" :disabled="controlsLocked">
            <el-option v-for="(item, key) in workOrderStatus" :key="key" :label="item.label" :value="Number(key)" />
          </el-select>
        </el-form-item>
        <el-form-item label="生产线">
          <el-select v-model="filters.workshopId" clearable filterable placeholder="全部生产线" style="width: 165px" :disabled="controlsLocked">
            <el-option v-for="item in workshops" :key="item.value" :label="item.label" :value="item.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="年月">
          <el-date-picker v-model="filters.month" type="month" value-format="YYYY-MM" format="YYYY 年 M 月" placeholder="选择年月" style="width: 145px" :disabled="dateDisabled()" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" :disabled="saving || !!drag" @click="applyQuery">查询</el-button>
          <el-button :disabled="loading || saving || !!drag" @click="resetFilters">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never" class="chart-card">
      <div class="toolbar">
        <div class="permission-note">
          <span>排程权限：可调整未完工、未关闭工单</span>
          <el-tag size="small" effect="plain" type="info">当前视图：{{ viewMode === 'month' ? '月' : viewMode === 'week' ? '周' : '日' }} · {{ periodLabel }}</el-tag>
          <el-tag v-if="pendingCount" size="small" type="warning" effect="plain">待保存 {{ pendingCount }} 张</el-tag>
          <el-tag v-if="saveBlocked && pendingCount" size="small" type="danger" effect="plain">存在保存冲突，请放弃后刷新</el-tag>
        </div>
        <div class="toolbar-actions">
          <el-button-group>
            <el-button size="small" :disabled="controlsLocked" @click="shiftView(-1)">{{ viewMode === 'month' ? '上月' : viewMode === 'week' ? '上周' : '前一天' }}</el-button>
            <el-button size="small" :disabled="controlsLocked" @click="shiftView(1)">{{ viewMode === 'month' ? '下月' : viewMode === 'week' ? '下周' : '后一天' }}</el-button>
          </el-button-group>
          <el-button size="small" :disabled="controlsLocked" @click="goToday">今天</el-button>
          <el-tag size="small" effect="plain">{{ currentMonthLabel }}</el-tag>
          <el-radio-group :model-value="viewMode" size="small" :disabled="controlsLocked" @update:model-value="changeViewMode">
            <el-radio-button value="month">月</el-radio-button>
            <el-radio-button value="week">周</el-radio-button>
            <el-radio-button value="day">日</el-radio-button>
          </el-radio-group>
          <el-switch v-model="showToday" size="small" active-text="显示今日" inactive-text="隐藏今日" :disabled="loading || saving || !!drag" />
          <el-button type="primary" :disabled="!pendingCount || saving || loading || saveBlocked" :loading="saving" @click="savePending">保存排程{{ pendingCount ? ` (${pendingCount})` : '' }}</el-button>
          <el-button v-if="pendingCount" type="warning" plain :disabled="saving || loading || !!drag" @click="refreshAfterDiscard">放弃并刷新</el-button>
        </div>
      </div>

      <div class="legend-row">
        <span class="legend-item"><i class="legend-dot not-started" />未开工</span>
        <span class="legend-item"><i class="legend-dot started" />已开工</span>
        <span class="legend-item"><i class="legend-dot completed" />已完成</span>
        <span class="legend-item"><i class="legend-dot delayed" />已延期</span>
        <span class="interaction-tip">拖动中间把手移动；拖动两侧把手调整开始/结束时间，按整小时吸附。修改后点击“保存排程”提交。</span>
      </div>

      <div ref="scrollContainer" v-loading="loading || saving" class="gantt-scroll" :style="axisStyle">
        <div class="gantt-head">
          <div class="line-heading">生产线</div>
          <div class="timeline-head">
            <div class="timeline-grid head-grid">
              <div v-for="cell in timelineCells" :key="cell.key" class="timeline-cell head-cell" :class="{ weekend: cell.weekend, 'today-cell': cell.today }" :title="cell.title">
                <template v-if="viewMode === 'day'">{{ cell.label }}</template>
                <template v-else>
                  <span>{{ cell.label }}</span>
                  <small>{{ new Intl.DateTimeFormat('zh-CN', { weekday: 'short', timeZone: 'UTC' }).format(new Date(`${cell.key}T00:00:00Z`)) }}</small>
                  <b v-if="cell.today" class="today-label">今天</b>
                </template>
              </div>
            </div>
            <div v-if="todayMarkerOffset != null" class="today-marker head-marker" :style="{ left: `${todayMarkerOffset}px` }" />
          </div>
        </div>

        <el-empty v-if="!hasRows && !loading" description="当前年月和筛选条件下没有工单" />
        <section v-for="group in lineGroups" :key="group.key" class="line-group">
          <div class="line-row">
            <div class="line-info">
              <span class="line-name">{{ group.name }}</span>
              <el-tag size="small" effect="plain">{{ group.rows.length }} 张工单</el-tag>
            </div>
            <div class="timeline-row" :style="{ height: lineGroupHeight(group) }">
              <div class="timeline-grid">
                <div v-for="cell in timelineCells" :key="cell.key" class="timeline-cell" :class="{ weekend: cell.weekend, 'today-cell': cell.today }" />
              </div>
              <div v-if="todayMarkerOffset != null" class="today-marker" :style="{ left: `${todayMarkerOffset}px` }" />
              <div
                v-for="(row, laneIndex) in group.rows"
                :key="`order-${row.order.id}`"
                class="order-bar"
                :class="{ locked: !canEditOrder(row), dragging: isDragging(row), pending: row.pending }"
                :style="{ ...barStyle(row.range, laneIndex), '--bar-color': orderColor(row.order, row.range) }"
                :title="`${row.order.code || '工单'} · ${productInfo(row.order).label}${productInfo(row.order).spec ? ` (${productInfo(row.order).spec})` : ''} · ${Number(row.order.completedQty || 0)}/${Number(row.order.qty || 0)} (${progress(row.order)}%) · ${formatDateTime(row.range.start)} 至 ${formatDateTime(row.range.end)} · ${sourceTypeLabel(row.sourceType)}${canEditOrder(row) ? '；可拖动移动或调整起止时间' : '；当前状态不可调整'}`"
                :aria-disabled="!canEditOrder(row) || saving || loading"
                @pointerdown="beginDrag($event, row)"
                @pointermove="moveDrag"
                @pointerup="stopDrag"
                @pointercancel="cancelDrag"
              >
                <span v-if="canEditOrder(row)" class="resize-handle resize-start" data-resize="start" title="调整开始时间" />
                <span class="bar-content">
                  <b>{{ row.order.code || `工单 #${row.order.id}` }}</b>
                  <small>{{ productInfo(row.order).label }} · {{ Number(row.order.completedQty || 0) }}/{{ Number(row.order.qty || 0) }} · {{ progress(row.order) }}%</small>
                </span>
                <span v-if="canEditOrder(row)" class="move-handle" data-resize="move" title="整条移动">⠿</span>
                <span v-if="canEditOrder(row)" class="resize-handle resize-end" data-resize="end" title="调整结束时间" />
                <span v-if="isDragging(row) && drag" class="drag-preview">{{ formatDateTime(drag.targetStart) }} – {{ formatDateTime(drag.targetEnd) }}</span>
              </div>
            </div>
          </div>
        </section>
      </div>
    </el-card>
  </div>
</template>

<style scoped lang="scss">
.production-schedule-page {
  .filter-card { margin-bottom: 12px; :deep(.el-form-item) { margin-bottom: 0; } }
  .chart-card { min-width: 0; }
  .toolbar { display: flex; align-items: flex-start; justify-content: space-between; flex-wrap: wrap; gap: 12px; margin-bottom: 12px; }
  .permission-note, .toolbar-actions { display: flex; align-items: center; flex-wrap: wrap; gap: 8px; }
  .permission-note { min-height: 30px; color: var(--el-text-color-secondary); font-size: 12px; }
  .toolbar-actions { justify-content: flex-end; }
  .legend-row { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; margin: 2px 0 12px; color: var(--el-text-color-secondary); font-size: 12px; }
  .legend-item { display: inline-flex; align-items: center; gap: 5px; white-space: nowrap; }
  .legend-dot { width: 9px; height: 9px; border-radius: 50%; }
  .not-started { background: #4388db; }
  .started { background: #e69a38; }
  .completed { background: #39a56a; }
  .delayed { background: #e36b6b; }
  .interaction-tip { margin-left: auto; }
  .gantt-scroll { position: relative; min-height: 180px; max-height: calc(100vh - 310px); min-width: 0; overflow: auto; overscroll-behavior: contain; }
  .gantt-head, .line-row { display: grid; grid-template-columns: var(--label-width) var(--timeline-width); width: max-content; min-width: 100%; }
  .gantt-head { position: sticky; top: 0; z-index: 6; background: var(--el-bg-color); border-bottom: 1px solid var(--el-border-color); }
  .line-heading, .line-info { position: sticky; left: 0; z-index: 4; width: var(--label-width); background: var(--el-bg-color); border-right: 1px solid var(--el-border-color-lighter); }
  .line-heading { display: flex; align-items: center; padding: 8px 12px; font-weight: 600; }
  .line-info { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 8px 12px; background: var(--el-fill-color-lighter); }
  .timeline-head, .timeline-row { position: relative; width: var(--timeline-width); }
  .timeline-grid { position: absolute; inset: 0; display: grid; grid-template-columns: repeat(var(--cell-count), var(--cell-width)); pointer-events: none; }
  .head-grid { position: relative; min-height: 44px; }
  .timeline-cell { min-width: 0; border-right: 1px solid var(--el-border-color-lighter); }
  .head-cell { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 1px; position: relative; color: var(--el-text-color-primary); font-size: 11px; font-weight: 600; white-space: nowrap; }
  .head-cell small { color: var(--el-text-color-secondary); font-size: 10px; font-weight: 400; }
  .weekend { background: var(--el-fill-color-light); }
  .head-cell.today-cell { background: var(--el-color-primary-light-9); color: var(--el-color-primary); }
  .today-label { position: absolute; bottom: 1px; font-size: 9px; line-height: 11px; }
  .today-marker { position: absolute; top: 0; bottom: 0; z-index: 2; width: 2px; background: #2583e8; pointer-events: none; box-shadow: 0 0 0 1px rgb(37 131 232 / 13%); }
  .head-marker { z-index: 3; }
  .line-group { border-bottom: 1px solid var(--el-border-color); }
  .line-name { overflow: hidden; font-size: 13px; font-weight: 700; text-overflow: ellipsis; white-space: nowrap; }
  .timeline-row { background: repeating-linear-gradient(to right, transparent 0, transparent calc(var(--cell-width) - 1px), var(--el-border-color-lighter) calc(var(--cell-width) - 1px), var(--el-border-color-lighter) var(--cell-width)); }
  .timeline-row .timeline-grid { z-index: 0; }
  .timeline-row .timeline-cell { background-color: transparent; }
  .timeline-row .timeline-cell.weekend { background-color: var(--el-fill-color-light); }
  .timeline-row .timeline-cell.today-cell { background-color: var(--el-color-primary-light-9); }
  .order-bar { position: absolute; top: 10px; z-index: 1; display: flex; align-items: center; min-width: 16px; height: 40px; overflow: visible; border: 1px solid color-mix(in srgb, var(--bar-color) 80%, #000 20%); border-radius: 5px; background: var(--bar-color); color: #fff; cursor: grab; touch-action: none; user-select: none; box-shadow: 0 1px 3px rgb(0 0 0 / 17%); }
  .order-bar:active { cursor: grabbing; }
  .order-bar.locked { cursor: not-allowed; opacity: .78; }
  .order-bar.dragging { z-index: 5; box-shadow: 0 3px 12px rgb(0 0 0 / 30%); }
  .order-bar.pending { outline: 2px dashed #f5a623; outline-offset: 1px; }
  .bar-content { display: flex; flex: 1; flex-direction: column; justify-content: center; min-width: 0; padding: 0 11px; pointer-events: none; }
  .bar-content b, .bar-content small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .bar-content b { font-size: 10px; line-height: 14px; }
  .bar-content small { font-size: 9px; line-height: 12px; opacity: .93; }
  .resize-handle { position: absolute; top: 0; bottom: 0; z-index: 3; width: 8px; cursor: ew-resize; }
  .resize-start { left: 0; border-radius: 5px 0 0 5px; }
  .resize-end { right: 0; border-radius: 0 5px 5px 0; }
  .resize-handle::after { position: absolute; top: 12px; bottom: 12px; left: 3px; width: 2px; border-radius: 2px; background: rgb(255 255 255 / 78%); content: ""; }
  .move-handle { position: absolute; top: 0; left: 50%; z-index: 3; display: flex; align-items: center; justify-content: center; width: 13px; height: 13px; transform: translateX(-50%); border-radius: 0 0 3px 3px; background: rgb(0 0 0 / 22%); color: #fff; font-size: 10px; line-height: 12px; cursor: grab; }
  .drag-preview { position: absolute; top: -22px; left: 0; z-index: 6; padding: 2px 5px; border: 1px solid var(--el-color-primary-light-5); border-radius: 3px; background: var(--el-bg-color); color: var(--el-text-color-primary); font-size: 10px; white-space: nowrap; pointer-events: none; box-shadow: 0 1px 4px rgb(0 0 0 / 12%); }
  @media (max-width: 900px) {
    .interaction-tip { flex-basis: 100%; margin-left: 0; }
    .toolbar-actions { justify-content: flex-start; }
    .gantt-scroll { max-height: calc(100vh - 380px); }
  }
}
</style>
