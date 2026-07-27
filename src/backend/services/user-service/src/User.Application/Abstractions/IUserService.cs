using User.Application.DTOs;
using User.Domain.Common;

namespace User.Application.Abstractions;

/// <summary>
/// 用户用例（应用服务）接口。表现层只依赖此抽象。
/// </summary>
public interface IUserService
{
    Task<PagedResult<UserDto>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<UserDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<UserDto> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default);

    Task<UserDto?> UpdateAsync(int id, UpdateUserDto dto, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
