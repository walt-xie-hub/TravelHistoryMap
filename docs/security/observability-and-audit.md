# 可观测性与审计

范围：安全事件的**审计**（谁在何时做了什么、成功还是失败）与遥测数据本身的安全（不外泄敏感内容、不被未认证者读写）。

## 现状

| 项 | 现状 | 证据 |
|---|---|---|
| 认证审计 | **完全没有**：`AuthEndpoints.cs` 无任何 `ILogger` 调用；无 `JwtBearerEvents` 的 `OnAuthenticationFailed` / `OnTokenValidated` 钩子 | `user-service/src/User.Api/Endpoints/AuthEndpoints.cs` |
| 请求日志 | 只有通用请求中间件，记录 `Method / Path / StatusCode / ElapsedMs`；**不记请求体、不记 Authorization 头、不记用户 id** | 两个 `Program.cs` 的请求日志中间件 |
| 脱敏 | 删除 `http.request.header.authorization` 的 `attributes` 段**被注释掉**（配置里写着"生产环境可按需打开"） | `infra/observability/otel-collector-config.yaml` |
| Collector | `debug` exporter 的 `verbosity: detailed` **同时挂在 traces / metrics / logs 三条管线**上 | 同上 |
| 采样 | `AlwaysOnSampler`（100%） | `shared/Shared.Observability/Telemetry.cs` |
| SQL 入 trace | 开启 `AddNpgsql()`，每条 SQL 生成 span（含 SQL 文本） | 同上 |
| 观测栈鉴权 | Loki `auth_enabled: false`；Grafana 默认 `admin/admin`；Jaeger / Prometheus / Collector 无认证 | `infra/observability/**`、`docker-compose.dev.yml` |
| 观测栈暴露 | dev 端口全部发布并绑定 `0.0.0.0`；Prometheus 还带 `--web.enable-lifecycle`（允许 `POST /-/reload`） | `docker-compose.dev.yml` |
| 运维端点 | `/metrics`、`/health`、`/swagger` 无条件映射（无环境判断、无鉴权） | 两个 `Program.cs` |
| 生产遥测落点 | `infra/k8s/base/configmap.yaml` 把 `OTEL_EXPORTER_OTLP_ENDPOINT` 指向集群内**不存在**的 `otel-collector`；`deploy-azure.sh` 未设置该变量，应用回落到 `http://localhost:4317` | 两处 |

结论：当前既没有安全事件的**审计**，观测栈本身也是未认证且暴露的面。

## 目标形态

### 审计（安全事件）

必须记录且有留存的事件：

| 事件 | 触发点 | 关键字段 |
|---|---|---|
| `login_succeeded` / `login_failed` | `/identity/login` | 时间、事件、用户 id（失败时可为不可解析标识）、来源 IP、User-Agent、结果 |
| `login_locked` | 失败计数达阈值 | 同上 + 锁定到期时间 |
| `token_refreshed` / `refresh_reuse_detected` | `/identity/token` | `jti`、family id、结果 |
| `logout` / `revoke` | `/identity/logout`、`/identity/revoke` | 用户 id、撤销范围 |
| `password_changed` | user-service 改密 | 用户 id、结果（`CredentialVersion` 自增） |
| `account_deactivated` | 停用账号 | 用户 id |
| `service_token_issued` / `internal_call_denied` | client credentials 与 `/internal/*` 校验 | 调用方 `sub`、`aud`、`scope`、结果 |

**绝不记录**：口令、令牌原文（access / refresh / 分享令牌）、验证码答案、请求体中的凭据字段。

留存要求：审计事件必须落到**可查询、可留存**的后端（见下），不能只存在于容器 stdout——否则"审计"在事故后无法回溯。

### 落地方式（2026-09-18）

- **一个端口，两个服务共用**：`Shared.Observability` 的 `IAuditLog.Write(AuditEvent)`。事件名与字段固定在
  `AuditEventNames` / `AuditLogExtensions`（`login_succeeded`、`login_failed`、`login_locked`、
  `token_refreshed`、`refresh_reuse_detected`、`logout`、`password_changed`、`service_token_issued`、
  `service_token_denied`）—— 改名等于改契约，因此只允许改那一处。
- **类型里没有敏感字段**：`AuditEvent` 只有事件名 / 结果 / 严重度 / 主体（用户 id 或不可解析的账号标识）/
  来源 IP / User-Agent / 事件特有标量。没有字段可以放口令、令牌原文或验证码答案。
- **来源 IP 与 User-Agent 只有一个取法**：`Shared.Observability` 的 `RequestContext`
  （XFF **最右一段**，无则回落 `RemoteIpAddress`）。审计里的 IP 必须与限流用的口径一致，
  否则事故时两边对不上。
- 落点仍是结构化日志（→ OTel → 可查询后端）：事件特有的标量走 scope 属性，不拼进消息文本，
  便于按 `familyId` 之类字段查询。
