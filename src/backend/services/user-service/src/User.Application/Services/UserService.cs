using User.Application.Abstractions;
using User.Application.DTOs;
using User.Domain.Abstractions;
using User.Domain.Common;
using User.Domain.Entities;

namespace User.Application.Services;

/// <summary>
/// 用户应用服务：编排领域对象与仓储，完成具体用例，并负责领域<->DTO 映射。
/// 只依赖领域抽象，不感知数据库实现。
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository _repository;

    public UserService(IUserRepository repository) => _repository = repository;

    public async Task<PagedResult<UserDto>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var result = await _repository.GetPagedAsync(page, pageSize, ct);
        var items = result.Items
            .Select(u => new UserDto(u.Id, u.Name, u.Email))
            .ToList();
        return new PagedResult<UserDto>(items, result.Page, result.PageSize, result.TotalCount, result.TotalPages);
    }

    public async Task<UserDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var user = await _repository.GetByIdAsync(id, ct);
        return user is null ? null : new UserDto(user.Id, user.Name, user.Email);
    }

    public async Task<UserDto> CreateAsync(CreateUserDto dto, CancellationToken ct = default)
    {
        var user = new AppUser { Name = dto.Name, Email = dto.Email };
        var created = await _repository.AddAsync(user, ct);
        return new UserDto(created.Id, created.Name, created.Email);
    }

    public async Task<UserDto?> UpdateAsync(int id, UpdateUserDto dto, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing is null)
            return null;

        existing.Name = dto.Name;
        existing.Email = dto.Email;
        existing.PhoneNumber = dto.PhoneNumber;
        existing.AvatarUrl = dto.AvatarUrl;
        existing.IsActive = dto.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        var updated = await _repository.UpdateAsync(existing, ct);
        return updated is null ? null : new UserDto(updated.Id, updated.Name, updated.Email);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        return await _repository.DeleteAsync(id, ct);
    }
}
