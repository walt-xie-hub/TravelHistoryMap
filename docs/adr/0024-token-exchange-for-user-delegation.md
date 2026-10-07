# 用户委托用 token exchange：sub=用户 + act=服务，aud 精确到被调服务

## Status

Accepted

## Context

- ADR-0022 定下了服务身份的形态（client credentials、`aud` 精确匹配、`/internal/*`、scope 默认拒绝），但那只回答"**谁在调**"。
- 当调用链是 `浏览器 → user-service → travel-service` 时，还缺另一半：**代表谁**。
  travel-service 需要知道"要返回哪个用户的数据"，并且在审计上要能区分"用户自己调的"与"某个服务以他的名义调的"。
- 两条看似省事的做法都不成立：
  - **转发用户令牌**：用户令牌的 `aud` 是客户端标识，拿到别的服务上按 `aud` 精确匹配应当被拒；
    而且转发等于让用户令牌"哪里都能用"，调用方也无法收窄权限（`service-identity.md` 明确列为不允许的形态）。
  - **用一个自定义头声明用户**（如 `X-On-Behalf-Of-User`）：这个头**不可验证**，安全性只能依赖
    "网关不转发该头 + 只有持服务令牌者能到达 `/internal/*`"这两条外围约束，而不是令牌本身。
- 平台侧只解决通道（ACA internal TLS；Dapr mTLS 见 ADR-0022 的分期），**不解决身份**。

## Decision

新增 **token exchange**（RFC 8693 的 on-behalf-of 流程）：调用方用「自己的服务凭证 + 用户的令牌」
向 identity-service 换一枚**重新签发**的令牌，把两个身份装进同一枚令牌。

### 端点与参数

`POST /identity/token`，`application/x-www-form-urlencoded`：

| 参数 | 值 |
|---|---|
| `grant_type` | `urn:ietf:params:oauth:grant-type:token-exchange` |
| `client_id` / `client_secret` | 调用方自己的服务凭证（= actor，与 client credentials 同一套注册） |
| `subject_token` | 用户的 access token |
| `subject_token_type` | `urn:ietf:params:oauth:token-type:access_token` |
| `audience` | 被调服务标识，必须在 `Identity:AllowedServiceAudiences` 内 |
| `scope` | **必填**，且必须已被授予给该 client |

### 签发出的令牌

| 声明 | 值 | 回答的问题 |
|---|---|---|
| `sub` | 用户的 `Users.Id`（十进制） | **代表谁** |
| `act` | 嵌套 JSON `{"sub":"service:<clientId>"}`（RFC 8693 代理链） | **谁在调** |
| `aud` | 被调服务标识，精确匹配 | 一个令牌只对一个目标有效 |
| `scope` | 本次调用允许的动作 | 权限收窄 |
| TTL | 与**服务令牌相同**（5 分钟），不沿用用户令牌的 20 分钟 | 它同时携带用户身份，泄露后果更重 |

### 裁决（全部默认拒绝）

1. 调用方凭证无效 → `invalid_client`（**先验调用方**，未认证的请求没有机会试探主体令牌）；
2. `audience` 不在白名单 → `invalid_target`；
3. `subject_token` 验签/过期失败，或账号已停用 → `invalid_grant`；
4. `scope` 缺失或未被授予 → `invalid_scope`。

第 4 条的"缺失也拒绝"是刻意的：否则委托令牌就退化成"用户令牌的副本"，
而收窄权限恰恰是这个流程存在的理由之一。

### 接收侧

- `Shared.Security` 新增 **`Delegated` 认证方案 + `DelegatedIdentity` 策略**（与用户令牌是两个方案，而不是一个方案加判断）。
- `aud` 必须等于**本服务标识**（取 `ServiceIdentity:ClientId`，缺失或仍是占位符即**拒绝启动**）。
- 策略**要求 `act` 声明存在**。这是关键判别位：委托令牌与 client_credentials 服务令牌的 `aud`
  都是本服务标识，光看 `aud` 无法区分；而 `act` 是服务令牌**不会带**的声明 ——
  少了这条要求，一枚普通服务令牌就能冒充"代表某用户"。
- 免走 JwtBearer 的 `ValidateToken` 之外不新增任何机制：公钥仍经 OIDC 发现取得，算法白名单仍只有 RS256。

## Consequences

- **跨服务调用多一次往返**：每枚委托令牌都要过一次 identity。TTL 短到 5 分钟意味着高频调用会反复换取 ——
  服务端按 `(subject, audience, scope)` 缓存是后续的优化点，但**不能**套用现有 `IServiceTokenClient` 的
  缓存键（它只有 `(audience, scope)`，会把甲用户的令牌发给乙用户）。
- **identity-service 仍在关键路径上**：它不再只服务于登录，还服务于服务间调用。这与 ADR-0021
  "资源服务验签零运行时调用"的取向形成一个有意为之的例外 —— 换取委托令牌本身需要它，但**校验**委托令牌仍是离线的。
- **scope 体系必须先于实现存在**：新增一次委托调用 = 一次注册动作（给 client 授予 scope），延续 ADR-0022 的摩擦。
- **用户令牌不再流出调用方**：它只到 identity 为止，服务间流动的是收窄过的新令牌，暴露面小于转发方案。
- **未关闭的门**：`CredentialVersion` 目前只在换取时检查账号是否停用；已签发的委托令牌在 5 分钟内
  仍可能有效。这是 TTL 换来的简洁，若要即刻失效需引入撤销清单（当前不做）。
