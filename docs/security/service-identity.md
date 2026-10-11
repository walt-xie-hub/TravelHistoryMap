# 服务身份与内部通道（Service identity）

范围：服务到服务的**认证**（谁在调）与内部通道的**加密**（怎么调）。授权规则仍由 `authorization.md` 与各服务自己负责。

相关决策：ADR-0022（契约与硬规则）、ADR-0019（服务不得依赖"内网"假设）。

## 现状

- **已有一个真实跨服务业务调用**（ADR-0025）：`浏览器 → user-service →（token exchange）→ travel-service`
  的「足迹概览」——对外 `GET /api/users/me/footprint`，内部 `GET /internal/travels/footprint`，scope `travel:read`。
  它是目前**唯一**一处，也是「服务身份 + 用户委托」第一次在真实业务端点上被端到端走通。
- **用户委托（token exchange）已实现**（ADR-0024）：调用方可用「自己的服务凭证 + 用户的令牌」换一枚
  `sub=用户`、`act=服务`、`aud=被调服务` 的短 TTL 令牌；接收侧是 `Shared.Security` 的 `Delegated` 方案，
  发起侧是同一程序集里的 `IDelegatedTokenClient`（**不做缓存**：缓存键漏了 subject 就会把甲用户的令牌发给乙用户）。
- 两服务之间**其余**的协作仍然是共享 `appdb` 与数据库级外键（ADR-0002），以及 `shared/Shared.Contracts` 里
  两个**尚未被使用**的集成事件契约（`IIntegrationEvent`、`UserCreatedEvent`）。
- 网关转发时**不注入任何身份头**，也不剥离客户端伪造的头；服务识别调用方的唯一手段是客户端自带的 `Authorization`（ADR-0019 的现状描述）。
- ACA 上后端都是 `internal`，但 `internal` = 同环境内任意容器应用可达，**没有对等认证**，也没有 NetworkPolicy。
- 环境**未接 VNet**，且网络类型创建后不可改 → 本期不可能用网络策略来隔离。

结论：服务身份基础设施已就绪，真实跨服务业务调用仍需按硬规则单独落地。

## 目标形态

### 凭据形态：client credentials

| 项 | 约定 |
|---|---|
| 注册 | 每个服务注册为一个 **Client**：`ClientId`、`ClientSecretHash`（**哈希存储，不存明文**）、`DisplayName`、`IsActive` |
| 换 token | client credentials：`ClientId` + secret → 短 TTL（≈5 分钟）access token |
| `sub` | `service:<name>`（例如 `service:travel-service`） |
| `aud` | 被调服务的标识，**精确匹配**，一个 token 只对一个目标有效 |
| `scope` | 允许的动作；**起步阶段不预置任何 scope（默认拒绝）** |
| 粒度 | **一个服务一个 Client，一个 Client 一个 secret**。禁止多服务复用同一 secret：共用意味着任意一个服务被攻陷即可冒充其余服务，且轮换/撤销只能一起做（审计里 `sub=service:<name>` 也会失去区分力） |
| 存储 | identity-service 自有表；secret 与用户口令一样只存哈希 |
| 注册方式 | **不提供管理端点**，先以部署期种子/运维脚本写入（少一个管理面就少一类攻击面） |

当前实现：identity-service 启动时读取 `Identity:ServiceClients`，将部署注入的明文 secret 转为 PBKDF2 哈希后幂等写入 `ServiceClients`；资源服务通过 `Shared.Security` 的 `IServiceTokenClient` 使用 `ServiceIdentity:ClientId/ClientSecret` 获取并缓存短期令牌。生产 secret 由 ACA secret 引用，本地由 `.env` 注入。

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

### 用户委托（token exchange，ADR-0024）

当调用链需要“**代表用户**”时（`浏览器 → user-service → travel-service`），转发用户令牌与自定义头两种做法都不成立，
改用 token exchange —— 一次性解决“谁在调”与“代表谁”。

