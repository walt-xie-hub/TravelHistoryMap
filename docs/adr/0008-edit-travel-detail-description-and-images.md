# Edit travel detail: description text and image add/remove

## Status

Accepted

## Context

Travel record 详情页此前只读（ADR-0006 只定义了描述与图片的存储与读取）。产品要求详情页可编辑“文本”与增删图片。细节与两处既有语义相关：

- Travel record 是地图上的一个点（ADR-0001），Location snapshot（地名/坐标）与 Arrival/Departure 边界决定其地图足迹。
- 归属校验是全量统一模式：任何单条操作都先校验 `TravelRecord.UserId`，失败一律按 404 处理（ADR-0005）。
- 后端已有全量替换的 `PUT /api/travels/{id}`（`UpdateTravelDto`），但领域层 `DeleteImageAsync` 是死代码，没有单图删除的 HTTP 端点。

## Decision

- **编辑范围限定在详情页的 Travel detail（描述）与图片增删**；Location snapshot（地名/坐标）与 Arrival/Departure 在详情页保持只读，不在本次改动暴露编辑入口。描述保存复用既有 `PUT /api/travels/{id}`，客户端回传记录全部字段（其余字段沿用加载到的只读值）。
- **新增单图删除端点** `DELETE /api/travels/{id}/images/{imageId}`：删除数据库行并清理原图/缩略图媒体文件；记录非本人所有、记录/图片不存在、或图片不属于该记录一律 404。媒体文件清理失败（IOException）不阻断数据库删除——数据库行是权威。
- **删除是永久性的，无回收站**：单图删除需要用户显式确认；不引入软删除/审计表。
- 详情页采用**就地编辑**：查看态下“编辑详情”切换为编辑态；描述用 textarea，图片网格逐张提供 ✕ 删除（仅编辑态出现，防止误删），并支持多选待上传图片（容量 = 9 − 现有 − 待上传）。
- **保存时序**：先 `PUT` 更新描述（全量替换、成功后才继续），再逐张 `POST /{id}/images` 上传新增图片；单张失败保留在待上传清单并点名报错，可重试/移除，不整体回滚。
- **网关放开 body 上限**：`client_max_body_size 12m`（nginx 默认 1 MB 会拒绝 >1 MB 图片，需高于 travel 服务 10 MB 单张上限）。

## Alternatives considered

- **允许编辑 Location snapshot / 时间边界**：被否决——会改写记录在地图上的身份与足迹，破坏 ADR-0001 的“一地点一停留、多次到访为多条独立记录”语义；如后续需要应作为独立特性（地名纠错等）。
- **独立编辑页 `/travels/:id/edit`**：被否决——只编辑描述与图片无需高德选点，就地编辑保留图片上下文、更少路由。
- **图片软删除/回收站**：被否决——项目无软删除约定，回收站是显著新增复杂度；以显式确认 + 不可恢复替代。
- **PATCH 局部更新**：维持既有 `PUT` 全量替换，避免新增 DTO/端点面；客户端负责回传完整记录。

## Consequences

- 详情页编辑不触碰地图足迹：地名、坐标、到达/离开时间在任何编辑操作后保持不变。
- 单图删除成为新的公共 API 面，需纳入既有归属校验模式与测试；`DeleteImageAsync` 死代码被真正使用。
- 图片删除先于“保存文本”生效（逐张确认即提交），编辑态“取消”只回退描述与待上传清单，不回滚已确认的删图——交互文案已明确标注“不可恢复”。
- 生产网关需随本改动重新部署以放开 body 上限，否则 >1 MB 图片上传 413。
