# 首个真实跨服务调用：user-service 以用户委托读 travel 的足迹概览

## Status

Accepted

## Context

- ADR-0022 给了服务身份（"谁在调"），ADR-0024 给了用户委托（"代表谁"），但**两者都没有真实调用点**：
  `docs/security/service-identity.md` 当时写着"当前后端仍零真实服务间业务调用"。
  只有 travel-service 的 `/internal/whoami` 验收端点能证明链路通，它不返回任何业务数据。
- 于是出现了"基础设施齐备但从未在真实压力下走过一次"的状态：换令牌的**发起侧代码根本不存在**
  （`IServiceTokenClient` 的缓存键 `(audience, scope)` 不含 subject，ADR-0024 明确禁止复用它），
  委托令牌的 scope 收窄（裁决 ④）也没有任何一个端点真的按它授权。
- 需求侧：个人资料页只有账号信息，与"这个人去过哪里"完全脱节；而足迹聚合数据由 travel-service 拥有。

## Decision

新增 **足迹概览**（Footprint overview），作为本项目**首个真实跨服务调用**：
`浏览器 → user-service →（向 identity 换取用户委托令牌）→ travel-service`。

| 项 | 决定 |
|---|---|
| 方向 | user-service 发起（它已注册 `IServiceTokenClient`，是天然的调用方）；travel-service 被调（它已注册 `Delegated` 接收方案） |
| 机制 | **用户委托令牌**（token exchange，ADR-0024），不是纯服务身份 —— 因为这次调用代表着一个具体的人 |
| 发起侧 | 新增 `Shared.Security/IDelegatedTokenClient`，与接收侧 `DelegatedIdentity` 同处一个程序集，**不做缓存** |
| 被调端点 | travel-service `GET /internal/travels/footprint`，要求 `DelegatedIdentity` 策略 + `travel:read` |
| 对外端点 | user-service `GET /api/users/me/footprint` —— **独立端点**，不塞进 `GET /users/me` |
| 失败语义 | **硬失败**：下游 5xx/不可达 → 503；我们自己配置错（scope 未授予、aud 未登记）→ 500 且消息直指要改的注册项 |
| 授权登记 | 给 `user-service` 这个 Client 授予 `travel:read`（identity appsettings）；`travel-service` 早已在 `AllowedServiceAudiences` 内 |
| 契约 | `Shared.Contracts/FootprintOverview` 与 `ServiceScopes`：两端共用同一类型与同一个 scope 字面量，避免契约漂移 |

**归属只来自令牌。** 请求体/查询串里没有任何 `userId`，服务端只读委托令牌的 `sub`
（`DelegatedUserId()`）；`act` 存在但 `sub` 不可解析时**拒绝**，而不是回落到 0。

## Considered Options

- **浏览器直接调 travel-service 拿聚合**（不加任何跨服务调用）：最省事，也符合 ADR-0007
  "客户端直接调属主服务"的取向。放弃的原因是本次的目标恰恰是**把已有但从未运行的服务身份设施
  跑通一次**；顺带得到一个"资料页自带足迹概览"的产品效果。代价是 user-service 承担了一点聚合语义
  （一个最小的 BFF 迹象）—— 这是明确接受的，且仅限这一处。
- **共享库 JOIN `Users` / 在 travel 侧直读**：两服务共享 `appdb`，travel 也已有 `Users` 外键，
  直接 SQL 就能拿到数据，完全不需要 HTTP。放弃的原因是它会让 travel-service 触碰它不拥有的表，
  把 ADR-0020 刚建好的最小权限模型（只有 identity 有 `Users` 的 `SELECT`）搞浑。
- **异步事件**（`Shared.Contracts` 里那两个尚未使用的集成事件）：彻底解耦，但引入消息基础设施
  和最终一致性，与"阅读一个页面上的卡片"这种即时读取的语义不匹配，且会让"验证链路"这件事
  变成"验证一个后台消费者"，更难断言。
- **降级成"空概览"**：下游挂了就显示 0 条记录。放弃的原因是它把链路故障伪装成"你还没记录过旅行"，
  与本次"证明链路真的通"的目标直接冲突。
- **缓存委托令牌**：TTL 只有 5 分钟，收益小；而缓存键漏了 `subject` 就会把甲用户的令牌发给乙用户
  （ADR-0024 已点名）。首版不做，等有明确需求再谈。

## Consequences

- **identity-service 进入资料页的关键路径**：这次调用要先换令牌。它是既有取向
  （ADR-0021"资源服务验签零运行时调用"）的一个**有意为之的例外**，与 ADR-0024 的记载一致。
- **每次请求都真换一次令牌**：不缓存意味着资料页刷新会各换一次。这既是安全上的保守选择，
  也是本次验证目的的一部分（每次都走真实换取，而不是命中缓存后"看起来是绿的"）。
- **本地与生产各多一次网络跳**：本地 `user-service → travel-server:8080`（容器名）；
  Azure 上走 `travel-service.internal.<env>.<region>.azurecontainerapps.io`，但换令牌仍绕行
  `ServiceIdentity:Issuer`（= 公网网关 FQDN）—— 已知绕行，未在本期优化。
- **失败会直接暴露给用户**：卡片有独立的失败态与"重试"，档案与改密不受影响。
- **仍未做的**：内部端点**未限流**（`service-identity.md` 的检查项要求限流；本期只做到鉴权），
  Dapr mTLS（P2）也未启用 —— `/internal/*` 目前靠 ACA internal ingress 提供通道加密，
  没有对等认证以外的额外保护。委托令牌在 5 分钟内不随改密码立即失效（ADR-0024 的已知取舍）。
- **ADR-0002 的红线仍然有效**：本调用是**读取聚合**，不是"跨服务同步调用校验用户是否存在"。
  服务身份/委托**不得**被用来做用户存在性校验（那件事仍由数据库外键保证）。
