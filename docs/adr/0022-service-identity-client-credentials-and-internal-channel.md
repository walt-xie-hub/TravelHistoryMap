# 服务身份用 client credentials 且默认拒绝 scope；首个跨服务调用必须先满足通道与身份要求

## Status

Accepted

## Context

- 当前后端**零服务间调用**：在 `src/backend/**` 搜 `HttpClient|AddHttpClient|Yarp|MassTransit|RabbitMQ` 零命中。所以「服务到服务认证」现在是契约问题，不是修 bug 问题。
- ACA 的 `internal` ingress 只保证「同一环境内的容器应用可达」，**不提供对等认证**；网关转发时不注入任何身份头（ADR-0019）。
- 需求要求内部服务之间的访问加密，可用 mTLS 或 token。

## Decision

- **服务身份由 identity-service 签发**：每个服务注册为一个 **Client**（`ClientId` + 哈希存储的 secret），用 client credentials 换取短 TTL（≈5 分钟）token；`sub = service:<name>`，`aud` = 被调服务，`scope` = 允许的动作。
- **默认拒绝**：起步阶段**不预置任何 scope**。新增跨服务能力时按需开 scope 并记录在案。
- **硬规则（准入条件）**：任何新增的跨服务 HTTP 调用，必须
  1. 携带服务身份 token；
  2. 经加密通道；
  3. 命中一个**要求服务身份**的目标端点（不得依赖「内网所以安全」的假设）。
  不满足者不予合并。
- **内部端点使用独立前缀 `/internal/*`**，不得落在网关转发的 `/api/*` 下（ADR-0019），并**拒绝**用户 token 访问。
- **通道加密分期**：先使用平台层（ACA internal TLS + 环境级加密）；**引入 Dapr（自带 mTLS 与证书自动轮换）在第一个真实 S2S 调用落地时启用**，不以「预防性基建」的名义提前引入 sidecar。自管 CA 的方案明确不采用。
- 服务身份**不得**被用来做「跨服务同步调用校验用户存在性」——ADR-0002 已明确否掉这一点，完整性仍由数据库外键保证。

## Consequences

- scope 体系先于实现存在：新增一次调用需要一次注册动作，这是有意的摩擦。
- 若不引入 Dapr，则「对等认证」只由 token 提供，传输层只有服务端证书（客户端不持证书）——即加密有了、对等认证只到 token 层。
- 「加密」与「对等认证」是两个正交的轴：平台内网 TLS 只加密，不识别调用方；识别调用方必须靠 token。