- 已接入：identity-service 的全部认证事件 + user-service 的改密。后者的测试
  （`ChangePasswordAuditTests`）明确断言"成功必有事件、失败不留事件"。

### 遥测落点（生产）

已核实的事实：ACA 提供**环境级托管 OpenTelemetry agent**，`az containerapp env telemetry app-insights set ...` 即可启用，**零额外 compute 成本**（平台预配资源且不计费），支持 App Insights / Datadog / 任意 OTLP 端点；agent 会**自动注入 `OTEL_EXPORTER_OTLP_ENDPOINT`**，因此 `Shared.Observability/Telemetry.cs` 的 OTLP 导出几乎无需改动。

两个硬限制必须知道：
- **App Insights 端点不收 metrics**（只收 logs + traces）；
- **agent 配置不支持 Key Vault 引用**，且若 App Insights 启用 Entra-only 认证，agent 将收不到数据（必须保留本地认证）。

目标：
1. 生产不再把 OTLP 指向不存在的组件，改为依赖环境级 agent 自动注入的端点。
2. traces + logs → Application Insights（有免费额度）。metrics：用 Azure Monitor managed Prometheus 抓各服务的 `/metrics`，或本期**明确不采集**——不接受"采集失败且无人知道"的中间状态。
3. 采样从固定的 `AlwaysOnSampler` 改为**可配置的比例采样**（`OTEL_TRACE_SAMPLING_RATIO`，0..1；默认仍是全采以保持本地开发行为，生产设 0.1），避免 100% 采样在小额度上烧完配额。**"错误全采"不在应用侧做**——那需要 tail sampling，本文件不承诺该能力。
4. `LiveMetrics` / 查询入口按最小权限配置。

### 脱敏与 PII

- **必须打开** `attributes` 过滤，删除 `http.request.header.authorization`、`cookie`、`set-cookie` 以及任何 token 字段。
- `debug` exporter **只在本地**开启；生产配置中不得出现 `verbosity: detailed`。
- SQL 文本入 span 是既有能力且有排障价值，但**必须确认 span 中不含参数值**（若含，需改为不采集 SQL 文本或做参数打码）。
- connection string 按**低敏感凭据**处理（官方明确它不是安全令牌），但"遥测数据完整性只有弱保证"这一点要写进风险清单：持有它的人可以注入伪造遥测。

### 运维端点的暴露策略

| 端点 | 目标 |
|---|---|
| `/health` | 保留公开（容器探针与边缘健康检查需要）；**不返回任何构建/环境细节** |
| `/metrics` | 仅内部可达（或要求服务身份）；生产由平台侧 Prometheus 抓取，不经网关暴露 |
| `/swagger`、`/openapi/v1.json` | **仅在 Development 开启**；生产关闭 |

## 检查项

### P0

- [x] dev 观测栈收口：所有端口绑定 `127.0.0.1`；Grafana 默认口令已移除（改为必需变量 `GRAFANA_ADMIN_PASSWORD`，缺失即启动失败）；Prometheus 去掉 `--web.enable-lifecycle`。
- [x] 打开 `attributes` 过滤（删除 `authorization` / `cookie` / `set-cookie`）；`debug` exporter 保留在本地配置但降为 `verbosity: basic`（不再打印完整 span/log 内容）。
- [x] 生产遥测端点：`Telemetry.cs` 改为"未显式配置端点就不启用 OTLP"（不再回落到 `localhost:4317`）；k8s configmap 移除指向不存在 collector 的配置；`deploy-azure.sh` 设 `OTEL_TRACE_SAMPLING_RATIO=0.1`。

### P1

- [ ] 实现上表的审计事件（先做认证相关 6 个），并确认落到可查询、可留存的后端。
- [ ] 采样策略调整（比例采样 + 错误全采）。
- [ ] `/swagger`、`/openapi/v1.json` 加环境判断；`/metrics` 收紧。
- [ ] 验证 trace 中的 SQL span 不含参数值。
- [ ] App Insights 访问权限最小化；确认 connection string 不在仓库内。

### P2

- [ ] metrics 正式入库（managed Prometheus）与告警：登录失败率、锁定次数、`refresh_reuse_detected` 次数、5xx 率。
- [ ] 若需要自托管观测栈，改为独立环境 + 认证 + 不与生产同环境。

## 验收方式

- 触发一次成功登录与一次失败登录 → 两个事件都能在查询后端里按用户 id / IP 检索到，且**搜不到口令与令牌原文**。
- 用 grep 扫 trace / log 落库内容 → 不出现 `Bearer `、`authorization` 头值、`password` 字段值。
- 生产环境访问 `/swagger` → 404 或 401。
- 从宿主机以外访问 dev 的 Grafana / Loki / Jaeger 端口 → 连接被拒。
- 故意触发一次 `refresh_reuse_detected` → 审计中出现该事件，且该族全部失效。
