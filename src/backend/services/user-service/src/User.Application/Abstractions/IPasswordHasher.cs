namespace User.Application.Abstractions;

/// <summary>
/// 密码哈希抽象（应用层不关心具体算法；实现为 PBKDF2，见 User.Infrastructure.Security）。
/// </summary>
public interface IPasswordHasher
{
    /// <summary>对明文密码做加盐哈希。</summary>
    string Hash(string plainText);

    /// <summary>校验明文密码是否匹配给定哈希。</summary>
    bool Verify(string plainText, string passwordHash);
}
