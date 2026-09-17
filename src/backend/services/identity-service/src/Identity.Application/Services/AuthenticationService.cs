using Identity.Application.Abstractions;
using Identity.Application.Models;
using Identity.Domain.Common;
using Identity.Domain.Entities;

namespace Identity.Application.Services;

/// <summary>
/// 用户认证（ADR-0020/0021）。凭据只读自 user-service 拥有的 Users 表，零跨服务调用。
///
/// 对外一律不区分失败原因：不存在、口令错误、无凭据、已停用、已锁定
/// 全部抛 <see cref="InvalidCredentialsException"/>（接口不能变成账号状态探测器）。
/// 真实原因只进审计日志。
/// </summary>
public sealed class AuthenticationService(
    ICredentialReader credentials,
    IPasswordVerifier passwords,
    ILoginAttemptRepository attempts,
    RefreshTokenService refreshTokens,
    IClock clock,
    IdentityOptions options,
    IAuditLog audit)
{
    public async Task<AuthTokens> LoginAsync(
        string email,
        string password,
        string? ip,
        string? userAgent,
        CancellationToken ct = default)
    {
        var key = email.Trim().ToLowerInvariant();
        var now = clock.UtcNow;

        var attempt = await attempts.FindAsync(key, ct);
        if (attempt?.IsLocked(now) == true)
        {
            audit.LoginLocked(key, attempt.LockedUntil!.Value, ip, userAgent);
            throw new InvalidCredentialsException();
        }

        var user = await credentials.FindByEmailAsync(key, ct);
        var passwordOk = user?.PasswordHash is not null && passwords.Verify(password, user.PasswordHash);

        // 账号不存在时也走一次哈希校验：把"存在/不存在"的响应时间拉平，
        // 否则响应耗时就足以枚举出哪些邮箱注册过。
        if (user is null)
            passwords.Verify(password, passwords.DummyHash);

        if (!passwordOk || user is null || !user.IsActive)
        {
            await RegisterFailureAsync(key, attempt, now, ip, userAgent, ct);
            throw new InvalidCredentialsException();
        }

        if (attempt is not null && (attempt.FailedCount > 0 || attempt.LockedUntil is not null))
        {
            attempt.FailedCount = 0;
            attempt.LockedUntil = null;
            await attempts.SaveChangesAsync(ct);
        }

        audit.LoginSucceeded(user.Id, ip, userAgent);
        return await refreshTokens.IssueNewFamilyAsync(user, options.Audience, ct);
    }

    private async Task RegisterFailureAsync(
        string key,
        LoginAttempt? attempt,
        DateTime now,
        string? ip,
        string? userAgent,
        CancellationToken ct)
    {
        var lockout = options.Lockout;
        var windowStart = now.AddMinutes(-lockout.WindowMinutes);

        if (attempt is null)
        {
            attempt = new LoginAttempt { Key = key, FailedCount = 1, FirstFailedAt = now };
            await attempts.AddAsync(attempt, ct);
        }
        else
        {
            // 窗口外的旧失败不再累计（滑动窗口语义）
            attempt.FailedCount = attempt.FirstFailedAt < windowStart ? 1 : attempt.FailedCount + 1;
            if (attempt.FirstFailedAt < windowStart)
                attempt.FirstFailedAt = now;
        }

        if (attempt.FailedCount >= lockout.MaxFailedAttempts)
            attempt.LockedUntil = now.AddMinutes(lockout.LockoutMinutes);

        await attempts.SaveChangesAsync(ct);
        audit.LoginFailed(key, ip, userAgent);

        if (attempt.IsLocked(now))
            audit.LoginLocked(key, attempt.LockedUntil!.Value, ip, userAgent);
    }
}
