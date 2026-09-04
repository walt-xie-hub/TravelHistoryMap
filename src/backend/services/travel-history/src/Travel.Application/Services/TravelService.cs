using Travel.Application.Abstractions;
using Travel.Application.DTOs;
using Travel.Domain.Abstractions;
using Travel.Domain.Common;
using Travel.Domain.Entities;

namespace Travel.Application.Services;

/// <summary>
/// 旅行记录应用服务：编排领域对象与仓储，完成具体用例，并负责领域&lt;-&gt;DTO 映射。
/// 只依赖领域抽象，不感知数据库实现。
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

    public async Task<TravelRecordDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        return record is null ? null : ToDto(record);
    }

    public async Task<TravelRecordDto> CreateAsync(CreateTravelDto dto, CancellationToken ct = default)
    {
        var record = new TravelRecord
        {
            UserId = dto.UserId,
            LocationName = dto.LocationName.Trim(),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            ArrivedAt = dto.ArrivedAt.ToUniversalTime(),
            DepartedAt = dto.DepartedAt?.ToUniversalTime(),
        };
        var created = await _repository.AddAsync(record, ct);
        return ToDto(created);
    }

    public async Task<TravelRecordDto?> UpdateAsync(int id, UpdateTravelDto dto, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing is null)
            return null;

        existing.LocationName = dto.LocationName.Trim();
        existing.Latitude = dto.Latitude;
        existing.Longitude = dto.Longitude;
        existing.ArrivedAt = dto.ArrivedAt.ToUniversalTime();
        existing.DepartedAt = dto.DepartedAt?.ToUniversalTime();
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await _repository.UpdateAsync(existing, ct);
        return updated is null ? null : ToDto(updated);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        => await _repository.DeleteAsync(id, ct);

    private static TravelRecordDto ToDto(TravelRecord record) => new(
        record.Id,
        record.UserId,
        record.LocationName,
        record.Latitude,
        record.Longitude,
        record.ArrivedAt,
        record.DepartedAt);
}
