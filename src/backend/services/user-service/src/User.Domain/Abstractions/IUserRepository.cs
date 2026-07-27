using User.Domain.Common;
using User.Domain.Entities;

namespace User.Domain.Abstractions;

/// <summary>
/// 用户仓储接口。定义在领域层，由基础设施层实现（依赖倒置）。
/// </summary>
public interface IUserRepository
{
    Task<IEnumerable<AppUser>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询用户。page 从 1 开始；返回本页数据、总条数与总页数。
    /// </summary>
    Task<PagedResult<AppUser>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<AppUser?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<AppUser> AddAsync(AppUser user, CancellationToken cancellationToken = default);

    /// <summary>更新已有用户（EF 会基于 RowVersion/xmin 做乐观锁校验）。</summary>
    Task<AppUser?> UpdateAsync(AppUser user, CancellationToken cancellationToken = default);

    /// <summary>按主键删除用户；不存在时返回 false。</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
