using Identity.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure.Security;

/// <summary>
/// 口令/secret 校验。用的是框架内置的 <see cref="PasswordHasher{TUser}"/>（PBKDF2 v3），
/// 与 user-service 写入 <c>Users.PasswordHash</c> 时用的是同一个类 ——
/// 因此两侧哈希格式天然互认，**不需要共享代码**（ADR-0020）。
/// </summary>
public sealed class PasswordVerifier : IPasswordVerifier, IServiceSecretHasher
{
    // 进程内只算一次：PBKDF2 默认 10 万次迭代，没必要每个实例重算。
    private static readonly string Dummy = new PasswordHasher<object>()
        .HashPassword(new object(), Guid.NewGuid().ToString("N"));

    private readonly PasswordHasher<object> _inner = new();
    private readonly object _subject = new();

    public string DummyHash => Dummy;

    public bool Verify(string password, string passwordHash)
        => _inner.VerifyHashedPassword(_subject, passwordHash, password) != PasswordVerificationResult.Failed;

    public string Hash(string secret) => _inner.HashPassword(_subject, secret);
}
