# identity-service 只拥有认证与令牌，User 聚合仍归 user-service，凭据列有唯一写者契约

## Status

Accepted

## Context

- 现状由 user-service 同时拥有档案与凭据（`Users.PasswordHash`）并签发 HS256（ADR-0005）。
- 新需求要一个独立身份服务提供 OIDC 发现、JWT 签发与验签材料、服务身份（client credentials）。
- ADR-0002 是硬约束：两个服务共享 `appdb`、以数据库外键保完整性、**不引入跨服务同步调用**。

把「凭据」从「档案」里切出去，天然会产生三种收场：跨服务调用、双写、或让身份服务读同一张表。前两种都会破坏 ADR-0002。

## Decision

- **identity-service 只负责**：认证（校验凭据）、令牌签发与验签材料发布（OIDC discovery / JWKS）、刷新与吊销、服务客户端（Client）注册与 client credentials。**它不拥有任何 User 数据，不写 `Users` 表。**
- **user-service 仍是 `Users` 表的唯一写者**，包含凭据列（`PasswordHash`、`CredentialVersion`）。identity-service 对 `Users` 只有**只读**权限，且只读认证所需的最小列：`Id`、`Email`、`PasswordHash`、`IsActive`、`CredentialVersion`。
- 因此**登录路径零跨服务调用**：identity-service 直接读同一 `appdb` 的凭据列完成认证。
- 凭据哈希格式由框架类 `Microsoft.AspNetCore.Identity.PasswordHasher`（PBKDF2 v3）定义，两个服务各自调用框架实现即可互认，**不共享代码**。
- **改密码 / 停用的即时失效**由 `Users.CredentialVersion` 承担：user-service 在改密码或停用时自增，identity-service 在认证与刷新时比对，不匹配即拒。这避免了「改密码后还要打通另一个服务」的可用性耦合。
- 列归属即刻成为契约：新增列必须写明归属；**identity-service 永远不得获得 `Users` 的写权限**。

## Considered Options

- **identity-service 拥有凭据表，user-service 拥有档案** —— 拒绝。注册要同时写两处，于是要么跨服务调用、要么双写。
- **user-service 暴露内部凭据校验端点**（`/internal/credentials/verify`）—— 拒绝。它会把**历史上第一个跨服务调用放在登录热路径上**（identity-service 或 user-service 一挂就没人能登录），还引入一个「给我 email + 密码我告诉你对不对」的端点——该端点一旦落到 `/api/` 前缀下就会经网关公开，成为密码预言机。

## Consequences

- 两个服务对同一张表各有一套模型，因此 schema 变更需要契约与两侧同步——这是 ADR-0002 已经接受的「共享 appdb」代价的延续。
- 「改密码后立即全局失效」不能靠内存状态，必须经 `CredentialVersion`。
- 若将来要让身份服务脱离这个库独立演进，必须先做一次凭据迁移（会引入注册期的跨服务协调）。**本决策明确选择不这样做。**
