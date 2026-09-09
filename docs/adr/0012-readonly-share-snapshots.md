# Read-only share snapshots for travel records

## Status

Accepted

## Context

“达人分享”需求：把精选足迹分享给他人（小红书/朋友圈/朋友），但系统是单用户私密模型（ADR-0005），记录与图片都按属主隔离、不可公开。需要一个“显式、只读、可控”的分享途径。

## Decision

- 新增**只读分享快照**：用户显式选择 1–100 条自己的 Travel record，创建时**复制分享行**（地点、到达/离开、已消毒正文），生成不可猜测的 48 位 hex Token，落库 `TravelShareSnapshots`（`RowsJson` 文本）。
- 只提供两个端点：
  - `POST /api/travels/share`（需登录）→ 返回 `{token, url}`；
  - `GET /api/share-snapshots/{token}`（**公开、无鉴权、只读**）→ 返回快照（标题/创建时间/行数/行列表）。
- **快照冻结**：后续编辑/删除原记录不影响已生成的快照（行在创建时复制）；分享**不含图片与原坐标细节**（避免为公开媒体新开鉴权面与放大暴露面）。
- 前端 `/s/:token` 为公开只读页（不受 authGuard 保护）；另有“足迹分享卡”下载/文案复制作为本地生成（不依赖后端）。
- 分享链接可复制、可删除对应快照为后续增强；本期不提供关注/社区/私信。

## Alternatives considered

- **直接公开记录/图片**：被否决——破坏 ADR-0005 隔离，且媒体公开需新的鉴权模型。
- **分享时引用原记录 id（不复制）**：被否决——会随原记录编辑/删除而变化，“快照”语义要求冻结。
- **带图片分享**：被否决——本期避免公开媒体面，卡片用本地 canvas 合成封面。

## Consequences

- 在保持“默认私密、仅本人可见”的前提下获得可控分享：快照内容由用户显式选定、Token 即权限（知者能看），不可猜测。
- 新表 + 幂等建表（DatabaseInitializer）；网关需放行 `/api/share-snapshots` 到 travel-service。
- 分享快照不含图片与坐标，信息面收敛；后续如需“分享整页含图”，再单独设计公开媒体 token。
