using Identity.Application.Models;

namespace Identity.Application.Abstractions;

/// <summary>
/// 只读访问 user-service 拥有的 Users 表（ADR-0020）。
///
/// 这是本服务与 Users 表之间的**唯一**通道：只读认证所需的最小列，
/// 永远不写。刻意不用 EF 实体映射，避免误建表或与 user-service 的模型漂移。
/// </summary>
public interface ICredentialReader
{
    /// <summary>按规范化（小写）邮箱读取凭据；不存在返回 null。</summary>
    Task<UserCredential?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>按用户 id 读取凭据；不存在返回 null。刷新令牌时需要重新确认 CredentialVersion/IsActive。</summary>
    Task<UserCredential?> FindByIdAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>启动期等待 Users 表就绪（user-service 可能先于本服务建表）。</summary>
    Task WaitUntilAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>口令校验。与 user-service 用同一个框架实现（PBKDF2 v3），哈希格式天然互认，不共享代码。</summary>
public interface IPasswordVerifier
{
    bool Verify(string password, string passwordHash);

    /// <summary>
    /// 一个固定的、任何人都匹配不上的哈希。账号不存在时也拿它跑一次校验，
    /// 把"存在/不存在"的响应耗时拉平，避免用响应时间枚举已注册邮箱。
    /// </summary>
    string DummyHash { get; }
}
