# Authentication via self-issued JWT; registration is the only way to create users, travel data is bound to the signed-in user

应用此前**完全没有认证**：users CRUD 端点公开、任何人可无密码创建/编辑用户；地图页通过「用户下拉 + `localStorage` 记忆」选择任意用户，并把客户端传入的 `userId` 作为 travel 查询/写入的归属者——服务端不校验、可被任意伪造（越权读取他人足迹）。`AppUser.PasswordHash` 与 `AvatarUrl` 字段预留但从未使用。

需求：注册与登录；**只有登录用户才能进入主界面**；右上角显示用户名与头像，点击菜单含「修改用户信息」与「登出」。设计收敛如下。

## Decision

### 1. 认证机制：自签发 JWT（轻量、无状态）
- 不引入 ASP.NET Core Identity / 角色 / 会话表；沿用现有 `AppUser` 与共享 appdb。
- **凭据**：注册/改密码时用框架内置 `PasswordHasher`（PBKDF2）哈希写入 `AppUser.PasswordHash`；校验用同一实现。登录成功由 user-service 签发 **HS256 JWT**（`Jwt:Key/Issuer/Audience` 配置），access-token-only、有效期 **7 天**，无 refresh token。
- **登出**：客户端丢弃 token 即完成（无状态，无服务端黑名单）；首个 401 由前端清理登录态并跳回 `/login`。
- **dev 密钥共享**：user-service（签发+验证）与 travel-history（仅验证）在 compose/本地通过同一组 `Jwt__*` 环境变量共享签名密钥。生产（k8s/Azure）由部署 secret 注入，不在代码库。

### 2. 注册是创建用户的唯一途径；users CRUD 端点下线
- 新增 `POST /api/auth/register`（name/email/password）与 `POST /api/auth/login`（email/password）；注册成功只写入用户并返回用户信息，不签发 token，前端引导用户重新登录。
- 移除公开的 `POST /api/users`（创建）、`PUT /api/users/{id}`（任意改人）、`DELETE /api/users/{id}`；移除与列表页配套的 `GET /api/users` 分页端点（无人再消费，且会暴露全部账号）。
- 领域约束保持：`Email` 唯一索引兜底，应用层预检冲突返回 409 语义。

### 3. 「me」资源模式替代任意用户编辑
- `GET /api/users/me`：返回当前用户档案（UserDto 扩展出 `phoneNumber`/`avatarUrl`）。
- `PUT /api/users/me`：改 name/email/phoneNumber/avatarUrl；邮箱唯一性冲突报错。
- `PUT /api/users/me/password`：验证 `currentPassword` 后写 `newPassword`（改密码不要求再次登录）。
- 所有 /me 端点要求有效 JWT，`userId` 一律取自 token（`NameIdentifier`），不接受路径/查询参数指定他人。

### 4. travel-history 同步接 JWT：足迹归属 = token 中的用户（Q9A）
- travel 服务引入同一 JwtBearer 验证；`GET /api/travels` 不再接受客户端 `userId`，只返回 token 用户自己的记录。
- 创建/更新/删除记录时归属与所有权校验以 token 用户为准：`CreateTravelDto` 移除 `UserId`；`GET/PUT/DELETE /api/travels/{id}` 对不属于当前用户的记录一律 404（不泄露存在性）。
- 数据库 FK/`UserId` 列结构不变，仍由 `user_id` 关联 `users.Id`（ADR-0002 不变）。

### 5. 前端：全局守卫 + 登录后个人化
- `/login`、`/register` 为独立公开路由；MainLayout（home/map/profile 等）整体挂 `canActivate`，未登录访问一律重定向 `/login`。
- token 存 `localStorage`；HTTP 层统一附加 `Authorization: Bearer`；401 自动清登录态回 `/login`（auth 端点自身除外）。
- 顶栏右侧用户菜单：头像（`avatarUrl` 为空时显示姓名首字母圆形）+ 用户名 →「修改用户信息」（`/profile`：姓名/邮箱/电话/头像 URL + 改密码区）与「登出」。
- 地图页**移除用户下拉与记忆**，足迹只属于当前登录用户（覆盖 ADR-0003 的「下拉选用户」与 ADR-0004 的「展示任意用户」展示语义）；users CRUD 列表页从导航与路由下线。

### 6. dev 演示数据
- `DatabaseInitializer`（user-service，仅 Development）根据外部配置幂等种子演示账号 `demo@travel.local`；密码以 PBKDF2 哈希存储，不进入代码库。
- 如需地图演示，为其归属的 travel 记录另行 seed（Development only，幂等）。

## Consequences
- ADR-0003「应用无登录概念、userId 由下拉选择」与 ADR-0004「可切换查看任意用户足迹」被本决策取代（展示只针对当前登录用户）。
- travel.http 等手工脚本需先调用登录获取 token 并附加 `Authorization` 头。
- 无角色/授权分级、无 refresh/服务端注销、无找回密码——明确 out-of-scope，留待后续。
- 已存在的旧用户（无 `PasswordHash`）将无法登录，只能通过注册新账号使用；dev 种子保证演示路径可用。
