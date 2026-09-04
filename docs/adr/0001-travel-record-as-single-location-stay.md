# Travel record modeled as a single-location stay

travel-history 的每条记录被定义为**一名用户在某地点的一段停留**（到达 + 离开），而不是一次聚合多个地点的"行程"（Trip）。前端地图标注"位置+时间"的需求用多个独立记录即可满足：每个记录 = 一个地图点。

地点以**快照**形式内联在记录上（`locationName` + `latitude` + `longitude`，全必填），不建独立地点表、不用 PostGIS。时间是 `arrivedAt`（必填）与 `departedAt`（可空，空 = 进行中的停留，离开后补录），以 UTC 存储；同地点多次到访产生多条记录，不做时间重叠校验。

若未来需要"一次出行多城"的行程视图，可在现有记录之上加可选的分组列（如 `TripId`）演进，而不必现在就把 schema 复杂化。
