# ADR-0013：回收站采用软删除（DeletedAt 时间戳）

- 状态：已接受
- 日期：2026-09-09
- 决策人：agent（达人路线图 M4「灯箱滑动 + 回收站」）

## 背景

地图页此前「删除停留记录」会立即物理删除整条记录（连同图片行与媒体文件），误删不可恢复。
达人路线图希望提供**回收站**：误删可恢复，恢复无痕；仅在回收站中「彻底删除」才真正释放。

## 决策

`TravelRecords` 增加可空列 `DeletedAt`（timestamptz，null = 未删除）：

1. **删除 = 移入回收站（软删除）**：`DELETE /api/travels/{id}` 只写 `DeletedAt = now()`，
   不清理图片行/媒体；默认列表、详情、更新、分享创建均**排除已软删除**的记录
   （分享创建在仓储 `GetByIdsAsync` 层过滤，回收站记录不可再被分享）。
2. **恢复**：`POST /api/travels/{id}/restore` 清空 `DeletedAt`；图片仍原样存在，恢复无痕。
3. **彻底删除**：`DELETE /api/travels/{id}/permanent` 先由 `TravelImageService`
   清理媒体文件（图片行靠 FK ON DELETE CASCADE 随记录删除），再物理删行。
4. **回收站列表**：`GET /api/travels/trash` 只返回本人 `DeletedAt` 非空的记录，按删除时间倒序。
5. 越权与存在性不泄露语义沿用 ADR-0005：非本人记录一律按“不存在 / false”处理。

### Schema 演进

沿用共享库的「模型驱动 + 幂等 DDL」约定（见 ADR-0001/0002）：
`DatabaseInitializer` 中 `ALTER TABLE "TravelRecords" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;`
对旧库幂等补列；新库由 EF `EnsureCreated`/`CreateTables` 直接建含该列的模型。
`TravelRecordConfiguration` 将该属性映射为可空 `timestamptz`。

## 影响

- 领域：`TravelRecord.DeletedAt`；仓储默认查询带 `DeletedAt == null` 过滤。
- API：既有 `DELETE /{id}` 语义从“物理删除”改为“移入回收站”（前端确认文案随之调整）；
  新增 trash / restore / permanent 端点。图片端点对回收站记录仍可读（由详情不可达兜底，
  仅当已知 id 直连才可能访问，风险可接受）。
- 前端：地图页删除改提示“移入回收站”；新增回收站页支持恢复与彻底删除。
- 存储：新增一列，不再占用额外容量；彻底删除前媒体文件仍在磁盘。

## 备选方案

- 物理删除 + 前端无回收站：无法满足“误删可恢复”，否决。
- 独立 trash 表 / is_deleted 布尔：时间戳可表达“何时删除、便于回收站排序”，且无需迁移行，采用时间戳。
