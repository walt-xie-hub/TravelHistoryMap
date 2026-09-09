using System.Security.Cryptography;
using System.Text.Json;
using Travel.Application.Abstractions;
using Travel.Application.DTOs;
using Travel.Application.Sanitization;
using Travel.Domain.Abstractions;
using Travel.Domain.Common;
using Travel.Domain.Entities;

namespace Travel.Application.Services;

/// <summary>
/// 旅行记录应用服务：编排领域对象与仓储，完成具体用例，并负责领域&lt;-&gt;DTO 映射。
/// 只依赖领域抽象，不感知数据库实现。
/// ADR-0005：userId 由表现层从认证身份解析后传入；单条读取/更新/删除都校验记录归属，
/// 非本人记录按“不存在”处理（不泄露存在性）。
/// </summary>
public class TravelService : ITravelService
{
    private readonly ITravelRepository _repository;

    public TravelService(ITravelRepository repository) => _repository = repository;

    public async Task<PagedResult<TravelRecordDto>> GetPagedAsync(
        int userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var result = await _repository.GetPagedAsync(userId, from, to, page, pageSize, ct);
        var items = result.Items.Select(ToDto).ToList();
        return new PagedResult<TravelRecordDto>(items, result.Page, result.PageSize, result.TotalCount, result.TotalPages);
    }

    public async Task<TravelRecordDto?> GetByIdAsync(int userId, int id, CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        // 已移入回收站视同不存在（ADR-0013），普通详情/更新均不可见
        return record is null || record.UserId != userId || record.DeletedAt is not null ? null : ToDto(record);
    }

    public async Task<TravelRecordDto> CreateAsync(int userId, CreateTravelDto dto, CancellationToken ct = default)
    {
        var record = new TravelRecord
        {
            UserId = userId,   // 归属者只来自认证身份，忽略客户端声明
            LocationName = dto.LocationName.Trim(),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            ArrivedAt = dto.ArrivedAt.ToUniversalTime(),
            DepartedAt = dto.DepartedAt?.ToUniversalTime(),
            Description = PrepareDescription(dto.Description),
            TagsJson = NormalizeTags(dto.Tags),
            IsFavorite = dto.IsFavorite,
        };
        var created = await _repository.AddAsync(record, ct);
        return ToDto(created);
    }

    public async Task<TravelRecordDto?> UpdateAsync(int userId, int id, UpdateTravelDto dto, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing is null || existing.UserId != userId || existing.DeletedAt is not null)
            return null;

