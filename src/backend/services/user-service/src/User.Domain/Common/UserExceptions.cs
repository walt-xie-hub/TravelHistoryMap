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

/// <summary>
/// 足迹概览暂时读不到（ADR-0025）：依赖的下游（identity-service / travel-service）不可用。
///
/// 刻意与 <see cref="UserNotFoundException"/> 之类分开：这是**暂时性**故障，表现层应回 503；
/// 而配置/契约破裂（scope 未授予、audience 未登记）**不用**这个类型，让它响亮地变成 500。
/// </summary>
public sealed class FootprintUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
