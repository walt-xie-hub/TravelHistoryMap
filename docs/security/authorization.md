# 授权（Authorization）

范围：谁能对哪个资源做什么。本项目**只有一条规则**——资源归属规则（owner-only）；本文件的任务是让这条规则在各处一致，并守住分享令牌的边界。

相关决策：ADR-0021（`sub` 与 claims）、ADR-0019（鉴权在服务侧）。

## 现状

| 项 | 现状 | 证据 |
|---|---|---|
| 端点保护 | 只有 group 级 `RequireAuthorization()`，**无 policy、无角色、无 scope** | `Travel.Api/Endpoints/TravelEndpoints.cs:16`、`User.Api/Endpoints/UserEndpoints.cs:16` |
| 归属校验 | 应用层手写比较，形如 `record.UserId != userId` → 返回 **404**（不泄露存在性） | `Travel.Application/Services/TravelService.cs` |
| 身份取值 | 每个端点文件各一份 `CurrentUserId`，解析 `NameIdentifier`，**失败得 0**（永不匹配 → 404） | 两个端点文件各一份 |
| 图片权限 | 走记录归属（图片端点在 `/api/travels` group 内） | `TravelEndpoints.cs` |
| 图片归属细节 | `TravelImageService.OwnsRecordAsync` 只判 `UserId`，**未过滤 `DeletedAt`** → 对已进回收站的记录仍可列出/上传图片 | `Travel.Application/Services/TravelImageService.cs` |
| 公开业务端点 | `GET /api/share-snapshots/{token}`（不在 group 内） | `TravelEndpoints.cs` |
| 公开运维端点 | `/health`、`/metrics`、`/swagger`、`/openapi/v1.json` 无条件映射 | 两个 `Program.cs` |
| 生产 CORS | 硬编码 localhost（user-service 允许任意 `http://localhost:*`，travel-service 只允许 `4200`/`8082`），**无环境判断、无配置项** | `User.Api/Program.cs:60`、`Travel.Api/Program.cs:56` |

## 目标形态

### 一条规则，一处实现

- **归属一律取自 token 的 `sub`**（用户 id），不接受路径或查询参数指定他人（ADR-0005 §4 继续有效）。
- 归属规则收敛为**一处可复用的实现**（取当前用户 id + 资源归属断言），而不是每个服务各写一份——当前两份 `CurrentUserId` 与各服务的手写比较是最容易漂移的地方。
- **已软删除的资源一律视为不存在**：归属断言必须同时覆盖 `DeletedAt`，与列表接口的过滤条件保持一致（修掉 `OwnsRecordAsync` 的不一致）。
- 不匹配一律 **404**，不返回 403（避免泄露资源存在性）。

### 不引入角色

当前没有角色模型，因此**不放任何角色/权限 claim**。若将来需要（例如管理员），必须先有明确的管理面与审计，再引入 claim——提前放 claim 会诱导出「按 claim 猜权限」的越权判断。

### 服务 token 与用户 token 分离

服务身份 token（`sub = service:<name>`）**不得**访问用户资源端点。资源服务判断依据是 `sub` 的前缀与 `aud`，而不是「有没有 token」：一个合法的服务 token 依然不能读某个用户的旅行记录。

### 分享令牌是能力令牌，不是主体

`GET /api/share-snapshots/{token}` 是唯一公开的业务端点（ADR-0012）。它的性质必须被明确约束：

- **只授权「读这一份快照」这一件事**，不得用于任何其他端点。
- **不可枚举**：令牌必须是密码学随机、长度足够（不接受自增 id、时间戳、短哈希）。
- **可吊销 + 有过期**：用户撤回分享后必须真正失效。
- **不泄露超出快照范围的内容**：不含图片、不含精确坐标（ADR-0012 的边界不变）。
- 响应不得包含可用于进一步探测的信息（例如内部 id、用户邮箱）。

## 检查项

### P1

- [ ] 归属规则收敛为一处实现，并覆盖 `DeletedAt`（含 `TravelImageService`）。
- [ ] 分享令牌：确认随机性来源与长度、确认过期与吊销语义、确认返回体不含图片/精确坐标/内部标识。
- [ ] CORS 改为配置项：生产只允许实际前端域名（或经网关同源）；移除硬编码 localhost 与 `SetIsOriginAllowed`。
- [ ] 运维端点收紧：`/swagger`、`/openapi/v1.json` 仅在 Development 开启；`/metrics`、`/health` 明确是否需要鉴权或仅限内部（见 `observability-and-audit.md`）。
- [ ] 新增端点的默认姿态写进评审习惯：**不写 `RequireAuthorization()` 就是公开**，评审时必须显式回答"谁可以调"。

### P2

- [ ] 若引入管理员或共享协作（多用户共同编辑一份记录），此时才引入角色/成员模型，并同步更新 `CONTEXT.md`。

## 验收方式

- 用 A 的 token 请求 B 的任意旅行记录（含已进回收站的）→ 期望 **404**（不是 403、不是 200）。
- 对已进回收站的记录执行列图片 / 上传图片 → 期望 **404**。
- 用服务身份 token 请求 `/api/travels` → 期望被拒。
- 分享令牌：两次采样检查随机性；撤回后请求 → 期望 404；响应体搜不到图片 URL、精确坐标、邮箱。
- 从非白名单 Origin 发起跨域请求 → 生产环境期望被拒。
