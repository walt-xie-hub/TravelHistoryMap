namespace User.Application.DTOs;

/// <summary>
/// 对外暴露的用户视图模型（不含领域内部细节）。
/// </summary>
public record UserDto(int Id, string Name, string Email);

/// <summary>
/// 创建用户请求模型。
/// </summary>
public record CreateUserDto(string Name, string Email);

/// <summary>
/// 更新用户请求模型（不含 Id 与密码哈希等敏感/服务端字段）。
/// </summary>
public record UpdateUserDto(
    string Name,
    string Email,
    string? PhoneNumber = null,
    string? AvatarUrl = null,
    bool IsActive = true);
