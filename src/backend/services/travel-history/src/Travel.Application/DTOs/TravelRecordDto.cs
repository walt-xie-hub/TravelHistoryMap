namespace Travel.Application.DTOs;

/// <summary>
/// 对外暴露的旅行记录视图模型（不含领域内部细节）。
/// 时间以 UTC 时刻（timestamptz）存储，序列化为 ISO 8601 带偏移；departedAt 为空表示停留仍在进行中。
/// </summary>
public record TravelRecordDto(
    int Id,
    int UserId,
    string LocationName,
    decimal Latitude,
    decimal Longitude,
    DateTimeOffset ArrivedAt,
    DateTimeOffset? DepartedAt);

/// <summary>
/// 创建旅行记录请求模型。无鉴权现状下由客户端显式传入 userId（与 user-service 约定一致）。
/// </summary>
public record CreateTravelDto(
    int UserId,
    string LocationName,
    decimal Latitude,
    decimal Longitude,
    DateTimeOffset ArrivedAt,
    DateTimeOffset? DepartedAt = null);

/// <summary>
/// 更新旅行记录请求模型（不允许改归属用户）。
/// </summary>
public record UpdateTravelDto(
    string LocationName,
    decimal Latitude,
    decimal Longitude,
    DateTimeOffset ArrivedAt,
    DateTimeOffset? DepartedAt = null);
