# SMEs 开发约定（供开发者 / 子 Agent 使用）

## 1. 项目结构

```
桌面/smes
├── backend/                    .NET 10 Web API（已在 5080 端口运行）
│   ├── Smes.sln
│   └── src/
│       ├── Smes.Domain/        实体（按模块分目录，自动被 EF 发现）
│       ├── Smes.Infrastructure/SmesDbContext + Seed
│       └── Smes.Api/           Controllers / Services / Program.cs
├── frontend/                   Vue3 + Vite + Element Plus（pure-admin-thin，8848 端口）
│   └── src/
│       ├── api/                可选：模块接口封装
│       ├── components/CrudTable/  通用 CRUD 页面组件（重点复用）
│       ├── store/modules/lookup.ts 全局下拉数据
│       ├── utils/biz.ts        业务状态字典
│       └── views/<模块>/<页面>/index.vue
├── docker-compose.yml
└── docs/
```

## 2. 运行环境

- 本地地址：前端 http://localhost:8848 ，后端 http://localhost:5080 ，MySQL 127.0.0.1:3307。数据库及演示账号密码从本地环境变量提供，参见根目录 `.env.example`。
- 后端已构建并在后台运行；新增/修改 C# 后只需 `dotnet build`，由主 Agent 统一重启。
- 前端 dev server 已运行，保存文件即热更新；**不要**自行 `pnpm dev` / `pnpm build` / 安装新依赖。

## 3. 后端约定

### 实体
- 所有业务实体继承 `Smes.Domain.Common.BaseEntity`：`Id(long) / Code / Name / Status / Remark / CreatedAt / CreatedBy / UpdatedAt / UpdatedBy / IsDeleted`。
- 实体放 `Smes.Domain/<模块>/` 下，`SmesDbContext.OnModelCreating` 会自动发现（命名空间以 `Smes.Domain` 开头且含 `Id` 属性），**不要改 DbContext**。
- 跨模块只存 Id（如 `ProductId`），不建导航属性/外键；只有主从表在同一模块内用 `List<子表>` + 集中关系配置（见 DbContext，新主从表需主 Agent 添加配置）。
- 金额 `decimal`，数量 `decimal`；状态用 int，含义写在字段注释里。
- 软删除由全局查询过滤器处理，删除只需 `IsDeleted = true`。

### 控制器
单表主数据直接继承通用控制器（无需写方法）：

```csharp
[ApiController]
[Route("api/quality-item")]
public class QualityItemController : CrudController<InspectionItem>
{
    public QualityItemController(SmesDbContext db, CurrentUser me) : base(db, me) { }
}
```

通用接口（自动具备）：`GET /api/xxx?page=1&pageSize=20&keyword=`、`GET /api/xxx/{id}`、
`POST /api/xxx`、`PUT /api/xxx/{id}`、`DELETE /api/xxx/{id}`、`POST /api/xxx/batch-delete`、`GET /api/xxx/options`。
额外过滤：query 中与实体属性同名的参数自动等值过滤（字符串为 contains），`status`、`startDate/endDate` 也已支持。

主从表/业务动作参考 `Controllers/SalesPurchaseControllers.cs`、`ProductionControllers.cs`、`WarehouseControllers.cs`。
统一返回 `ApiResult.Ok(data)` / `ApiResult.Fail("消息")`（code=0 成功）；分页用 `PagedResult<T>` 或匿名 `new { list, total, pageSize, currentPage }`。
单号用 `await NumberGenerator.NextAsync(_db, "WO", _me.Id)`，现有规则：SO/PO/PLAN/WO/BILL/QC/REP/EX/TAKE。
库存过账用 `StockService.ApplyAsync(bill, _me.Id)`（已注册 DI）。

### 小时级计划排程
- `GET /api/production-schedule?workshopId=` 返回 `{ plans:[{ ..., orders:[...] }], unplannedOrders:[...] }`；均需 JWT。
- `PUT /api/production-schedule/plans/{id}/move` or `PUT /api/production-schedule/orders/{id}/move`，请求 `{ expectedStart, expectedEnd, newStart }`，日期格式为无时区的工厂本地时间 `YYYY-MM-DDTHH:00:00`。服务端保留时长，按整小时平移。
- `PUT /api/production-schedule/orders/{id}/range` 请求 `{ expectedStart, expectedEnd, newStart, newEnd }`，用于调整工单工期；仅工单拖动/拉伸使用，结束必须晚于开始。
- 整计划移动：原子更新计划、旗下工单和明细交期。单工单移动或拉伸：原子更新工单、对应交期和所属计划的最早开始/最晚结束。`PlanDate` 是业务日期，不随排程移动。
- 禁止调整已完成/已关闭计划、工单；原起止时间已变返回 `code=409`，前端须刷新后重试。测试请在独立数据库进行，避免改动使用中的演示/正式数据。

