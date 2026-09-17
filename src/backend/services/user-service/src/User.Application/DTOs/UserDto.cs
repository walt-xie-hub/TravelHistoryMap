namespace User.Application.DTOs;

/// <summary>
/// 对外暴露的用户档案视图模型。
/// phoneNumber/avatarUrl 供“修改用户信息”与右上角头像展示使用。
/// </summary>
public record UserDto(
    int Id,
    string Name,
    string Email,
    string? PhoneNumber = null,
    string? AvatarUrl = null);

/// <summary>更新本人资料请求（仅允许改自己的档案，不暴露启用状态）。</summary>
public record UpdateProfileDto(
    string Name,
    string Email,
    string? PhoneNumber = null,
    string? AvatarUrl = null);

/// <summary>修改密码请求（需携带当前密码做校验）。</summary>
public record ChangePasswordDto(string CurrentPassword, string NewPassword);

/// <summary>注册请求。</summary>
public record RegisterRequestDto(string Name, string Email, string Password);
