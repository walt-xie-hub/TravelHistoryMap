# Gateway 只做边缘工作，鉴权永远在服务侧，且服务的可达性不等于可信

## Status

Accepted

## Context

现状（已核实）：

- 网关（`src/gateway/nginx.conf.template`）是纯 nginx 转发：`listen 80`、无 `limit_req`、无 `auth_request`，**既不注入也不剥除任何身份头**；TLS 由 Azure Container Apps 边缘终止。
- 路由是前缀全量转发：`/api/*` → user-service，`/api/travels`、`/api/share-snapshots` → travel-service，`/` → client。因此**新增在 `/api/` 下的端点会自动对公网可达**。
- 部署上只有 gateway 是 `external`，后端均为 `internal`（`deploy-azure.sh`）；但 ACA 的 `internal` 语义只是「同一环境内的容器应用可达」——没有 mTLS、没有 IP 白名单、没有 NetworkPolicy，而且**当前环境未接 VNet，且网络类型创建后不可改**。

结论：任何已进入同一环境的进程都能直连任一后端，绕过网关的全部边缘控制。

## Decision

- **网关只做边缘工作**：TLS 强制、请求规范化、限流、安全响应头、body 上限。它**不注入任何身份头**（不引入 `X-User-Id` 这类隐式身份）。
- **鉴权永远在服务侧**：每个服务自己验证调用方身份（用户 JWT 或服务身份 token）。网关不是授权点。
- **客户端头不可信**：`X-Forwarded-For` / `X-Forwarded-Proto` 由网关重写；任何 `X-User-*`、`X-Internal-*` 一律在边缘剥除；服务不读取任何未经自己验证的身份头。
- **内部端点不得落在网关已转发的路径前缀下**（即不得放在 `/api/*`）：统一使用 `/internal/*`，并且必须要求服务身份。
- **暴露面约束**：生产只允许 gateway 为 `external`；其他容器应用保持 `internal`，且不得被登记为环境级 HTTP 路由的目标。

## Considered Options

- **网关注入身份头（`X-User-Id`）作为服务信任的身份来源** —— 拒绝。在「同一环境内可直连服务」的现实中，服务无法区分该头来自网关还是伪造者，这会造出一个可冒充任意用户的通道。
- **网关做集中鉴权（`auth_request`）** —— 拒绝。授权（归属规则）与业务数据在同一处，把它搬到边缘会把业务规则塞进 nginx 配置，且服务仍需自证。

## Consequences

- 攻击者若已进入同一环境仍可直连服务；该风险由「服务自证身份」兜住，进一步的网络层收口需要把环境重建为 VNet 集成（见 `docs/security/network-and-edge.md` 的 P2）。
- `/api/*` 的全前缀转发意味着**新增业务端点默认会公开**。因此每个新增端点必须显式回答「谁可以调」，默认要求鉴权；内部端点必须走 `/internal/*`。
