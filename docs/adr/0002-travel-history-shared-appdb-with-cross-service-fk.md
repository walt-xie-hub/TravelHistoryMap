# travel-history shares appdb with user-service via cross-service FK and CreateTables bootstrap

新服务 travel-history **不复用 user-service 的 EnsureCreated 单库引导**，而是与它共享既有 `appdb`（compose / k8s / Azure 三处已统一的单库事实），代码层保持服务自治：travel-history 不引用 `User.Domain`，`user_id` 指向 `users.Id` 的完整性由**数据库级外键**保证。

选型原因与约束：
- EF Core 官方文档：`EnsureCreated` 只要数据库里存在任意表（包括其他 DbContext 的表）就**整体跳过建表**，多 DbContext 共享一库时不可用。因此两服务都采用「`EnsureCreated`（建库/空库建表）+ 自查自己的表缺失时 `IRelationalDatabaseCreator.CreateTables()` 强制补建」的引导，任何启动顺序都正确，user-service 的 `Program.cs` 同步做了最小改造（数行）。
- 外键 `TravelRecords(user_id) REFERENCES users(Id) ON DELETE CASCADE` 由幂等原生 SQL 在启动时补建（两服务模型间无法声明 EF 关系）；`users` 表未就绪时按固定间隔重试。删除用户将**连带删除其旅行记录**（明确选择 CASCADE）。
- 写入不存在的 `user_id` 由数据库外键违例（PostgreSQL 23503）映射为 HTTP 400，不引入跨服务同步调用。

代价：删除用户会清空其旅行史；表结构变更需演进引导逻辑或将来切到 EF Migrations（仓库现状为无迁移文件、模型驱动）。
