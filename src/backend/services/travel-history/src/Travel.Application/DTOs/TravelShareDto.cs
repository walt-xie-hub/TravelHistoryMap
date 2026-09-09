namespace Travel.Application.DTOs;

/// <summary>创建只读分享的请求：选择要分享的 Travel record id 集合。</summary>
public sealed record ShareCreateRequest(IReadOnlyList<int> TravelIds);

/// <summary>分享快照中的单行（地点/时间/正文；不含坐标细节与图片，正文为已消毒 HTML）。</summary>
public sealed record ShareSnapshotRow(
    string LocationName,
    DateTimeOffset ArrivedAt,
    DateTimeOffset? DepartedAt,
    string? Description);

/// <summary>公开读取的快照 DTO（只读，无归属字段）。</summary>
public sealed record PublicShareSnapshotDto(
    string Title,
    DateTimeOffset CreatedAt,
    int RecordCount,
    IReadOnlyList<ShareSnapshotRow> Rows);
