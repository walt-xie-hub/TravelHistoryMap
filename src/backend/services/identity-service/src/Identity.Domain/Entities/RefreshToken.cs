namespace Identity.Domain.Entities;

/// <summary>
/// 刷新令牌记录（ADR-0021）。**只存哈希，绝不存原文**——原文只在签发响应里出现一次。
///
/// 轮换语义：每次使用即换发新令牌，旧记录写入 <see cref="ReplacedByHash"/>。
/// 若一个已被替换的令牌再次被使用，说明它已泄露（客户端与服务端副本不一致），
/// 此时撤销整个 <see cref="FamilyId"/>。
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>归属用户（引用 user-service 的 Users.Id，跨服务外键；ADR-0002/0020）</summary>
    public int UserId { get; set; }

    /// <summary>换取该令牌的客户端标识</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>令牌原文的 SHA-256（十六进制小写）</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>一次登录派生出的令牌链；撤销以族为单位</summary>
    public Guid FamilyId { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>轮换后继任者的哈希；非空即表示本令牌已被使用过一次</summary>
    public string? ReplacedByHash { get; set; }

    /// <summary>签发时 Users.CredentialVersion 的快照：改密码/停用后旧 refresh 立即失效（ADR-0020）</summary>
    public int CredentialVersionAtIssue { get; set; } = 1;

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
