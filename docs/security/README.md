# 安全指引

本目录记录本项目的安全目标态、现状差距与检查清单。它**不是**规范文档，而是"要做什么、为什么、怎么证明做完"的工作面。

- 建立时的定位基准：**小范围真实用户**（有公开注册入口、分享链接公网可达、数据库公网可达）。
- **生产环境的唯一真相是 Azure Container Apps**（`deploy-azure.sh`）。`infra/k8s/**` 已冻结为本地实验，只在恢复维护时按 `network-and-edge.md` 的附录逐项补齐。
- 本目录只描述**目标态与差距**；已落盘的架构决策在 `docs/adr/0019`–`0023`。

> 🔴 **当前最高优先级（2026-09-17 确认）**：本仓库是 **public**，且初始提交 `8faf6c5` 里 `infra/k8s/base/secret.yaml` 的 `Db__Password` **就是当前有效的数据库口令**（2026-07-26 起对全网可见，2026-09-07 才从工作树移除，历史仍可取出）。处置步骤见 [secrets-and-keys.md](./secrets-and-keys.md) 的「立即处置」。

## 分档

| 档 | 含义 |
|---|---|
| **P0** | 现在就是敞开的，立即处理 |
| **P1** | 上线/扩大使用前必须完成 |
| **P2** | 后续演进，不阻塞当前使用 |

## 主题分片

| 分片 | 覆盖 |
|---|---|
| [threat-model.md](./threat-model.md) | 信任边界、资产、攻击面、已被接受的风险 —— **先读这篇** |
| [authentication.md](./authentication.md) | 用户认证、令牌模型、OIDC 发现、迁移与共存 |
| [authorization.md](./authorization.md) | 归属规则、分享令牌边界、CORS 与运维端点 |
| [service-identity.md](./service-identity.md) | 服务身份、内部端点约定、跨服务调用的准入硬规则 |
| [secrets-and-keys.md](./secrets-and-keys.md) | 机密清单、Key Vault 托管、轮换顺序 |
| [network-and-edge.md](./network-and-edge.md) | 网关加固、限流、CSP、暴露面、数据库收口（含 k8s 恢复清单） |
| [observability-and-audit.md](./observability-and-audit.md) | 审计事件、遥测落点、脱敏、运维端点 |

## 决策记录

| ADR | 主题 |
|---|---|
| [0019](../adr/0019-trust-boundary-gateway-edge-and-services-self-authorize.md) | 网关只做边缘，鉴权在服务侧，可达性 ≠ 可信 |
| [0020](../adr/0020-identity-service-owns-authentication-not-the-user-aggregate.md) | identity-service 只拥有认证与令牌；凭据列唯一写者契约 |
| [0021](../adr/0021-token-model-rs256-jwks-rotation-and-refresh.md) | 令牌模型：RS256 + JWKS + 轮换 refresh（**取代 ADR-0005 §1**） |
| [0022](../adr/0022-service-identity-client-credentials-and-internal-channel.md) | 服务身份：client credentials + 默认拒绝 + 内部通道分期 |
| [0023](../adr/0023-secret-and-key-management-key-vault-and-managed-identity.md) | 机密与密钥托管：Key Vault + 托管身份 |

## 四条硬规则

新增代码时必须满足，评审时逐条检查：

1. **新增端点默认要求鉴权**。`/api/*` 是前缀全量转发，不写 `RequireAuthorization()` 就等于公开。
2. **内部端点用 `/internal/*`**，不得挂在 `/api/*` 下，且必须要求服务身份、拒绝用户 token。
3. **新增跨服务调用必须：带服务身份 token + 走加密通道 + 命中要求服务身份的目标端点**。不满足不予合并。
4. **不引入未经自己验证的身份头**。网关不注入身份头；服务只认自己验签的 token。

## 检查项总表

条目按 issue 分组。issue 的正文与创建脚本在 [`issues/`](./issues/)，装上 `gh` 后运行 `docs/security/issues/create-issues.ps1` 即可一次性开出。

### P0

| issue | 条目 | 分片 |
|---|---|---|
| ① | 数据库暴露面收口与口令轮换（**口令已确认泄露、且仍是当前有效值**、`SslMode` 强制） | [secrets-and-keys](./secrets-and-keys.md) · [network-and-edge](./network-and-edge.md) |
| ② | GHCR 镜像转私有 + ACA registry 凭据 | [secrets-and-keys](./secrets-and-keys.md) |
| ③ | 观测栈收口（端口绑回环、去掉 Grafana 默认口令、关 `debug` exporter、打开 `attributes` 过滤） | [observability-and-audit](./observability-and-audit.md) |
| ④ | 生产遥测端点（去掉指向不存在的 collector，改走环境级托管 agent） | [observability-and-audit](./observability-and-audit.md) |
| ⑤ | 网关加固（`Host`、`limit_req`、CSP、头剥离、`server_tokens off`、HSTS 仅 HTTPS） | [network-and-edge](./network-and-edge.md) |
| ⑥ | `IsActive` 检查随 identity-service 首个增量落地（停用账号不得换到令牌） | [authentication](./authentication.md) |

