# Travel Map

记录用户到过哪里、何时到达与离开的应用：user-service 管理用户档案，travel-history 记录用户的旅行历史，前端将历史渲染到地图上。

## Language

**User**:
由 user-service 拥有并管理的用户档案（`users` 表）。其他服务只通过整数 `user_id` 引用它，不复制用户数据。
_Avoid_: member, account, owner

**Travel record**（旅行记录）:
一名用户在某地点的一段有明确到达与离开边界的一次停留。一条记录对应地图上的一个点；同一地点多次到访是独立的多条记录。由 travel-history 服务拥有（`TravelRecords` 表）。
_Avoid_: trip, itinerary, journey（这些暗示一次聚合了多个地点的行程）

**Arrival / Departure**（到达 / 离开）:
一段停留的时间边界。`arrivedAt` 必填；`departedAt` 可空——为空表示该停留仍在进行中（人还在当地），离开后再补录。两者都以 UTC 存储。
_Avoid_: start time / end time, check-in / check-out

**Location snapshot**（地点快照）:
内联在 Travel record 上的地名（`locationName`）与坐标（`latitude` / `longitude`）。是历史快照而非对某个可编辑地点的引用，因此地点改名或移动不影响旧记录。坐标以 **WGS-84**（十进制度）存储，前端渲染到高德地图时客户端转换为 GCJ-02；若换引擎只需切换转换函数，数据层不动（见 ADR-0003）。
_Avoid_: place 表, POI, 外键地点引用, PostGIS

**Referenced user**:
Travel record 的归属者，由 `user_id` 外键引用 `users.Id`。travel-history 与 user-service 在代码层零耦合（不引用 `User.Domain`），完整性由数据库级外键保证。
_Avoid_: 跨服务同步调用验证用户存在性
