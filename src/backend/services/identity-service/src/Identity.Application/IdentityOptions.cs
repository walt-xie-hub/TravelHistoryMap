namespace Identity.Application;

/// <summary>
/// 身份服务的运行参数（ADR-0021/0023）。issuer 必须是**显式配置项**，不从请求头推导：
/// discovery 公布的 issuer 与令牌里的 iss 必须逐字一致，否则所有验签方都会拒绝。
/// </summary>
public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>例如 https://&lt;gateway-FQDN&gt;/identity；本地开发为 http://localhost:8090/identity</summary>
    public string Issuer { get; set; } = "http://localhost:8090/identity";

    /// <summary>用户令牌的受众（客户端标识）</summary>
    public string Audience { get; set; } = "travel-map-client";

    /// <summary>本服务自身的标识，用作服务令牌访问 /internal/* 时的 aud（ADR-0022）</summary>
    public string ServiceId { get; set; } = "identity-service";

    /// <summary>access token 有效期（分钟）。短 TTL 是无状态令牌唯一的兜底手段。</summary>
    public int AccessTokenMinutes { get; set; } = 20;

    /// <summary>服务令牌有效期（分钟）。比用户令牌更短：服务身份不需要人参与的续期，宁可多换几次。</summary>
    public int ServiceTokenMinutes { get; set; } = 5;

    /// <summary>refresh token 有效期（天）</summary>
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>
    /// 允许作为服务令牌 aud 的目标服务标识。**默认为空 = 谁都不能换服务令牌**（默认拒绝，ADR-0022）；
    /// 新增跨服务调用时在此登记被调服务，并给调用方 Client 授予相应 scope。
    /// </summary>
    public List<string> AllowedServiceAudiences { get; set; } = [];

    public LockoutOptions Lockout { get; set; } = new();

    public SigningKeyStoreOptions SigningKeyStore { get; set; } = new();

    public sealed class LockoutOptions
    {
        /// <summary>窗口内允许的连续失败次数；达到即锁定</summary>
        public int MaxFailedAttempts { get; set; } = 5;

        /// <summary>失败计数窗口（分钟）</summary>
        public int WindowMinutes { get; set; } = 15;

        /// <summary>锁定时长（分钟）</summary>
        public int LockoutMinutes { get; set; } = 15;
    }

    public sealed class SigningKeyStoreOptions
    {
        /// <summary>Development（本地文件）或 KeyVault（ADR-0023）</summary>
        public string Provider { get; set; } = "Development";

        /// <summary>Development：存放 jwt-signing-&lt;kid&gt;.pem 的目录</summary>
        public string Path { get; set; } = "keys";

        /// <summary>KeyVault：如 https://kv-travelmap.vault.azure.net/</summary>
        public string KeyVaultUri { get; set; } = string.Empty;

        /// <summary>指定用哪把密钥签发；留空则用最新创建的那把</summary>
        public string ActiveKid { get; set; } = string.Empty;
    }
}
