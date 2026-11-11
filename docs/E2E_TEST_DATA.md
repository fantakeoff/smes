# 多行业 E2E 测试数据与隔离测试

## 安全边界

E2E CLI 和 `scripts/e2e/run.sh` 都要求 `E2E_DATABASE_NAME` 已设置且包含 `_e2e`；检查失败即退出。测试不会创建/删除数据库，不执行 `DROP`，运行前由操作者准备专用数据库并启动 API。不要指向开发、共享或生产数据库。

## 三类固定种子

`Smes.Infrastructure.Seed.IndustryScenarioSeeder.SeedAsync(db)` 以 `E2E_` 固定编码幂等创建：

| Tenant code | 场景 | 特性 |
|---|---|---|
| `E2E_MACHINERY` | 离散机加工 | 合金钢坯、CNC 车削、机加工轴承座 BOM/路线 |
| `E2E_ELECTRONICS` | 电子装配 | PCB 板、SMT 贴装、控制板组件 BOM/路线 |
| `E2E_FOOD` | 食品批次 | 浓缩果汁、灌装、食品批次号 `E2E_FOOD_LOT-001` |

每租户有测试用户、单位、仓库、供应商、成品和原料、BOM、工艺路线、缺料/充足工单、库存和采购单。测试密码仅用于隔离测试，禁止复用。Seeder 不会清理已有记录或执行删除。

## 整合要求

- `SysTenant` 需要提供 `Code`（或 `TenantCode`）与 `Id`，名称字段可为 `Name`/`TenantName`；种子器按反射适配这些名称。
- 每个业务实体应继承含 `TenantId` 的 `BaseEntity`。种子器在属性存在时设置租户值；租户查询过滤和数据库唯一约束仍须由应用/迁移提供。
- 在应用初始化/专用 seed 命令中显式调用 `IndustryScenarioSeeder.SeedAsync(db)`；当前 Seeder 不擅自接入生产启动流程。
- 认证需按租户选取用户；登出应撤销 refresh token。现有 API 的 logout 目前只返回成功而不撤销 refresh token，因此对应检查会揭示该集成缺口。

## 运行

先设置并核实独立 E2E DB/API，再运行。`E2E_DATABASE_NAME` 必须包含 `_e2e`，并与 `E2E_DB_CONNECTION_STRING` 中的 `Database`/`Initial Catalog` 相同；同时设置 API 地址：

```bash
export E2E_DATABASE_NAME='smes_e2e'
export E2E_DB_CONNECTION_STRING='Server=127.0.0.1;Database=smes_e2e;User ID=e2e;Password=从本机安全配置注入'
export E2E_API_BASE_URL='http://127.0.0.1:5000'
export E2E_USER_A='e2e_machinery_operator'
export E2E_PASSWORD_A='从本机安全配置注入'
export E2E_USER_B='e2e_electronics_operator'
export E2E_PASSWORD_B='从本机安全配置注入'
# 按接入后的业务控制器 DTO 填写 path|JSON；任何缺项都会使对应检查失败。
export E2E_CROSS_TENANT_PATH='/api/替换为租户资源'
export E2E_BOM_ROUTE_REQUEST='/api/替换|{}'
export E2E_SHORTAGE_PURCHASE_REQUEST='/api/替换|{}'
export E2E_PURCHASE_RECEIPT_REQUEST='/api/替换|{}'
export E2E_TRANSFER_REQUEST='/api/替换|{}'
export E2E_NEGATIVE_STOCK_REQUEST='/api/替换|{}'
export E2E_REPORT_QUEUE_REQUEST='/api/替换|{}'
export E2E_WORKORDER_RECEIPT_REQUEST='/api/替换|{}'
./scripts/e2e/run.sh
```

业务接口 DTO/路径仍在演进，CLI 将其作为显式配置而非猜测。当前程序覆盖匿名 401、登录/刷新/退出后 refresh 撤销、跨租户读取、BOM/路线、缺料采购、采购收货、调拨、负库存、报工幂等、工单入库的 HTTP 骨架。补齐业务配置时，调拨守恒与报工幂等需增加操作前后库存/记录数 read-back 断言；不能只凭 POST 成功判定完成。

E2E CLI 要求 .NET 10。不得为测试便利而给脚本添加自动清库逻辑。
