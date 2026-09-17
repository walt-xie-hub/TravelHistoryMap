# 令牌模型：RS256 + 公开 JWKS、短 access token、轮换 refresh token

## Status

Accepted（取代 ADR-0005 的第 1 节）

## Context

ADR-0005 §1 当初刻意选了最轻的方案：user-service 自签 HS256（`Jwt:Key` 由部署 secret 注入，两个服务共用同一把对称密钥）、有效期 7 天、无 refresh、无服务端吊销、登出等于客户端丢弃 token，并把 refresh 与服务端注销明确列为 out-of-scope。

新需求把这些拉回范围内：OIDC 发现、服务身份、内部加密。同时 HS256 的共享密钥形态在当前部署里已经暴露问题——`deploy-azure.sh` 根本没有注入 `Jwt__Key`，而两个服务缺 key 就会启动即抛异常，说明线上那把密钥只存在于某个手工配置里，仓库无法追踪它、更无法轮换它。

## Decision

- 签名改为**非对称 RS256**，公钥经 **JWKS** 发布；资源服务（travel-history、user-service）**验签即可**，不再持有任何签名密钥。
- issuer 为 `https://<gateway-FQDN>/identity`，**是显式配置项而非从请求推导**；discovery 位于 `/identity/.well-known/openid-configuration`。
- `sub` = `Users.Id` 的十进制字符串。用户 id 自增且不复用（删用户会级联删其旅行史），因此无需引入不透明 subject 与映射表。
- claims 最小集：`sub`、`iss`、`aud`、`exp`、`iat`、`nbf`、`jti`、`email`。**不放任何角色/权限 claim**——当前没有角色模型，提前放会诱导出越权判断。
  `name` 也**不放**：档案字段归 user-service，令牌里冗余复制只会制造第二个真相源（客户端登录后自行调 `GET /api/users/me`）。
- `aud` 必须精确匹配：用户 token 为 `travel-map-client`；服务 token 为被调服务标识。
- 有效期：access token **15–30 分钟**；refresh token **7 天**，**每次刷新即轮换**，旧 token 立即失效，且**旧 token 被再次使用即视为泄露、整族（family）撤销**。
- 登出 = 撤销该族 refresh；access token 靠短 TTL 兜底。
- **第一期前端仍用自有登录端点换取令牌**（`POST /identity/login`），它是明确标注的过渡端点；Authorization Code + PKCE、`id_token`、`/userinfo` 的迁移单列（见 `docs/security/authentication.md` 的 P2）。

## Consequences

- ADR-0005 §1 被取代；其 §2（注册是创建用户的唯一途径）、§3（`/me` 资源模式）、§4（归属取自 token 而非请求参数）**继续有效**。
- access token 因此是无状态、不可即时撤销的（除 `CredentialVersion` 比对），安全上依赖短 TTL——这是选 RS256 + 自验签的直接代价，换来的是资源服务与身份服务之间**零运行时耦合**。
- 迁移不是一刀切：travel-history 先上线「JWKS 验签（RS256）+ 暂时保留 HS256 校验」，identity-service 上线即停止签发 HS256，**共存窗口上限 7 天（= 最长 token 生命周期），到期必须删除 HS256 分支**。
