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

## 强制手段（2026-09 补记）

上面写的「只读」最初只是**约定**：identity-service 与 user-service 共用同一个 `appuser` 连接串，
因此它事实上能 `UPDATE` / `DELETE` `Users`。现已改为权限系统里的事实：

- 独立角色 `identity_service`：`NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS`，
  只有库的 `CONNECT`、schema 的 `USAGE` + `CREATE`（建自己那四张表）、以及 `Users` 的 `SELECT`。
- 角色与口令由 `scripts/sql/identity-role.sql` 创建（幂等，口令不在文件里，由调用方经 psql 变量传入）：
  本地走 `scripts/provision-identity-db-role.ps1`，Azure 由 `deploy-azure.sh` 调用。
- `Users` 的 `SELECT` 由**表属主** user-service 在启动时补授（`DatabaseInitializer`）。
  这样就不存在启动时序问题：Azure 上 `min-replicas 0`，`Users` 可能要等第一个请求才被创建。
- identity-service 不再拿到 `appuser` 的口令（compose 与部署脚本都移除了它的 `Db__Password`）。

已实测的行为：`SELECT` 通过；`INSERT` / `UPDATE` / `DELETE` 报 `permission denied for table Users`；
`ALTER` / `DROP` 报 `must be owner of table Users`。

残余：`appuser` 是库属主，因此**它**仍能读写 `RefreshTokens` 等身份表。彻底隔离需要拆库或换掉属主角色，
这属于 ADR-0002（共享 appdb）已经接受的代价。