        existing.LocationName = dto.LocationName.Trim();
        existing.Latitude = dto.Latitude;
        existing.Longitude = dto.Longitude;
        existing.ArrivedAt = dto.ArrivedAt.ToUniversalTime();
        existing.DepartedAt = dto.DepartedAt?.ToUniversalTime();
        existing.Description = PrepareDescription(dto.Description);
        existing.TagsJson = NormalizeTags(dto.Tags);
        existing.IsFavorite = dto.IsFavorite;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await _repository.UpdateAsync(existing, ct);
        return updated is null ? null : ToDto(updated);
    }

    /// <summary>移入回收站（软删除，ADR-0013）：仅置 DeletedAt，不清理图片。</summary>
    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing is null || existing.UserId != userId || existing.DeletedAt is not null)
            return false;
        return await _repository.SetDeletedAtAsync(id, DateTimeOffset.UtcNow, ct);
    }

    public async Task<PagedResult<TravelRecordDto>> GetTrashAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var result = await _repository.GetTrashPagedAsync(userId, page, pageSize, ct);
        var items = result.Items.Select(ToDto).ToList();
        return new PagedResult<TravelRecordDto>(items, result.Page, result.PageSize, result.TotalCount, result.TotalPages);
    }

    /// <summary>从回收站恢复：清空 DeletedAt。</summary>
    public async Task<bool> RestoreAsync(int userId, int id, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing is null || existing.UserId != userId || existing.DeletedAt is null)
            return false;
        return await _repository.SetDeletedAtAsync(id, null, ct);
    }

    /// <summary>彻底删除：物理删除记录行（图片媒体与图片行由表现层先行清理）。</summary>
    public async Task<bool> DeletePermanentlyAsync(int userId, int id, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing is null || existing.UserId != userId)
            return false;
        return await _repository.DeleteAsync(id, ct);
    }

    public async Task<string?> CreateShareAsync(int userId, IReadOnlyList<int> travelIds, CancellationToken ct = default)
    {
        var ids = travelIds.Distinct().ToList();
        if (ids.Count == 0 || ids.Count > 100)
            throw new ArgumentException("请选择 1–100 条记录用于分享。");

        var records = (await _repository.GetByIdsAsync(userId, ids, ct))
            .OrderBy(r => r.ArrivedAt)
            .ToList();
        if (records.Count == 0)
            return null;

        var rows = records
            .Select(r => new ShareSnapshotRow(r.LocationName, r.ArrivedAt, r.DepartedAt, r.Description))
            .ToList();

        var saved = await _repository.AddShareAsync(new TravelShareSnapshot
        {
            Token = GenerateShareToken(),
            UserId = userId,
            Title = rows.Count == 1
                ? rows[0].LocationName
                : $"{rows[0].LocationName} 等 {rows.Count} 站",
            RowsJson = JsonSerializer.Serialize(rows),
            RecordCount = rows.Count,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        return saved.Token;
    }

    public async Task<PublicShareSnapshotDto?> GetShareSnapshotAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var share = await _repository.GetShareByTokenAsync(token, ct);
        if (share is null)
            return null;

        var rows = JsonSerializer.Deserialize<List<ShareSnapshotRow>>(share.RowsJson) ?? [];
        return new PublicShareSnapshotDto(share.Title, share.CreatedAt, share.RecordCount, rows);
    }

    private static string GenerateShareToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    private static TravelRecordDto ToDto(TravelRecord record) => new(
        record.Id,
        record.UserId,
        record.LocationName,
        record.Latitude,
        record.Longitude,
        record.ArrivedAt,
        record.DepartedAt,
        record.Description,
        ParseTags(record.TagsJson),
        record.IsFavorite);

    private const int MaxTags = 8;
    private const int MaxTagLength = 20;

    /// <summary>
    /// 标签归一化（ADR-0014）：trim、忽略空、忽略大小写去重、限量 8 个、单个 ≤20 字符；
    /// 超限抛 ArgumentException（表现层转 400）。序列化为 JSON 数组字符串落库。
    /// </summary>
    private static string NormalizeTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
            return "[]";
        if (tags.Count > MaxTags)
            throw new ArgumentException($"每个旅行记录最多 {MaxTags} 个标签。");

        var cleaned = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            var tag = raw?.Trim();
            if (string.IsNullOrEmpty(tag))
                continue;
            if (tag.Length > MaxTagLength)
                throw new ArgumentException($"单个标签长度不能超过 {MaxTagLength} 个字符。");
            if (seen.Add(tag))
                cleaned.Add(tag);
        }
        return JsonSerializer.Serialize(cleaned);
    }

    private static IReadOnlyList<string> ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 正文入库口径（ADR-0009）：空→null；否则消毒成 allow-list HTML；
    /// 可见字符（去标签、解码、空白折叠）超过 4000 视为非法，抛 ArgumentException（表现层转 400）。
    /// </summary>
    private static string? PrepareDescription(string? description)
    {
        var html = RichTextSanitizer.Prepare(description);
        if (html is null)
            return null;
        if (RichTextSanitizer.VisibleCharacterCount(html) > RichTextSanitizer.MaxVisibleCharacters)
            throw new ArgumentException("Travel detail must not exceed 4000 visible characters.");
        return html;
    }
}
