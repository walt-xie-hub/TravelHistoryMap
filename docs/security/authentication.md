# 认证（Authentication）

范围：用户认证（凭据核对）、令牌签发、OIDC 发现、令牌校验材料发布。**不含**授权（见 `authorization.md`）与机器身份（见 `service-identity.md`）。

相关决策：ADR-0020（凭据归属与 identity-service 职责）、ADR-0021（令牌模型，取代 ADR-0005 §1）、ADR-0023（密钥托管）。

> 实现/未实现的状态只在 [`README.md`](./README.md) 的「实现状态」维护一处；本篇只描述目标形态与验收标准（不要在这里勾选）。

## 现状

| 项 | 现状 | 证据 |
|---|---|---|
| 签发 | user-service 自签 **HS256**，`ExpiresInDays` 默认 **7 天** | `user-service/src/User.Api/Security/JwtTokenFactory.cs:26,41` |
| 校验 | 两个服务各自复制一份校验代码，**共用同一把对称密钥** | `User.Api/Program.cs`、`Travel.Api/Program.cs` |
| 密钥来源 | 仅 `appsettings.Development.json` 里的占位符 + compose 的 `${JWT_KEY}`；**`deploy-azure.sh` 未注入 `Jwt__Key`**（缺 key 启动即抛） | `docker-compose.dev.yml`、`deploy-azure.sh` |
| 刷新 / 吊销 | **无 refresh token、无服务端吊销**；登出 = 客户端丢弃 token | ADR-0005 §1 |
| 口令哈希 | 框架内置 `PasswordHasher`（PBKDF2 v3，随机盐） | `User.Infrastructure/Security/PasswordHasher.cs` |
| 口令策略 | 仅最小长度 8，无复杂度、无常见口令黑名单 | `AuthEndpoints.ValidateCredentials` |
| 验证码 | 4 位数字、**`Random.Shared`（非密码学随机）**、内存缓存 5 分钟、一次性 | `User.Api/Security/CaptchaService.cs:78` |
| 失败治理 | **无失败计数、无锁定** | 全后端无相关实现 |
| 账号停用 | `AppUser.IsActive` 字段存在，但登录流程**从不检查** | `User.Domain/Entities/AppUser.cs:29` |
| 认证审计 | 登录成功/失败/注册/改密**不写任何日志** | `AuthEndpoints.cs` 无 `ILogger` 调用 |
| 传输 | 两个服务都注释掉了 `UseHttpsRedirection()` | `User.Api/Program.cs:122`、`Travel.Api/Program.cs:119` |

### 失败治理（已定值）

`MaxFailedAttempts=5` / `WindowMinutes=15` / `LockoutMinutes=15`（`Identity:Lockout`）。
锁定期间**连凭据查询都不做**；对外与口令错误返回同一个 401，真实原因只进审计日志——
代价是合法用户也看不出被临时锁定，这是刻意选的（不泄露账号状态优先）。

## 目标形态

identity-service 承担认证与令牌；注册与档案留在 user-service（ADR-0020）。

### 端点契约

| 路径 | 用途 | 鉴权 | 期次 |
|---|---|---|---|
| `GET /identity/.well-known/openid-configuration` | OIDC 元数据（issuer、jwks_uri、支持的 grant 与签名算法） | 公开 | P1 |
| `GET /identity/jwks` | 公钥集合（多 `kid` 并存） | 公开 | P1 |
| `GET /identity/captcha` | 图片验证码（答案与登录同进程） | 公开 | P1 |
| `POST /identity/login` | **过渡端点**：验证码 + 凭据 → access + refresh | 公开 | P1 |
| `POST /identity/token` | refresh 轮换换新（`grant_type=refresh_token`） | 凭 refresh | P1 |
| `POST /identity/logout` | 撤销该族 refresh。**只需 refresh token 本身**（持有即可撤销，RFC 7009 的语义）：不要求 access token，因此 access 过期后依然能登出 | 凭 refresh | P1 |
| ~~`POST /identity/revoke`~~ | 与 logout 合并：撤销 = 撤销该族，单独暴露一个“按令牌撤销”的端点没有额外收益（少一个面） | — | — |
| `GET /identity/userinfo` | OIDC 用户信息 | 需 access | P2 |
| `GET /identity/authorize` | Authorization Code + PKCE | — | P2 |

`/identity/*` 与 `/api/*` 平行，经网关同源暴露（ADR-0019 的路径规则）；**issuer 必须是显式配置项**，不从请求推导。

### 令牌 claims（最小集，ADR-0021）

| claim | 用户 token | 服务 token |
|---|---|---|
| `sub` | `Users.Id` 的十进制字符串 | `service:<name>` |
| `iss` | `https://<gateway-FQDN>/identity` | 同左 |
| `aud` | `travel-map-client` | 被调服务标识（**精确匹配**） |
| `exp` / `iat` / `nbf` | access 15–30 分钟 | ≈5 分钟 |
| `jti` | 唯一，用于审计关联 | 同左 |
| `email` | 有 | 无 |
| `name` | **不带**：档案归 user-service，不在令牌里冗余复制（ADR-0020）。客户端登录后自行调 `GET /api/users/me` | 无 |
| 角色 / 权限 | **不放** | 由 `scope` 表达 |

### 凭据读取与即时失效

- identity-service **只读** `Users` 的 `Id`、`Email`、`PasswordHash`、`IsActive`、`CredentialVersion`；user-service 是唯一写者（ADR-0020）。
- 登录与刷新时都必须检查 **`IsActive`**，并比对 **`CredentialVersion`**：不匹配即拒绝（改密码/停用后旧 refresh 立刻失效）。
- 哈希用框架类互认，不共享代码。

