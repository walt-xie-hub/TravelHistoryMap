# ADR-0014：旅游记录标签（Tags）与精选收藏（IsFavorite）

- 状态：已接受
- 日期：2026-09-09
- 决策人：agent（达人路线图 M3「照片墙 + 标签/收藏」的第二半）

## 背景

用户希望给自己的足迹打上“是什么性质/和谁/主题”的标签（如 亲子游、独自旅行、徒步、美食），
并在大量记录中快速标记出“最想回味 / 重点推荐”的少数记录。

## 决策

在 `TravelRecords` 上直接加两个标量字段，不做多表关联（单用户、低基数，见 ADR-0005/0002 约束）：

1. **`TagsJson`（text，默认 `"[]"`）**：一条记录的自由标签，以 JSON 数组字符串存储。
   - 归一化在应用层完成：逐条 `Trim`、忽略空、**忽略大小写去重**、最多 **8 个**、单个 **≤20 字符**；
     超限抛 `ArgumentException`（表现层转 400）。
   - 用 JSON 列而非关联表，避免 Tags/TravelRecordTags 两张表与多对多映射的复杂度；
     单用户场景下无需按标签做关系查询（当前先支持展示与随记录读写）。
2. **`IsFavorite`（boolean，默认 false）**：精选收藏标记，供详情/列表用 ★ 快速切换。

DTO 与读写口径：`TravelRecordDto` 增加 `Tags`（string[]）与 `IsFavorite`；
`CreateTravelDto`/`UpdateTravelDto` 接受二者；标签入库前一律过 `NormalizeTags`。
既有只读分享快照不含标签/收藏（分享行保持精简，见 ADR-0012）。

### Schema 演进
沿用幂等 DDL 约定：`ALTER TABLE ... ADD COLUMN IF NOT EXISTS "TagsJson" text NOT NULL DEFAULT '[]';`
与 `... ADD COLUMN IF NOT EXISTS "IsFavorite" boolean NOT NULL DEFAULT false;`；新库由 EF 直接建含该列的模型。

## 影响
- 领域：`TravelRecord.TagsJson` / `IsFavorite`；创建、全量更新时随记录读写。
- API：Create/Update 请求与记录 DTO 携带 Tags/IsFavorite；无新增端点。
- 前端：创建页支持输入标签（chips）与“标为精选收藏”；详情页展示标签并可用 ★ 快捷切换收藏。
- 未来（非本次范围）：命名“收藏夹/合集”（多条记录组成、可分享成册）需独立集合表与 ADR；若需按标签搜索/筛选再做标签规范化表与索引。

## 备选方案
- 规范化的 Tag + 关联表（可查询/聚合）：单用户低基数下收益有限而复杂度高，否决。
- 纯前端标签（不落库）：换设备即失，否决。
