using Identity.Application.Models;

namespace Identity.Application.Abstractions;

/// <summary>
/// 访问令牌签发。抽象放在应用层、由 API 层用 JWT 库实现，
/// 这样认证/刷新逻辑可以完全脱离框架做单元测试。
/// </summary>
public interface IAccessTokenIssuer
{
    /// <summary>用户令牌：sub = Users.Id，aud = 客户端标识（ADR-0021）。</summary>
    string CreateUserToken(UserCredential user);

    /// <summary>服务令牌：sub = service:&lt;clientId&gt;，aud = 被调服务，scope 表达允许的动作。</summary>
    string CreateServiceToken(string clientId, string audience, IReadOnlyCollection<string> scopes);

    /// <summary>
    /// 委托令牌（token exchange）：sub = 用户的 Users.Id（**不是** service:*），
    /// aud = 被调服务，act = 代理发起方（service:&lt;clientId&gt;）。
    ///
    /// 与 <see cref="CreateServiceToken"/> 的关键差别就在 sub：那一枚回答“谁在调”，这一枚同时回答
    /// “代表谁”。接收方靠 act 的存在把两者区分开，因此 act 是安全的判别位，不能省。
    /// </summary>
    string CreateDelegatedToken(int userId, string actorClientId, string audience, IReadOnlyCollection<string> scopes);

    /// <summary>access token 有效期（秒），由实现与签发配置保持一致。</summary>
    int AccessTokenLifetimeSeconds { get; }

    /// <summary>服务令牌有效期（秒）。比用户令牌更短（ADR-0022）。</summary>
    int ServiceTokenLifetimeSeconds { get; }
}
