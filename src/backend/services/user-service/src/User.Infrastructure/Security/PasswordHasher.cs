using User.Application.Abstractions;
using User.Domain.Entities;

namespace User.Infrastructure.Security;

/// <summary>
/// 基于 ASP.NET Core Identity 内置 <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/>
/// （PBKDF2-SHA256 + 随机盐 + 版本前缀）的密码哈希实现（ADR-0005）。
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<AppUser> _inner = new();

    public string Hash(string plainText) => _inner.HashPassword(null!, plainText);

    public bool Verify(string plainText, string passwordHash)
        => _inner.VerifyHashedPassword(null!, passwordHash, plainText)
           != Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed;
}
