# ADR-0015：城市快照（City snapshot）与同城连续停留（City run）

- 状态：已接受
- 日期：2026-09-09
- 决策人：agent + 用户（地图时光轴）

## 背景

地图上方要新增一条“时光轴”（Map timeline），把停留记录按时间先后折叠展示，并让**同一城市的连续停留**折叠为一个节点（GitHub 分支式可展开）。问题：现有 Travel record 只存“地点快照”（自由文本名 + WGS-84 坐标），**没有城市**；若按坐标聚簇归并（11m）无法把同一城市内不同地点（如 外滩/田子坊）合起来，时间轴会很长。服务端也无 AMap 密钥做逐条逆地理。

## 决策

1. **写入时落库 City snapshot**：`TravelRecords` 增加可空短文本列 `City`（如 `上海`/`中山`，≤40 字符）。创建/编辑记录时，客户端本就在调用 AMap（搜索 PlaceSearch / 点选逆地理 Geocoder），把城市抽成短名随请求写入；空值存 NULL。这是历史快照（呼应 Location snapshot），不引用任何可编辑地点表。
2. **口径**：取地级市短名（去“市/地区”等行政后缀）；PlaceSearch 用 `cityname ?? pname`，点选地图用 `addressComponent.city ?? province`；空值回退 NULL。旧记录无 City——在时间轴单独成点，用户编辑时自然补上。
3. **归并语义（City run）**：前端把按到达时间排序的记录切分——**相邻且 City 相同**的一段为一个 City run（停留段）；中途到过别处再回同一城市＝新 run。此归并是**展示层**概念，不落库。
4. 记录 DTO 增加 `city`；Create/Update 接受可选 `city`；全量 PUT（编辑保存/收藏切换）须回传 `city`（否则会被清空——见 ADR-0014 同款教训）。

### Schema 演进
幂等 DDL：`ALTER TABLE "TravelRecords" ADD COLUMN IF NOT EXISTS "City" character varying(40) NULL;`（见 DatabaseInitializer）。

## 影响
- 领域：`TravelRecord.City`（可空）；仓储 `UpdateAsync` 需拷贝新列（手抄清单再 +1）。
- API：DTO/请求带 `city`；无新端点。
- 前端：地图页在 `.map-layout` 上方插入 Map timeline；节点 hover 出三动作气泡（详情/展开/删除）；节点上方显示城市名、下方显示该段首图缩略图（若有图）。
- 展示层 City run 分组逻辑与现有“坐标聚簇合并地图标记（ADR-0004）”是两个不同概念，勿混用。

## 备选
- 运行期逐条逆地理（不改库）：N 次外部调用、易失败，否决。
- 按坐标聚簇代替城市归并：同一城市不同 POI 不合，否决。
- 区县级粒度：过碎，否决。
