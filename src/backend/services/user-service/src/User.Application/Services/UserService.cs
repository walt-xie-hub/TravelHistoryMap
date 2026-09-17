using User.Application.Abstractions;
using User.Application.DTOs;
using User.Domain.Abstractions;
using User.Domain.Common;
using User.Domain.Entities;

namespace User.Application.Services;

/// <summary>
/// 用户应用服务：编排领域对象、密码哈希与仓储，完成注册/登录与本人档案管理用例，并负责领域&lt;-&gt;DTO 映射。
/// 只依赖领域/应用抽象，不感知数据库实现。邮箱统一小写规范化存储（依赖数据库 Email 唯一索引兜底）。
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly IPasswordHasher _passwordHasher;

    public UserService(IUserRepository repository, IPasswordHasher passwordHasher)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
    }

    public async Task<UserDto> RegisterAsync(string name, string email, string password, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (await _repository.GetByEmailAsync(normalizedEmail, ct) is not null)
            throw new EmailAlreadyExistsException(normalizedEmail);

        var created = await _repository.AddAsync(new AppUser
        {
            Name = name.Trim(),
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.Hash(password),
        }, ct);
        return ToDto(created);
    }

    /// <summary>
    /// 邮箱+口令登录。
    ///
    /// 注意：**登录已迁到 identity-service**（ADR-0020），本方法目前已无生产调用方，
    /// 保留是因为它仍是 Users 凭据校验在应用层的参考实现与单测对象；
    /// 计划在共存窗口结束后删除（避免两处凭据校验逻辑漂移）。
    /// </summary>
    public async Task<UserDto> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await _repository.GetByEmailAsync(NormalizeEmail(email), ct);
        // 先验凭据：这一步的失败与"账号被停用"必须返回同一个异常，
        // 否则接口会变成账号存在性/状态的探测器（见 docs/security/authentication.md）。
        if (user?.PasswordHash is null || !_passwordHasher.Verify(password, user.PasswordHash))
            throw new InvalidCredentialsException();

        // 凭据正确后再判停用：停用账号不得换到令牌（安全指引 P0）。
        // 未来身份服务化（ADR-0020）后此处挪到 identity-service，语义不变。
        if (!user.IsActive)
            throw new InvalidCredentialsException();

        return ToDto(user);
    }

    public async Task<UserDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var user = await _repository.GetByIdAsync(id, ct);
        return user is null ? null : ToDto(user);
    }

    public async Task<UserDto> UpdateProfileAsync(int id, UpdateProfileDto dto, CancellationToken ct = default)
    {
        var user = await _repository.GetByIdAsync(id, ct)
            ?? throw new UserNotFoundException();

        var normalizedEmail = NormalizeEmail(dto.Email);
        if (!string.Equals(user.Email, normalizedEmail, StringComparison.Ordinal)
            && await _repository.AnyByEmailAsync(normalizedEmail, id, ct))
            throw new EmailAlreadyExistsException(normalizedEmail);

        user.Name = dto.Name.Trim();
        user.Email = normalizedEmail;
        user.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();
        user.AvatarUrl = string.IsNullOrWhiteSpace(dto.AvatarUrl) ? null : dto.AvatarUrl.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        var updated = await _repository.UpdateAsync(user, ct)
            ?? throw new UserNotFoundException();
        return ToDto(updated);
    }

    public async Task ChangePasswordAsync(int id, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _repository.GetByIdAsync(id, ct);
        if (user?.PasswordHash is null || !_passwordHasher.Verify(currentPassword, user.PasswordHash))
            throw new InvalidCredentialsException();

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        // 凭据版本自增：identity-service 比对到不一致就会拒绝旧 refresh token（ADR-0020）
        user.CredentialVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(user, ct);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static UserDto ToDto(AppUser user) => new(user.Id, user.Name, user.Email, user.PhoneNumber, user.AvatarUrl);
}