### 迁移：HS256 → RS256

1. travel-history 先上线「JWKS 验签（RS256）**+ 暂时保留** HS256 校验」。
2. identity-service 上线并开始签发 RS256，**立即停止签发 HS256**。
3. **共存窗口上限 7 天**（= 最长 token 生命周期），到期**必须删除 HS256 校验分支**与全部 `Jwt__Key` 配置。

#### 窗口是「可执行」的，不是口号

HS256 是否被接受**不取决于 `Jwt:Key` 是否存在**（那等于永久接受），而由 `Shared.Security` 在启动时判定：

| 配置 | 行为 |
|---|---|
| 不配 `Jwt:AllowLegacyHs256`（**默认**） | 只接受 RS256；`Jwt:Key` 即使存在也被忽略 |
| `=true` + `Jwt:LegacyUntil`（ISO-8601 UTC，距今 ≤ 7 天） | 接受 HS256，旧 issuer 一并进入白名单 |
| `=true` 但缺少 / 格式错 / 已过期 / 超过 7 天 | **拒绝启动**（fail closed），错误信息直接点名要改或要删的键 |
| `=true` 但没配 `Jwt:Key` | 拒绝启动 |

- 到期**不需要发版**：请求路径上再按 issuer 判一次，窗口一过旧令牌立刻失效（pod 长期不重启也一样）。
- 因此上面的验收条件更正为：**到期后服务启动即失败**，逼你把 `Jwt:AllowLegacyHs256` /
  `Jwt:LegacyUntil` / `Jwt:Key` 清干净。`grep -r "HmacSha256" src/backend` 仍会命中
  `Shared.Security`——那是白名单本身，不再是「还有旧代码」的证据；可怕的是「还有旧配置」。
- 回归测试：`src/backend/shared/Shared.Security.Tests`（9 条）逐行覆盖上表。
- 何时开启：只有迁移期需要接受**已发出去的旧令牌**时才开。新部署不要开。

### 为什么"按来源 IP"不做硬锁定（决定）

原检查项写的是「按账号 + 按来源 IP 的滑动窗口」。其中**按 IP 的硬锁定**（锁定即拒绝来自该 IP 的登录）
**刻意没有**做成应用逻辑，理由：

- 入口是网关，`limit_req` 已经按**真实客户端 IP**（XFF 最右一段）限到 10 r/m。它同样是
  「按来源 IP 的滑动窗口」，但作用在边缘、成本低，而且不用把状态写进数据库。
- 登录**强制**过图片验证码（先取后验、无论对错都消耗，防重放）。自动化爆破在这一关就被挡住，
  再加一层 IP 锁定的边际收益很小。
- 硬锁定会立刻变成 DoS 工具：攻击者用同一个共享出口 IP（企业 NAT、运营商 CGNAT）刷几次失败，
  就能让整栋楼的用户都登录不了。代价高于收益。

因此应用内的锁定**只按账号**（`LoginAttempts.Key` 是规范化邮箱）；来源 IP 与 User-Agent 进审计事件，
用于事后定位。将来若需要更强的 IP 防御，应当走**软措施**（渐进延迟、提高验证码难度）或边缘 / WAF，
而不是"按 IP 拒绝登录"。

## 检查项

### P0

- [x] **`IsActive` 必须被检查**：identity-service 的登录与刷新路径都检查（`AuthenticationService` / `RefreshTokenService`）。

### P1

- [ ] identity-service 建立：OIDC discovery + JWKS + `/identity/login` + `/identity/token` + `/identity/logout`（契约见上表）。
- [ ] RS256 签发、JWKS 发布、资源服务改为只验签（不再持有签名密钥）。
- [ ] refresh：7 天、每次轮换、**旧 token 复用即整族撤销**。
- [ ] `CredentialVersion` 比对接入登录与刷新路径。
- [ ] 验证码迁到 identity-service，随机数改用 `RandomNumberGenerator`；验证码端点限流。
- [ ] 登录失败计数与锁定：阈值与锁定时长已定值（见上文）；**来源 IP 维度改为边缘限流 + 强制验证码，理由见上**。
- [ ] 认证审计事件：`login_succeeded` / `login_failed` / `login_locked` / `token_refreshed` / `refresh_reuse_detected` / `logout` / `password_changed`；字段为「时间、事件、用户 id（或不可解析标识）、来源 IP、User-Agent、结果」，**绝不记录口令、令牌原文、验证码答案**。
- [ ] HS256 退役：删除共享对称密钥配置，更新 ADR-0005 状态为 superseded。

### P2

- [ ] Authorization Code + PKCE + `id_token` + `/userinfo`；前端令牌改内存存储。
- [ ] 口令策略提高到 12 位并引入常见口令黑名单。
- [ ] 找回密码 / 邮箱验证流程（需要新的令牌语义与邮件通道）。

## 验收方式

- 用停用账号登录 → 期望 401（不是 200）。
- 改密码后用旧 refresh 换新 → 期望被拒；用新口令登录 → 成功。
- 同一个 refresh 连续使用两次 → 第二次失败，且该族全部失效。
- 把 access token 的 `aud` 改成别的值 → 资源服务拒绝。
- `curl /identity/.well-known/openid-configuration` 返回的 `issuer` 与实际签发的 `iss` **字符串完全一致**。
