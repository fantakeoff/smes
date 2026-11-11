# SMEs 智能制造运营平台

参考 `mom.625sc.com`（Asi MOM 商业版）与 `gitee.com/ZM-Rid/imes`（iMES 工厂管家）重写的制造运营管理系统，覆盖 **销售 → 计划 → 工单 → 领料 → 报工 → 检验 → 入库 → 出货** 的完整业务链，并包含设备、工装、异常、库存、系统管理等支撑模块。

- 后端：.NET 10 Web API + EF Core 9 + Pomelo MySQL + JWT
- 前端：Vue 3 + Vite 7 + TypeScript + Element Plus（基于 `pure-admin-thin`，UI 与 `vue-pure-admin` 一致）
- 数据库：MySQL 8.4
- 部署：Docker Compose（MySQL + Redis + RabbitMQ + API + Nginx），生产入口可接 OpenResty 与雷池 SafeLine

## 快速开始（Docker）

```bash
cd ~/桌面/smes
cp .env.example .env
# 编辑 .env：为所有 CHANGE_ME 项设置独立随机值，并设置允许访问的前端源
# 生产部署还需设置两个互不相同的 ALTCHA HMAC 密钥，并配置 HTTPS 入口；详见 docs/DEPLOYMENT_SAFE.md
docker compose config
docker compose up -d --build
```

Compose 使用 MySQL 8.4（AWS Public ECR 的 Docker Official Image 镜像）、Redis 7.4、RabbitMQ 4.1；Node/Nginx 默认也使用官方镜像镜像源，可通过 `.env` 中对应 `*_IMAGE` 变量覆盖。MySQL、Redis、RabbitMQ 不发布宿主机端口，三者及 API 仅通过 `backend` 网络通信；API 同时连接 `web` 网络，前端仅连接 `web`。数据库、缓存和消息代理数据分别持久化到命名卷。Redis 启用密码和 AOF，RabbitMQ 使用独立用户及 `/smes` vhost。

启动后只对外提供以下应用端口：

| 服务 | 地址 |
| --- | --- |
| 前端（默认仅本机） | http://localhost:8088 |
| 后端 API | 通过前端的 `/api/` 反向代理访问，不单独发布端口 |
| API 文档（Scalar） | 仅开发环境启用，生产环境不公开 |

MySQL 连接由 `MYSQL_DATABASE`、`MYSQL_USER`、`MYSQL_PASSWORD` 组成的应用账户提供；root 密码仅供容器内健康检查。更改凭据不会自动改写已有数据卷中的账号，首次初始化后修改密码需同步迁移数据库凭据。

生产环境要求 `ALLOWED_ORIGINS` 精确匹配 HTTPS 站点，`Seed__DemoEnabled` 默认关闭，登录通过自托管 ALTCHA 签名验证、Redis 一次性重放阻断及按账户限速。部署前配置独立随机的 `ALTCHA_HMAC_SIGNATURE_SECRET` 和 `ALTCHA_HMAC_KEY_SIGNATURE_SECRET`。Compose 的 Web 端口默认绑定 `127.0.0.1:8088`；如通过网桥上的前置代理，可显式设置 `WEB_BIND_ADDRESS` 和 `WEB_PORT`，并对外部直连采取防火墙限制。

`.env.example` 中的值都是 `CHANGE_ME` 提示，不能直接用于部署。请通过秘密管理系统注入生产凭据，并确保 JWT 密钥至少为 32 个随机字节。开发环境配置中的空密码/连接字符串也需由本地环境变量提供。

如需创建演示数据，启用相应的 `SEED_*_ENABLED` 选项，并在本地 `.env` 中设置 `SEED_ADMIN_PASSWORD`、`SEED_DEMO_USER_PASSWORD`；多行业测试数据还需设置 `SEED_SCENARIO_PASSWORD`。这些密码至少 12 位，须包含大小写字母和数字，不要提交到仓库。

## 本地开发

```bash
# 1. 数据库
docker run -d --name smes-mysql-dev -e MYSQL_ROOT_PASSWORD="$MYSQL_ROOT_PASSWORD" \
  -e MYSQL_DATABASE=smes -p 3307:3306 mysql:8.4 \
  --character-set-server=utf8mb4 --collation-server=utf8mb4_unicode_ci

# 2. 后端（默认连接 localhost:3307）
cd backend
dotnet build
dotnet run --project src/Smes.Api --urls http://localhost:5080

# 3. 前端
cd frontend
pnpm install
pnpm dev        # http://localhost:8848，/api 已代理到 5080
```

