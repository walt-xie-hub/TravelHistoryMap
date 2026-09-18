using Identity.Application.Abstractions;
using Shared.Observability;

namespace Identity.Application.Services;

/// <summary>client credentials 的结果。失败时 <see cref="Error"/> 取 OAuth 的 error 语义。</summary>
public sealed record ServiceTokenResult(
    bool Succeeded,
    string? AccessToken,
    int ExpiresInSeconds,
    string? Scopes,
    string Error)
{
    public static ServiceTokenResult Ok(string token, int expiresInSeconds, string scopes)
        => new(true, token, expiresInSeconds, scopes, string.Empty);

    public static ServiceTokenResult Fail(string error) => new(false, null, 0, null, error);
}

/// <summary>
/// 服务身份（ADR-0022）：client credentials 换短 TTL 令牌。
///
/// **默认拒绝**：没有任何 scope 预置、没有一个 allowed audience 预置时，
/// 任何 client 都拿不到可用的服务令牌——这正是"契约先行、按需开放"的落地方式。
/// </summary>
public sealed class ClientCredentialsService(
    IServiceClientRepository clients,
    IPasswordVerifier secrets,
    IAccessTokenIssuer accessTokens,
    IdentityOptions options,
    IAuditLog audit)
{
    public async Task<ServiceTokenResult> CreateTokenAsync(
        string clientId,
        string clientSecret,
        string audience,
        string? requestedScope,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return Deny(clientId, "invalid_client");

        var client = await clients.FindByIdAsync(clientId, ct);
        if (client is null || !client.IsActive || !secrets.Verify(clientSecret, client.ClientSecretHash))
            return Deny(clientId, "invalid_client");

        if (!options.AllowedServiceAudiences.Contains(audience, StringComparer.Ordinal))
            return Deny(clientId, "invalid_target");

        var granted = client.Scopes.Select(s => s.Scope).ToHashSet(StringComparer.Ordinal);
        var requested = (requestedScope ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (requested.Any(s => !granted.Contains(s)))
            return Deny(clientId, "invalid_scope");

        var scopes = string.Join(' ', requested);
        var token = accessTokens.CreateServiceToken(client.ClientId, audience, requested);
        audit.ServiceTokenIssued(client.ClientId, audience, scopes);
        return ServiceTokenResult.Ok(token, accessTokens.ServiceTokenLifetimeSeconds, scopes);
    }

    private ServiceTokenResult Deny(string clientId, string error)
    {
        audit.ServiceTokenRequestDenied(clientId, error);
        return ServiceTokenResult.Fail(error);
    }
}
