namespace User.Domain.Common;

/// <summary>邮箱已被其他账号占用（注册或改资料时）。</summary>
public sealed class EmailAlreadyExistsException(string email)
    : Exception($"Email '{email}' is already registered.");

/// <summary>邮箱或密码不正确（登录或改密码的旧密码校验）。</summary>
public sealed class InvalidCredentialsException()
    : Exception("Email or password is incorrect.");

/// <summary>用户不存在（通常对应 token 指向已删除的用户）。</summary>
public sealed class UserNotFoundException()
    : Exception("User not found.");