| 项 | 约定 |
|---|---|
| 换令牌 | `POST /identity/token`，`grant_type=urn:ietf:params:oauth:grant-type:token-exchange` |
| 调用方身份 | `client_id` + `client_secret`（= actor，与 client credentials 同一套注册） |
| 主体 | `subject_token` + `subject_token_type=urn:ietf:params:oauth:token-type:access_token`（必须是**用户**令牌） |
| 目标 | `audience` 必须在 `Identity:AllowedServiceAudiences` 内 |
| scope | **必填**且必须已授予；缺失即拒 —— 否则委托令牌就退化成“用户令牌的副本” |
| 令牌内容 | `sub`=用户 id、`act`=`{"sub":"service:<clientId>"}`（RFC 8693 代理链）、`aud`=被调服务 |
| TTL | 与服务令牌相同（5 分钟），不沿用用户令牌的 20 分钟 |
| 接收侧 | `Shared.Security` 的 `Delegated` 方案 + `DelegatedIdentity` 策略 |

接收侧有两道校验不能省：

1. **`aud` 必须等于本服务标识**（取 `ServiceIdentity:ClientId`，缺失或仍是占位符则**拒绝启动**）；
2. **必须要求 `act` 声明存在**——委托令牌与 client_credentials 服务令牌的 `aud` 都是本服务标识，
   光看 `aud` 区分不了；`act` 是服务令牌**不会带**的声明。少了这条，一枚普通服务令牌就能冒充“代表某用户”。

### 与 ADR-0002 的边界

服务身份**不得**被用来做"跨服务同步调用校验用户存在性"。用户存在性仍由数据库外键保证（写入不存在的 `user_id` → 23503 → 400）。服务身份只用于**新增的**、真正需要的跨服务能力。

## 检查项

### 已实现

- identity-service 的部署期 Client 注册与哈希存储。
- 资源服务侧的 client credentials 客户端，包含按 audience/scope 缓存、提前刷新和并发请求去重。
- **用户委托（token exchange）**：签发侧 `TokenExchangeService` + `JwtAccessTokenIssuer.CreateDelegatedToken`；
  接收侧 `Shared.Security/DelegatedIdentity.cs`；验收端点 `Travel.Api/Endpoints/InternalEndpoints.cs`。
  单测：`TokenExchangeServiceTests`、`JwtAccessTokenIssuerTests`（含 act 的嵌套 JSON 形状与 TTL）、
  `DelegatedIdentityTests`（act 解析 + 本服务标识缺失时拒绝启动）。
- **首个真实跨服务调用**（ADR-0025）：发起侧 `Shared.Security/DelegatedTokenClient.cs`（`IDelegatedTokenClient`）；
  被调端点 travel-service `GET /internal/travels/footprint`（要求 `DelegatedIdentity` 策略 + `travel:read`）；
  对外端点 user-service `GET /api/users/me/footprint`（下游不可用 → 503，**不降级成空概览**）；
  端点级 scope 授权用 `DelegatedIdentity.RequireDelegatedScope`，它把 `scope` 按空格拆分 ——
  签发侧把多个 scope 放在**一个**声明里，`RequireClaim("scope", ...)` 的整串精确匹配会在多 scope 令牌上静默失败。
  可重放验收：`scripts/validate-delegation.sh`（需 `curl` + `jq` + 一个真实用户令牌）。

### P1

- identity-service Client 注册表与 client credentials 端点已实现；新增 Client 通过部署 secret 注入，不提供运行时管理端点。
- 资源服务侧的服务身份校验骨架已实现于 identity-service 的 `/internal/*` 策略；新增资源服务端点仍需按 scope 明确授权。
- [x] 明确 `/internal/*` 的前缀约定并写进评审习惯（已在 `AGENTS.md` 的 Security 节与 `README.md` 的硬规则第 2 条）。
- [x] 把上面三条"硬规则"写进 `AGENTS.md` 或本目录索引（见 `AGENTS.md` 的 Security 节与 `README.md` 的「四条硬规则」）。

