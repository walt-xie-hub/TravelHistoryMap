using Identity.Application.Abstractions;
using Shared.Observability;

namespace Identity.Application.Services;

/// <summary>
/// 用户委托（token exchange，ADR-0024）：把「用户的令牌 + 调用方自己的服务凭证」
/// 换成一枚**新的**令牌，同时携带两个身份：
///
/// <list type="bullet">
/// <item><c>sub</c> = 用户的 Users.Id —— 回答“代表谁”（授权上下文）；</item>
/// <item><c>act</c> = service:&lt;clientId&gt; —— 回答“谁在调”（调用方身份，由 client credentials 证伪不了）；</item>
/// <item><c>aud</c> = 被调服务 —— 一个令牌只对一个目标有效。</item>
/// </list>
///
/// 为什么不直接转发用户令牌：用户令牌的 aud 是客户端标识，拿到别的服务上应当被拒；
/// 而且转发等于让用户令牌“哪里都能用”，也让调用方无法把权限收窄（docs/security/service-identity.md
/// “不允许的形态”）。委托令牌是**重新签发**的：TTL 更短、scope 更小、目标单一。
///
/// **默认拒绝**（与 client credentials 同一套裁决姿态）：未注册的 client、未放行的 audience、
/// 未授予的 scope、无效的主体令牌，一律换不到令牌。
/// </summary>
public sealed class TokenExchangeService(
    IServiceClientRepository clients,
    IPasswordVerifier secrets,
    ICredentialReader credentials,
    IUserTokenValidator subjectTokens,
    IAccessTokenIssuer accessTokens,
    IdentityOptions options,
    IAuditLog audit)
{
    public async Task<ServiceTokenResult> ExchangeAsync(
        string clientId,
        string clientSecret,
        string subjectToken,
        string audience,
        string? requestedScope,
        CancellationToken ct = default)
    {
        // ① 先验调用方。放在最前面有两个理由：未认证的请求不应该有机会去试探主体令牌；
        //    而且后面的裁决都需要“这个问题是哪个 client 问的”才能审计。
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return Deny(clientId, "invalid_client");

        var client = await clients.FindByIdAsync(clientId, ct);
        if (client is null || !client.IsActive || !secrets.Verify(clientSecret, client.ClientSecretHash))
            return Deny(clientId, "invalid_client");

        // ② 目标服务必须在白名单里（默认拒绝，ADR-0022）。
        if (!options.AllowedServiceAudiences.Contains(audience, StringComparer.Ordinal))
            return Deny(clientId, "invalid_target");

        // ③ 主体必须是本服务签发的、仍然有效的**用户**令牌。
        var subjectUserId = subjectTokens.Validate(subjectToken);
        if (subjectUserId is null)
            return Deny(clientId, "invalid_grant");

        // ④ 复审账号状态：已签发的用户令牌可能还没过期，但账号可能已被停用。
        //    少了这一步，停用账号能用旧令牌持续换出新的委托令牌 —— 与刷新路径同一条契约（ADR-0020）。
        var user = await credentials.FindByIdAsync(subjectUserId.Value, ct);
        if (user is null || !user.IsActive)
            return Deny(clientId, "invalid_grant");

        // ⑤ scope：这里比 client credentials **更严** —— 必须显式请求，且必须已被授予。
        //    空的 scope 会被拒绝，否则委托令牌就退化成“用户令牌的副本”，
        //    接收方也无法用 scope 表达“这一次调用只允许做什么”。
        var requested = (requestedScope ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (requested.Length == 0)
            return Deny(clientId, "invalid_scope");

        var granted = client.Scopes.Select(s => s.Scope).ToHashSet(StringComparer.Ordinal);
        if (requested.Any(s => !granted.Contains(s)))
            return Deny(clientId, "invalid_scope");

        var scopes = string.Join(' ', requested);
        var token = accessTokens.CreateDelegatedToken(user.Id, client.ClientId, audience, requested);
        audit.DelegatedTokenIssued(user.Id, client.ClientId, audience, scopes);

        return ServiceTokenResult.Ok(token, accessTokens.ServiceTokenLifetimeSeconds, scopes);
    }

    private ServiceTokenResult Deny(string clientId, string error)
    {
        audit.DelegatedTokenDenied(clientId, error);
        return ServiceTokenResult.Fail(error);
    }
}
