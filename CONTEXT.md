# Travel Map

记录用户到过哪里、何时到达与离开的应用：user-service 管理用户档案，travel-history 记录用户的旅行历史，前端将历史渲染到地图上。

## Language

**User**:
由 user-service 拥有并管理的用户档案（`users` 表）。用户持有**凭据**（email + password）用于登录；注册是创建用户的唯一途径（公开的 users CRUD 已下线）。其他服务只通过整数 `user_id` 引用它，不复制用户数据。
_Avoid_: member, account, owner

**Signed-in user**（登录用户）:
已通过 user-service 登录流程认证、获准进入主界面的用户。主界面数据（如 Travel records、地图足迹）只呈现给其归属的登录用户自己。
_Avoid_: guest, anonymous, current account

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

**Travel detail**:
Travel record 的可选正文，直接属于该次停留；没有正文不影响 Travel record 的存在。由该记录的登录用户所有，可随时编辑或清空。正文是带排版的富文本内容（加粗/斜体/下划线/删除线/标题/列表/引用/链接，以及字体、字号、颜色、背景高亮、对齐等行内排版）；**不内嵌图片**——图片独立管理为该记录的 Travel image。
_Avoid_: 独立行程、游记聚合、把图片塞进正文

**Travel image**:
与 Travel record 关联的图片资源。数据库保存文件元数据和原图/缩略图路径，文件内容保存在可配置的持久化媒体存储中；图片访问必须继承所属 Travel record 的用户权限。由该记录的登录用户所有，可在详情页逐张增删（删除为永久移除，不可恢复）。
_Avoid_: 将图片二进制塞入 TravelRecords、公开图片 URL

**Client application**:
访问 Travel Map 能力的客户端，包括浏览器和未来的手机端应用。客户端通过同一组面向资源的公共 API 操作登录用户自己的数据，不依赖具体后端服务地址或存储实现。
_Avoid_: 将 Web 页面视为唯一客户端、把微服务内部地址暴露给客户端

**City snapshot**（城市快照）:
内联在 Travel record 上的一段短期行政城市标签（如 上海、中山），创建/编辑该记录时由客户端经 AMap 派生并落库；是历史快照而非对可编辑地点的引用。用于“同城连续停留”的归并与展示。
_Avoid_: 实时逆地理引用、把城市当作外键地点表、区县级粒度

**Map timeline**（地图时光轴）:
地图页上、地图区域上方的一条横向时间视图，把登录用户的 Travel records 按时间先后折叠展示（左早右晚）；同一 City snapshot 的连续多段折叠为一个节点，单段则为一个普通节点。
_Avoid_: 纵向列表替代地图、独立的第二条记录来源

**City run**（停留段 / 同城连续停留）:
到达时间排序下**相邻且 City snapshot 相同**的一段 Travel records；中途到过别处再回到同一城市＝新的 City run。在 Map timeline 上以一个节点呈现，展开可查看该段的全部停留（GitHub 分支式结构）。
_Avoid_: 把不同城市的记录误并为一段、按坐标聚簇代替城市归并（坐标聚簇仅用于地图标记，见 ADR-0004）

**精选收藏（Favorite）**:
Travel record 上的是/否标记，表示“值得重点回味的足迹”。用户可在创建/详情切换；地图页可开启“★ 收藏”只看这些记录，列表/地图/时光轴上以 ★ 呈现。
_Avoid_: 收藏夹、合集、相册（把多条记录组成可分享的集合是另一个概念，尚未实现）

**Travel icon（旅行标识图标）**:
一条 Travel record 上内联的图标选择（Icon library 中的 key，如 `animal-panda`），用于在详情页、列表行、时光轴节点标识这次旅行。为空表示未显式选择——渲染期按 City snapshot 派生地区特色图标，该派生不落库（见 ADR-0016）。地图标记仅在**同坐标组解析结果一致**时才改用该图标，否则仍是默认标记加计数。
_Avoid_: 把图标当地图 marker（marker 是标注）、外键引用可编辑图标表

**Icon library（图标库）**:
前端只读的图标目录（剪影造型，分类 动物/美食/建筑/植物），key → 造型的映射由前端维护；不是用户数据，不提供增删改。
_Avoid_: 用户上传的图标库、把图标库当业务实体表

**Regional icon（地区特色图标）**:
按 Travel record 的 City snapshot 自动匹配的地区特色图标（MVP 为热门城市市花/代表动物等精选映射）。优先级：显式 Travel icon > Regional icon > 无（默认标记）。
_Avoid_: 实时逆地理查询、把地区映射当权威市花数据库
