# 生产部署核查

生产环境使用 `docker-compose.yml`，将机密信息放在服务器的 `.env` 或秘密管理系统中。不要提交 `.env`、管理员凭据、数据库备份、证书或密钥。参考根目录 `.env.example` 配置独立随机的数据库、Redis、RabbitMQ、JWT 和两项 ALTCHA HMAC 密钥；示例占位符不能直接用于部署。

## 网络与入口

MySQL、Redis、RabbitMQ 仅在 Compose 内部网络提供服务；API 不直接发布宿主机端口。Web 默认只绑定 `127.0.0.1:8088`。如需外网访问，在现有 HTTPS 入口后配置反向代理，按实际拓扑设置 `WEB_BIND_ADDRESS`、`WEB_PORT` 和 `ALLOWED_ORIGINS`，并限制对 Web 端口的直接访问。管理端口与管理凭据保持在单独的私有运维文档中。

## 更新

更新前备份数据库和服务器上的 `.env`，并核实备份可恢复。检查配置和构建后再更新服务：

```bash
docker compose config --quiet
docker compose build api web
docker compose up -d --no-build
docker compose ps
```

常规更新不要执行 `docker compose down -v`，它会删除持久化数据。对已有非空数据库，先按 `DATABASE_MIGRATIONS.md` 核查迁移历史。

## 验证

检查 HTTPS 登录页、`/api/altcha/challenge`、未认证请求被拒绝、验证码重放被拒绝，以及已有站点仍可正常访问。生产环境默认关闭 `Seed__DemoEnabled` 和 `Seed__IndustryScenariosEnabled`。管理员凭据在服务器上安全保存，演示账号不得用于生产。
