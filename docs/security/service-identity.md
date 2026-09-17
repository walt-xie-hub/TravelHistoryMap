# 服务身份与内部通道（Service identity）

范围：服务到服务的**认证**（谁在调）与内部通道的**加密**（怎么调）。授权规则仍由 `authorization.md` 与各服务自己负责。

相关决策：ADR-0022（契约与硬规则）、ADR-0019（服务不得依赖"内网"假设）。

## 现状

- **后端零服务间调用**：在 `src/backend/**` 搜 `HttpClient`、`AddHttpClient`、`Yarp`、`MassTransit`、`RabbitMQ` **零命中**（命中的 `HttpClient` 全在前端）。
- 两服务之间唯一的"协作"是共享 `appdb` 与数据库级外键（ADR-0002），以及 `shared/Shared.Contracts` 里两个**尚未被使用**的集成事件契约（`IIntegrationEvent`、`UserCreatedEvent`）。
- 网关转发时**不注入任何身份头**，也不剥离客户端伪造的头；服务识别调用方的唯一手段是客户端自带的 `Authorization`（ADR-0019 的现状描述）。
- ACA 上后端都是 `internal`，但 `internal` = 同环境内任意容器应用可达，**没有对等认证**，也没有 NetworkPolicy。
- 环境**未接 VNet**，且网络类型创建后不可改 → 本期不可能用网络策略来隔离。

结论：现在没有任何服务身份机制，也没有一个真实的调用点需要它。因此这件事的形态是**契约先行**。

## 目标形态

### 凭据形态：client credentials

| 项 | 约定 |
|---|---|
| 注册 | 每个服务注册为一个 **Client**：`ClientId`、`ClientSecretHash`（**哈希存储，不存明文**）、`DisplayName`、`IsActive` |
| 换 token | client credentials：`ClientId` + secret → 短 TTL（≈5 分钟）access token |
| `sub` | `service:<name>`（例如 `service:travel-service`） |
| `aud` | 被调服务的标识，**精确匹配**，一个 token 只对一个目标有效 |
| `scope` | 允许的动作；**起步阶段不预置任何 scope（默认拒绝）** |
| 存储 | identity-service 自有表；secret 与用户口令一样只存哈希 |
| 注册方式 | **不提供管理端点**，先以部署期种子/运维脚本写入（少一个管理面就少一类攻击面） |

### 内部端点的形态约定

- 前缀 **`/internal/*`**，**不得**落在网关转发的 `/api/*` 下（ADR-0019）。理由：`/api/*` 是前缀全量转发，任何挂在下面的端点都会自动对公网可达。
- 必须**要求服务身份**：无 token → 401；token 但 `aud` 不匹配 → 401；`aud` 匹配但缺 `scope` → 403。
- 必须**拒绝用户 token**：用户 token 的 `sub` 不是 `service:*`，一律 403。
- 必须**限流**并**记录调用方 `sub`**（审计需要知道是谁调的）。

### 硬规则（准入条件）

任何新增的跨服务 HTTP 调用，必须同时满足，否则不予合并：

1. 携带服务身份 token；
2. 经加密通道；
3. 命中一个要求服务身份的目标端点。

不允许的形态：靠"内网所以可信"的裸 HTTP 调用；把用户 token 当服务身份用；用共享静态密钥代替 token。

### 通道加密分期

| 期次 | 机制 | 说明 |
|---|---|---|
| 本期 | 平台层：ACA internal TLS（`transport=Auto`）+ 环境级加密 | **只加密，不做对等认证**——客户端不持证书 |
| 首个真实 S2S 调用出现时 | **Dapr 服务调用** | ACA 上 GA，官方明示自带 mTLS 与加密、证书由平台轮换，零自管 CA |
| 明确不采用 | 自管 CA + 把证书塞进 secret 自己轮换 | 轮换成本对这个规模是纯负担，且平台不帮忙 |

> "加密"与"对等认证"是两个正交的轴：平台内网 TLS 只保证链路机密性；**识别调用方必须靠 token**。不要把前者当作后者。

### 与 ADR-0002 的边界

服务身份**不得**被用来做"跨服务同步调用校验用户存在性"。用户存在性仍由数据库外键保证（写入不存在的 `user_id` → 23503 → 400）。服务身份只用于**新增的**、真正需要的跨服务能力。

## 检查项

### P1

- [ ] identity-service 实现 Client 注册表与 client credentials 端点（契约见上）。
- [ ] 资源服务侧实现「服务身份校验中间件」骨架：校验 `iss`/`aud`/`sub` 前缀/`scope`/`exp`，默认拒绝。
- [ ] 明确 `/internal/*` 的前缀约定并写进评审习惯。
- [ ] 把上面三条"硬规则"写进 `AGENTS.md` 或本目录索引，让新增调用必须过这一关。

### P2

- [ ] 首个真实 S2S 调用落地时启用 Dapr（含 mTLS），并把 `includeDapr` 的 trace 接到观测后端。
- [ ] 若跨服务调用数量增长到需要可观测的调用拓扑，评估服务网格（Istio/Ambient）而非手工 mTLS。

## 验收方式

- 不带 token 调 `/internal/*` → 401；带**用户** token → 403；带 `aud` 指向别的服务的服务 token → 401；`aud` 对但 scope 不匹配 → 403。
- 服务 token 的 TTL 实测 ≤ 5 分钟，且过期后必须重新换取（无静默续期）。
- `grep -rn "api/" src/backend/**/Endpoints/*.cs` 中不出现 `/internal` 路径（内部端点不得挂在 `/api/` 下）。
- 启用 Dapr 后：用一个未启用 Dapr 的直接 HTTP 调用目标服务 → 期望失败（证明确实走了加强通道）。
