# SMES 生产化改造与验收基线

## 改造目标

本轮将现有演示系统提升为可验证的生产化基线，重点覆盖：默认安全、Redis 可撤销登录会话、多租户数据隔离、RabbitMQ Topic 报工任务、库存与采购数量守恒、三类制造业测试数据及隔离环境端到端测试。

## 强制安全边界

1. 除登录、刷新和健康检查外，所有 API 默认要求认证。
2. MySQL、Redis、RabbitMQ 只接入 Docker 内部网络，不映射宿主机端口。
3. 生产启动不得使用示例密码、示例 JWT 密钥或自动演示账号；演示种子必须由显式开关启用。
4. Redis 会话不可用时认证失败关闭，不允许绕过撤销检查。
5. 所有业务实体由服务端租户上下文写入 TenantId；客户端传入的 TenantId 不作为信任来源。
6. 所有出库、领料、调拨默认拒绝负库存；采购收货、报工、入库不得超出剩余数量。
7. 报工 HTTP 接口只确认任务已受理；RabbitMQ 消费完成后才形成有效报工记录和工单累计。
8. 消息处理必须覆盖 HTTP 重试、Outbox 重发、RabbitMQ 重投和消费者崩溃重启，业务效果最多一次。
9. 自动化流程测试只允许连接数据库名包含 `_e2e` 的隔离数据库。

## 服务拓扑

- `web`：Nginx + Vue3，只暴露 Web 端口。
- `api`：ASP.NET Core API、Outbox dispatcher 与 RabbitMQ consumer；后续可独立拆分 worker。
- `mysql`：业务真相源，仅 Docker 内网。
- `redis`：会话与非关键主数据短 TTL 缓存，仅 Docker 内网。
- `rabbitmq`：Topic exchange `smes.events`，仅 Docker 内网。

## RabbitMQ 报工契约

- Exchange：`smes.events`，durable topic。
- Routing key：`tenant.{tenantId}.production.work-report.requested.v1`。
- Queue：`smes.production.work-report.v1`。
- Binding：`tenant.*.production.work-report.requested.v1`。
- 任务状态：Pending → Published/Processing → Succeeded/Failed。
- HTTP 幂等：`Idempotency-Key` 唯一约束。
- 消费幂等：Inbox `EventId` 唯一约束。
- 失败：有限重试，超过上限进入 DLQ 并保留失败原因。

## Redis 会话契约

- Access token 包含 `tenantId`、`tenantCode`、`sid`、`jti`，采用短有效期。
- Session key：`smes:{env}:auth:session:{sid}`，TTL 与 refresh token 一致。
- Refresh token 只保存不可逆哈希/JTI；刷新时原子轮换，旧令牌立即失效。
- Logout 删除 session；停用用户、重置密码和权限重大变化可撤销该用户全部 session。
- 主数据缓存可回源 MySQL；库存、工单累计、消息状态不得以 Redis 为唯一真相源。

## 多租户边界

- Tenant 由登录租户代码确定并写入 JWT，后续请求不可通过查询参数切换租户。
- BaseEntity 与用户角色关系表包含 TenantId。
- DbContext 全局过滤 `TenantId == CurrentTenantId && !IsDeleted`，写入时服务端覆盖 TenantId。
- 用户名、业务编码、角色编码等自然键改为租户内组合唯一。
- 两个租户允许相同用户名和业务编码，但跨租户 ID 查询、更新、关联写入必须返回不存在或拒绝。

## 制造闭环验收

1. 产品、BOM、工艺路线具备可执行引用关系。
2. 计划下达只生成一次工单和物料需求。
3. 原料不足时生成采购申请，经审批转采购订单，收货后增加指定仓库/批次库存。
4. 调拨保持总库存不变，源仓与目标仓分别形成流水。
5. 领料不超需求、不超库存；失败整体回滚。
6. 工序报工校验工单状态、数量及合格+不良守恒，通过 Topic 队列异步落库。
7. 工单入库不超剩余可入库数量，并形成成品库存与流水。
8. 已审核库存单据不直接删除或重复过账。

## 测试数据

- `machinery`：离散机加工，多级机械组件、机加工路线、原料/在制/成品仓。
- `electronics`：电子装配，元器件 BOM、SMT/组装/测试路线、待检和电子料仓。
- `food`：食品批次，重量单位、配方 BOM、称量/混合/灌装/检验/包装路线及批次仓库。

所有测试业务编码使用 `E2E-MACH-`、`E2E-ELEC-`、`E2E-FOOD-` 前缀，并采用固定随机种子和幂等业务键。

## 发布门禁

- `dotnet build` 0 错误。
- `pnpm typecheck` 与 `pnpm build` 通过。
- Compose 配置解析通过，基础设施无宿主端口暴露。
- 匿名业务请求返回 401；越权请求返回 403。
- Logout 后旧 access/refresh token 均不可继续使用。
- 两租户同名用户可登录且互相看不到业务数据。
- RabbitMQ 重复消息不会重复增加工单数量。
- 并发领料、重复审核、超量收货、负库存请求被拒绝且账实一致。
- 三行业隔离 E2E 流程通过后才允许发布。