### 权限
控制器统一 `[Authorize]`（CrudController 已带）；登录后 JWT 含角色与权限。不要新增全局过滤器，页面级权限由菜单控制。

## 4. 前端约定

### 通用 CRUD 页面（优先使用）
`src/components/CrudTable/index.vue`，示例：`src/views/basic/product/index.vue`。

```vue
<CrudTable api="/customer" title="客户信息" :columns="columns" :form-items="formItems" :default-form="{ status: 1 }" />
```

- `api` 不含 `/api` 前缀（axios baseURL 已设为 `/api`）。
- `columns`: `{ prop, label, width, minWidth, dict, money, date, align }`；`dict` 传 `src/utils/biz.ts` 里的字典或 `{1:{label:'启用',type:'success'}}`。
- `formItems`: `{ prop, label, type: input|number|select|textarea|date|switch|password, options, required, span, precision }`。
- `searchFields`: 额外查询条件，同 formItems 结构（支持 select/date/input）。
- 插槽：`#toolbar`（右上按钮）、`#actions="{ row }"`（行内额外按钮）、`#form-extra`。
- 下拉数据：`const lookup = useLookupStore(); onMounted(() => lookup.load());`
  `lookup.options("products|customers|suppliers|warehouses|units|workshops|processes|users|depts|categories|brands|defects|equipments")`；
  `lookup.name("customers", id)` 显示名称。需要的下拉类型若不在 `/api/lookup` 中，可自行 `http.request("get","/xxx/options")`。

### 自定义页面
- HTTP：`import { http } from "@/utils/http"; const res: any = await http.request("get", "/sales-order", { params });`，判断 `res.code === 0`，数据在 `res.data`。
- 消息：`import { message } from "@/utils/message";`；确认框：`import { ElMessageBox } from "element-plus"`。
- UI 只用 Element Plus + 项目已有依赖（echarts 已装），不要引入新 npm 包。
- 中文界面、表格 `border stripe`、分页居右、弹窗宽度 640–960px、金额千分位。
- 路由和菜单已由后端菜单表下发，**不要**在 `src/router` 新增路由，也不要改菜单；页面文件路径必须与菜单 `component` 一致（如 `basic/customer/index` → `src/views/basic/customer/index.vue`）。
- 页面顶部不要放整页大标题（布局已有面包屑），卡片内工具条标题即可。

### 状态字典（`src/utils/biz.ts`）
`salesOrderStatus / purchaseOrderStatus / planStatus / workOrderStatus / priorityDict / billTypeDict / billStatusDict / takeStatusDict / inspectTypeDict / inspectResultDict / equipmentStatusDict / repairStatusDict / alarmStatusDict / alarmTypeDict / taskStatusDict / maintainTypeDict / toolStatusDict / exceptionStatusDict / exceptionTypeDict / levelDict / productTypeDict / warehouseTypeDict`，配合 `toOptions(dict)` 生成下拉。

## 5. 已完成（不要重复实现）

- 登录 / JWT / 动态路由 / 菜单 / 角色菜单 / 用户 / 部门 / 字典 / 参数 / 日志
- 基础数据：产品（含页面）、客户、供应商、仓库、单位、车间、工序、BOM、工艺路线、不良品项、工厂日历（后端）
- 销售/采购订单（后端含确认、出库、收货）、生产计划/工单/报工（后端）、库存出入库/过账/盘点（后端）
- 仪表板分析页、销售订单页、用户管理页（前端参考实现）

## 6. 完成标准

1. 页面真实调用后端接口，列表有演示数据；新增/编辑/删除可用（或业务动作可用）。
2. `dotnet build` 0 error（改了 C# 时）；前端保存后无 Vite 编译错误。
3. 不修改他人模块文件、不改共享文件（Program.cs、DbContext、CrudTable、router、http、package.json、vite.config.ts）。
4. 完成后报告：改了哪些文件、新增接口列表、未完成项。