### P2

- [ ] 首个真实 S2S 调用落地时启用 Dapr（含 mTLS），并把 `includeDapr` 的 trace 接到观测后端。
  > 首个真实调用已于 ADR-0025 落地（未启用 Dapr）；`/internal/*` 目前只靠平台层通道加密。
- [ ] 内部端点限流（本文件「内部端点的形态约定」要求，但 ADR-0025 的端点尚未做；网关也不转发 `/internal/*`，
  所以现在只能靠 ACA internal ingress 与令牌本身）。
- [ ] 若跨服务调用数量增长到需要可观测的调用拓扑，评估服务网格（Istio/Ambient）而非手工 mTLS。

## 验收方式

- 不带 token 调 `/internal/*` → 401；带**用户** token → 401/403；带 `aud` 指向别的服务的服务 token → 401；`aud` 对但 scope 不匹配 → 403。
- 服务 token 的 TTL 实测 ≤ 5 分钟，且过期后必须重新换取（无静默续期）。
- `grep -rn "api/" src/backend/**/Endpoints/*.cs` 中不出现 `/internal` 路径（内部端点不得挂在 `/api/` 下）。
- 启用 Dapr 后：用一个未启用 Dapr 的直接 HTTP 调用目标服务 → 期望失败（证明确实走了加强通道）。

### 用户委托（token exchange）的验收

| 请求 | 期望 | 原因 |
|---|---|---|
| `subject_token_type` 不对 / 缺 `subject_token` | 400 `invalid_request` | RFC 8693 要求显式声明主体类型 |
| `client_secret` 错 / client 未注册 | 400 `invalid_client` | 先验调用方，且不触碰主体令牌 |
| `audience` 不在白名单 | 400 `invalid_target` | 默认拒绝 |
| `subject_token` 过期/伪造/是服务令牌 | 400 `invalid_grant` | 只接受本服务签发的用户令牌 |
| 缺 `scope` 或请求了未授予的 scope | 400 `invalid_scope` | 防止退化成“用户令牌的副本” |
| 带委托令牌调 `/internal/whoami` | **200** | 回显 `subject`（用户 id）、`actingService`（`service:user-service`）、`scopes` |
| 带 **client_credentials 服务令牌**调同一端点 | **403** | 它 `aud` 对得上但不能通过 `RequireClaim("act")` —— **这条证明服务令牌不能冒充用户委托** |
| 带**用户令牌**调 `/internal/whoami` | **401** | 用户令牌 `aud` 是客户端标识，与 `Delegated` 方案的 `ValidAudience` 不匹配 |
| 带委托令牌调**别的**服务 | 401 | `aud` 精确匹配，一个令牌只对一个目标有效 |

### 足迹概览（首个真实业务调用，ADR-0025）

| 请求 | 期望 | 原因 |
|---|---|---|
| 委托令牌调 `GET /internal/travels/footprint`（scope 含 `travel:read`） | **200** | 首个真实内部业务端点 |
| 同端点、令牌缺 `travel:read` | 403 | 端点级 scope 授权 |
| 委托令牌调 `GET /api/travels`（用户端点） | 401 | `aud` 不匹配用户方案 |
| 用户令牌调 `GET /api/users/me/footprint` | **200** | 端到端：浏览器只调 user-service，跨服务那一跳由它自己完成 |
| 同一请求在下游不可用时 | 503 | **刻意不降级成空概览**：静默降级会把链路故障伪装成"你还没记录过旅行" |

上表全部项由 `scripts/validate-delegation.sh` 一次跑完（含 token exchange 的四道闸门与 401/403 边界）：

