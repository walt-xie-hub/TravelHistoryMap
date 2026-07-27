using Microsoft.EntityFrameworkCore;
using User.Domain.Abstractions;
using User.Domain.Common;
using User.Domain.Entities;
using User.Infrastructure.Persistence;

namespace User.Infrastructure.Repositories;

/// <summary>
/// IUserRepository 的 EF Core 实现（基础设施层）。
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db) => _db = db;

    public async Task<IEnumerable<AppUser>> GetAllAsync(CancellationToken ct = default)
        => await _db.Users.AsNoTracking().ToListAsync(ct);

    public async Task<PagedResult<AppUser>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Users.AsNoTracking().OrderBy(u => u.Id);
        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResult<AppUser>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<AppUser> AddAsync(AppUser user, CancellationToken ct = default)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<AppUser?> UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        // FindAsync 返回被跟踪的实体，SaveChanges 时 EF 会在 WHERE 中带上 xmin（乐观锁）
        var existing = await _db.Users.FindAsync(new object[] { user.Id }, ct);
        if (existing is null)
            return null;

        existing.Name = user.Name;
        existing.Email = user.Email;
        existing.PhoneNumber = user.PhoneNumber;
        existing.PasswordHash = user.PasswordHash;
        existing.AvatarUrl = user.AvatarUrl;
        existing.IsActive = user.IsActive;
        existing.UpdatedAt = user.UpdatedAt;

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var existing = await _db.Users.FindAsync(new object[] { id }, ct);
        if (existing is null)
        {
            return false;
        }

        _db.Users.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
