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
    DateTimeOffset? DepartedAt,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    bool IsFavorite = false,
    IReadOnlyList<TravelImageDto>? Images = null);

public record TravelImageDto(
    int Id,
    string OriginalFileName,
    string ContentType,
    long FileSize,
    string ThumbnailUrl,
    string OriginalUrl);

/// <summary>
/// 创建旅行记录请求模型（ADR-0005）。归属用户不再由客户端传入：
/// 一律取自 JWT（NameIdentifier），见 TravelEndpoints / ITravelService.CreateAsync。
/// </summary>
public record CreateTravelDto(
    string LocationName,
    decimal Latitude,
    decimal Longitude,
    DateTimeOffset ArrivedAt,
    DateTimeOffset? DepartedAt = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    bool IsFavorite = false);

/// <summary>
/// 更新旅行记录请求模型（不允许改归属用户）。
/// </summary>
public record UpdateTravelDto(
    string LocationName,
    decimal Latitude,
    decimal Longitude,
    DateTimeOffset ArrivedAt,
    DateTimeOffset? DepartedAt = null,
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    bool IsFavorite = false);