```bash
SUBJECT_TOKEN='<浏览器 localStorage[\"travel-map.auth.token\"]>' \
USER_SERVICE_CLIENT_SECRET="$(grep '^USER_SERVICE_CLIENT_SECRET=' .env | cut -d= -f2-)" \
bash scripts/validate-delegation.sh
```

一条 curl 就能验通最后三行（`$TOKEN` 为换到的委托令牌）：

```bash
curl -i -H "Authorization: Bearer $TOKEN" http://localhost:8081/internal/whoami
```

### 逐步验证：看见每一枚令牌

`scripts/validate-delegation.sh` 只回 `[PASS]`/`[FAIL]`。要看清**每一步产出的令牌长什么样**，用 `scripts/walk-s2s-auth.sh`：它把请求参数、原始响应、以及每枚 JWT 解码后的声明都打出来，并逐条核对。

先看身份提供方公布的事实：`GET /identity/.well-known/openid-configuration` 的 `grant_types_supported` **必须包含** token exchange，`scopes_supported` 为**空**（scope 是逐 Client 授予的，没有可供客户端发现的全集）；`GET /identity/jwks` 列出的 `kid` 必须包含令牌头里的 `kid` —— 否则接收方一定验签失败。

| 步骤 | 产出的令牌 | 由谁签发 | 关键字段（期望） | 谁验证它 |
|---|---|---|---|---|
| ① 登录 | 用户 access token | identity | `sub`=用户 id、`aud`=`travel-map-client`、`exp-iat`=1200、**无 `act`** | 资源服务的用户令牌方案；换 token 时充当主体令牌 |
| ② client credentials | 服务令牌 | identity | `sub`=`service:user-service`、`aud`=被调服务、**无 `act`**、`exp-iat`=300 | 要求服务身份的端点 |
| ③ token exchange | 委托令牌 | identity | `sub`=用户 id、`act.sub`=`service:user-service`、`aud`=被调服务、`scope`=收窄后的权限、`exp-iat`=300 | 被调服务的 `Delegated` 方案（要求 `act`）+ 端点级 scope 策略 |

三枚令牌最关键的差别是 **`act`**：服务令牌**不会带它**，所以"有没有 `act`"是接收侧区分"服务在调"与"服务代表用户调"的唯一判据（ADR-0024）。第 ⑤ 步用一组**篡改测试**（改签名 / 改 payload 但沿用原签名 / `alg=none`）证明接收侧真的**验签**，而不是"解出 JSON 就信"；再换一枚 `aud` 指向别的服务的委托令牌，证明 `aud` 是精确匹配。

怎么拿 `SUBJECT_TOKEN`（登录有图片验证码，脚本不代登录）：浏览器 DevTools → Application → Local Storage → `travel-map.auth.token`（20 分钟过期）。

```bash
# 宿主机
SUBJECT_TOKEN='<粘贴>' \
USER_SERVICE_CLIENT_SECRET="$(grep '^USER_SERVICE_CLIENT_SECRET=' .env | cut -d= -f2-)" \
bash scripts/walk-s2s-auth.sh
```

```bash
# 容器里（宿主 127.0.0.1 不可达时）。dev 栈的宿主端口只绑 IPv4，所以默认地址用 127.0.0.1。
docker run --rm --add-host=host.docker.internal:host-gateway -v "$PWD:/w" alpine:3.20 \
  sh -c 'apk add --no-cache bash curl jq >/dev/null && \
         IDENTITY_BASE=http://host.docker.internal:8090/identity \
         USER_API=http://host.docker.internal:8080/api \
         TRAVEL_BASE=http://host.docker.internal:8081 \
         SUBJECT_TOKEN=<...> USER_SERVICE_CLIENT_SECRET=<...> bash /w/scripts/walk-s2s-auth.sh'
```

实跑结果（2026-10-07，本地 dev 栈）：**32 项全过**，含 4 项篡改/算法混淆测试与 5 项 `invalid_*` 负例。