## 功能模块

### 仪表板
- 分析页：今日工单/在制工单/完成率/设备利用率、生产趋势、工单状态分布、工序不良排名、设备报警、近期工单、订单金额趋势
- 工作台：待办（异常/检验/在制工单/库存预警）、快捷入口、最近工单

### 基础数据
产品分类、产品品牌、产品管理、计量单位、仓库信息、客户信息、供应商信息、车间产线、工序管理、工艺路线（含工序明细）、BOM 管理（含用料明细）、不良品项、工厂日历

### 销售管理
销售订单（草稿/确认/部分出库/完成/取消）、销售出库（生成出库单并过账）、销售退货

### 采购管理
采购订单、采购收货（生成入库单并过账）、采购退货

### 生产管理
生产计划（主从表、下达生成工单）、生产工单（下达/开工/领料/入库/关闭、BOM 用料展开）、生产领料、生产入库、报工记录、计划排程。

排程以**工厂本地时间**展示小时轴：拖动计划条会整体平移旗下工单与明细交期；拖动工单条会重算所属计划的起止时间与该行交期。拖动按整小时吸附，已完工或已关闭的任务不能移动；并发修改会提示刷新后重试。计划日期是业务建单日期，不随排程移动。

### 质量管理
检验项目、检验方案（主从表）、检验任务/检验单（来料/过程/出货）、判定（合格/不合格/让步接收）、质量看板

### 库存管理
库存余额、库存流水、出入库单（审核过账）、库存调拨、库存盘点（生成/实盘/完成调整）、库存预警

### 设备管理
设备分类、设备台账、点检保养计划、点检保养任务（执行）、设备报修（处理）、设备报警（确认/关闭）

### 工装管理
工装台账、工装领用、工装归还

### 异常管理
异常提报、异常处理

### 消息中心
系统通知、我的消息（已读/全部已读）

### 报表分析
生产报表（按日/按产品汇总、导出 CSV）、工单进度看板（30 秒自动刷新）

### 系统管理
用户管理（角色分配/重置密码）、角色管理（菜单权限树）、菜单管理、部门管理、数据字典、系统参数、操作日志

## 目录结构

```
smes/
├── backend/
│   ├── Smes.sln
│   ├── Directory.Build.props
│   ├── Dockerfile
│   └── src/
│       ├── Smes.Domain/          # 实体（按模块分目录，EF 自动发现）
│       ├── Smes.Infrastructure/  # DbContext、种子数据、演示数据
│       └── Smes.Api/             # Controllers、Services、JWT、Program
├── frontend/
│   ├── src/views/<模块>/<页面>/index.vue
│   ├── src/components/CrudTable/ # 配置化 CRUD 页面组件
│   ├── src/store/modules/lookup.ts
│   ├── src/utils/biz.ts          # 业务状态字典
│   ├── Dockerfile
│   └── nginx.conf
├── docs/DEV_GUIDE.md             # 开发约定（新增模块必读）
├── docker-compose.yml
└── README.md
```

## 架构要点

- **单一 DbContext 自动发现实体**：新增模块只加实体类，不修改 `SmesDbContext`。
- **通用 CRUD 控制器**：`CrudController<T>` 提供分页/关键字/字段过滤/排序/软删除/批量删除/下拉选项，主数据页面后端约 10 行代码。
- **并行开发友好**：跨模块只存 `long XxxId`，不建跨模块导航属性；主从表关系集中在 DbContext 配置。
- **菜单即路由**：菜单表存储 `path / component / icon / permission`，登录后由 `/api/get-async-routes` 下发，前端动态注册路由。
- **库存统一过账**：`StockService` 按单据类型（采购入库/生产领料/生产入库/销售出库/调拨/盘盈亏）更新库存余额并写流水，避免各模块重复实现。

## 与参考系统的差异

- 参考商业版中的 **零售管理、结算管理（供应商/客户对账）、物流管理、在线 Excel、UReport 报表设计器、流程设计器、多租户** 未包含在本项目内。
- 未实现商业版的打印模板、移动端、自定义实体扩展与代码生成器。
- 采用单租户模型；权限控制到菜单与接口（JWT + 角色菜单），未实现字段级数据权限。

这些模块的数据库结构、菜单入口和通用 CRUD 基座均已具备，可按 `docs/DEV_GUIDE.md` 逐步扩展。
