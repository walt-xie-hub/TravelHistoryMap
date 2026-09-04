using User.Domain.Entities;

namespace User.Domain.Abstractions;

/// <summary>
/// 用户仓储抽象（领域层定义）。公开 users CRUD 下线后，仅保留认证与 me 资源所需的读取/写入。
/// </summary>
public interface IUserRepository
{
    /// <summary>按 id 读取（跟踪外）。</summary>
    Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>按规范化邮箱读取（跟踪外）。</summary>
    Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>判断除指定用户外邮箱是否已被占用（改资料时冲突预检）。</summary>
    Task<bool> AnyByEmailAsync(string email, int excludeUserId, CancellationToken ct = default);

    /// <summary>新增用户。</summary>
    Task<AppUser> AddAsync(AppUser user, CancellationToken ct = default);

    /// <summary>保存字段修改；用户不存在返回 null。</summary>
    Task<AppUser?> UpdateAsync(AppUser user, CancellationToken ct = default);
}
