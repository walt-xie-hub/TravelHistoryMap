namespace Identity.Domain.Entities;

/// <summary>
/// 服务客户端（ADR-0022）：一个后端服务在身份提供方注册后的身份，用于 client credentials 换 token。
///
/// 与 User 是**两套凭据体系**：不复用用户令牌的密钥或语义，也不得被用来做
/// "跨服务同步调用校验用户存在性"（ADR-0002 明确否掉）。
/// </summary>
public class ServiceClient
{
    public string ClientId { get; set; } = string.Empty;

    /// <summary>secret 只存哈希（PBKDF2，与用户口令同一实现），不存明文</summary>
    public string ClientSecretHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>被允许的 scope；起步阶段为空集（默认拒绝，ADR-0022）</summary>
    public List<ServiceClientScope> Scopes { get; set; } = [];
}
