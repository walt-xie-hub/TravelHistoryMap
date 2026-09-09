using Microsoft.EntityFrameworkCore;
using Npgsql;
using Travel.Domain.Abstractions;
using Travel.Domain.Common;
using Travel.Domain.Entities;
using Travel.Infrastructure.Persistence;

namespace Travel.Infrastructure.Repositories;

/// <summary>
/// ITravelRepository 的 EF Core 实现（基础设施层）。
/// </summary>
public class TravelRepository : ITravelRepository
{
    private readonly TravelDbContext _db;

    public TravelRepository(TravelDbContext db) => _db = db;

    public async Task<PagedResult<TravelRecord>> GetPagedAsync(
        int userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        // 主查询：按用户 + 仅活动记录（未移入回收站）+ 可选到达时间窗过滤，按到达时间倒序（最新在前）
        var query = _db.TravelRecords.AsNoTracking()
            .Where(t => t.UserId == userId && t.DeletedAt == null);
        if (from.HasValue)
            query = query.Where(t => t.ArrivedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.ArrivedAt <= to.Value);

        query = query.OrderByDescending(t => t.ArrivedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResult<TravelRecord>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<PagedResult<TravelRecord>> GetTrashPagedAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        // 回收站：仅已软删除记录，按删除时间倒序（最近删除在前）
        var query = _db.TravelRecords.AsNoTracking()
            .Where(t => t.UserId == userId && t.DeletedAt != null)
            .OrderByDescending(t => t.DeletedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResult<TravelRecord>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<TravelRecord?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.TravelRecords.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<TravelRecord> AddAsync(TravelRecord record, CancellationToken ct = default)
    {
        try
        {
            _db.TravelRecords.Add(record);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            // 跨服务 FK（user_id → users.Id）违例：该用户不存在。转为领域异常由表现层返回 400。
            throw new UnknownUserException(record.UserId);
        }
        return record;
    }

    public async Task<TravelRecord?> UpdateAsync(TravelRecord record, CancellationToken ct = default)
    {
        // FindAsync 返回被跟踪的实体，SaveChanges 时 EF 会在 WHERE 中带上 xmin（乐观锁）
        var existing = await _db.TravelRecords.FindAsync(new object[] { record.Id }, ct);
        if (existing is null)
            return null;

        existing.UserId = record.UserId;
        existing.LocationName = record.LocationName;
        existing.Latitude = record.Latitude;
        existing.Longitude = record.Longitude;
        existing.ArrivedAt = record.ArrivedAt;
        existing.DepartedAt = record.DepartedAt;
        existing.Description = record.Description;
        existing.UpdatedAt = record.UpdatedAt;

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var existing = await _db.TravelRecords.FindAsync(new object[] { id }, ct);
        if (existing is null)
        {
            return false;
        }

        _db.TravelRecords.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetDeletedAtAsync(int id, DateTimeOffset? deletedAt, CancellationToken ct = default)
    {
        var existing = await _db.TravelRecords.FindAsync([id], ct);
        if (existing is null)
            return false;

        existing.DeletedAt = deletedAt;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static bool IsForeignKeyViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: "23503" };

    public async Task<IReadOnlyList<TravelImage>> GetImagesAsync(int recordId, CancellationToken ct = default)
        => await _db.TravelImages.AsNoTracking().Where(image => image.TravelRecordId == recordId)
            .OrderBy(image => image.Id).ToListAsync(ct);

    public Task<TravelImage?> GetImageAsync(int imageId, CancellationToken ct = default)
        => _db.TravelImages.AsNoTracking().FirstOrDefaultAsync(image => image.Id == imageId, ct);

    public async Task<TravelImage> AddImageAsync(TravelImage image, CancellationToken ct = default)
    {
        _db.TravelImages.Add(image);
        await _db.SaveChangesAsync(ct);
        return image;
    }

    public async Task<bool> DeleteImageAsync(int imageId, CancellationToken ct = default)
    {
        var image = await _db.TravelImages.FindAsync([imageId], ct);
        if (image is null) return false;
        _db.TravelImages.Remove(image);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<TravelRecord>> GetByIdsAsync(int userId, IReadOnlyList<int> ids, CancellationToken ct = default)
        => await _db.TravelRecords.AsNoTracking()
            .Where(t => t.UserId == userId && ids.Contains(t.Id) && t.DeletedAt == null)
            .ToListAsync(ct);

    public Task<TravelShareSnapshot?> GetShareByTokenAsync(string token, CancellationToken ct = default)
        => _db.TravelShareSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Token == token, ct);

    public async Task<TravelShareSnapshot> AddShareAsync(TravelShareSnapshot share, CancellationToken ct = default)
    {
        _db.TravelShareSnapshots.Add(share);
        await _db.SaveChangesAsync(ct);
        return share;
    }
}
