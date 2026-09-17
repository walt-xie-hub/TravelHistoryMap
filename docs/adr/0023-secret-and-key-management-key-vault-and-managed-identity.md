# 机密与密钥托管：Key Vault + 托管身份，签名私钥走应用内 SDK、其余走平台引用

## Status

Accepted

## Context

- 当前无 Key Vault、无托管身份。配置只走 `appsettings*.json` + 环境变量。
- `deploy-azure.sh` 既未注入 `Jwt__Key`（缺 key 时两个服务启动即抛 `InvalidOperationException`），也没有任何轮换路径——线上密钥只能手工配置，仓库无法追踪。
- git 历史 `8faf6c5` 可完整取出 `infra/k8s/base/secret.yaml` 中的 `Db__Password: "123456"`；删除该文件的提交并没有让口令消失。
- `docker-compose.dev.yml` 提交了 Grafana 的 `GF_SECURITY_ADMIN_PASSWORD=admin`。

## Decision

- 所有机密与密钥迁入 **Azure Key Vault**，容器应用使用**系统分配的托管身份**访问。
- **签名私钥由应用内读取**（`Azure.Identity` + Key Vault SDK）：轮换要求**新旧公钥并存、不重启即可切换**（JWKS 必须同时暴露新旧 `kid`），这是唯一需要代码的机密。
- **其余机密走平台原生 Key Vault 引用**（DB 密码、Client secret、演示账号密码）：零代码改动、ASP.NET 配置键不变，重启即可生效。
- **Key 命名约定**：`jwt-signing-<kid>`。identity-service 通过列出该前缀的 secret 得到完整 key set，因此**不落签名密钥表**。
- **轮换语义**：以新 `kid` 写入新 key → JWKS 同时暴露新旧 → 新签发只用新 key → 等待最长 access TTL 之后停用旧 key（旧 key 仍需可验签，直到它签发的 token 全部过期）。
- 机密清单与轮换顺序见 `docs/security/secrets-and-keys.md`。

## Consequences

- 这个**不对称**（签名私钥用 SDK、其余用平台引用）是刻意的。若后人为了「统一」而把签名私钥也改成平台引用，会立刻丧失不重启轮换的能力——这正是本 ADR 要留下的原因。
- 轮换从「不可执行的运维动作」变成可重复流程：密钥不在部署脚本里、也不需要重新构建镜像。
- 代价是 identity-service 多一个 Key Vault 依赖与一次启动期读取；KV 不可用时的策略（拒绝启动 vs 使用缓存公钥）见 `docs/security/secrets-and-keys.md`。