### P1

| issue | 条目 | 分片 |
|---|---|---|
| ⑦ | identity-service 落地：契约与骨架 → OIDC discovery + JWKS + `/identity/login` + refresh 轮换 | [authentication](./authentication.md) |
| ⑧ | RS256/JWKS 上线、受限共存、HS256 退役与 ADR-0005 状态更新 | [authentication](./authentication.md) |
| ⑨ | P1 汇总（Key Vault 接线、审计事件、失败锁定、CORS、运维端点、自定义域名、AMap 白名单、Client 注册表骨架） | 各分片 |

### P2

| issue | 条目 | 分片 |
|---|---|---|
| ⑩ | P2 汇总（PKCE + 内存存储、Dapr mTLS、Front Door/WAF、ACA 环境重建为 VNet、metrics 入库与告警、k8s 恢复清单、轮换日历） | 各分片 |

## 实现状态（2026-09-17）

**已完成**（同批提交）：

| 项 | 落地位置 |
|---|---|
| 停用账号不得换取令牌（P0 ⑥） | `UserService.LoginAsync` + 2 个单元测试 |
| dev 端口全部只绑回环（P0 ③） | `docker-compose.dev.yml` |
| Grafana 默认口令移除（P0 ③） | `docker-compose.dev.yml` + `.env.example` + `README.md`（新增**必需**变量 `GRAFANA_ADMIN_PASSWORD`） |
| 遥测属性脱敏、debug 降级（P0 ③） | `infra/observability/otel-collector-config.yaml` |
| 生产遥测落点（P0 ④） | `Shared.Observability/Telemetry.cs`（未配端点不启用 OTLP）+ `infra/k8s/base/configmap.yaml` + `deploy-azure.sh`（采样 0.1） |
| 网关加固（P0 ⑤） | `src/gateway/nginx.conf.template`、`src/frontend/client/nginx.conf` |
| 部署脚本收口（P0 ① 的配置部分） | `deploy-azure.sh`（`SslMode=Require`、自动生成 `Jwt__Key`、`--allow-insecure false`） |

**仍未完成**（需要真实凭据或环境操作，我无法代做）：

| 项 | 原因 |
|---|---|
| **数据库口令轮换** | 需要真实凭据；步骤见 [secrets-and-keys.md](./secrets-and-keys.md) 的「立即处置」 |
| **启用生产遥测采集** | P0 ④ 只做到"不再指向不存在的 collector"：OTLP 现在是**明确关闭**（不再静默失败），并没有接通采集。要真正采到数据需创建 Application Insights 资源并执行 `az containerapp env telemetry app-insights set …`（需要 Azure 权限，以及新资源的费用决定） |
| 收窄 PostgreSQL 公网访问 | 当前环境未接 VNet、ACA 出站 IP 不稳定，只能随环境重建处理（P2） |
| 把 `SslMode` / 采样率同步到**已部署**的应用 | 需要 `az containerapp update` 权限（脚本改动只影响下次初始化） |
| GHCR 转私有 | 需要 registry 凭据与仓库设置 |
| 本地 `pg_hba.conf` 的 `127.0.0.1 trust` | 它在**运行期数据目录**里（`database/postgres/pg_hba.conf`，未被 git 跟踪），改动不可评审也不可复现，所以没有动。要收口请本地执行：把 `host all all 127.0.0.1/32 trust` 改为 `scram-sha-256`，再 `docker exec postgres-db pg_ctl reload` |
| 生产入口实测（80→443 重定向、CSP 上报内容、限流是否按真实客户端 IP 生效） | 需要真实域名与流量；其中"XFF 最右一段是真实客户端"这一点目前是**基于官方文档的假设，尚未在你们的环境实测** |

## 维护方式

- 新增安全相关决策时：先判断是否符合 `docs/adr/` 的收录标准（难回退 + 没上下文会困惑 + 真权衡），够格才落 ADR；否则写进对应分片的"目标形态"。
- 术语（Caller / Credential / Identity provider / Client / Scope & Audience / Refresh token）定义在根目录 `CONTEXT.md`，本目录不重复定义。
- 每个分片的检查项完成后请勾选，并在 PR 描述里引用对应分片的"验收方式"。
