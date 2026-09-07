# Travel details and image media

## Status

Accepted

## Context

地图上的 Travel record 需要支持文字描述和图片，但地点、坐标与到达/离开时间仍属于既有 `TravelRecords` 主记录。图片还需要缩略图展示和原图查看，并且不能绕过当前登录用户的记录归属校验。

## Decision

- 文字描述作为 `TravelRecords.Description` 可空字段保存。
- 图片元数据保存到 `TravelImages`，通过 `TravelRecordId` 外键关联并级联删除。
- 文件内容由可替换的媒体存储抽象保存；开发环境使用持久化目录，数据库只保存原图和 320px WebP 缩略图路径。
- 图片列表、缩略图和原图接口都先校验 `TravelRecord.UserId` 与 JWT 用户 ID 一致。
- 上传限制为每条记录最多 9 张，单张最多 10 MB，仅允许 JPEG、PNG、WebP。

## Consequences

旧 Travel record 可以没有描述和图片，详情页显示为空状态。生产环境应将 `ITravelImageStorage` 替换为对象存储实现，并通过部署配置注入存储地址和凭据；开发目录不能作为生产可靠存储。