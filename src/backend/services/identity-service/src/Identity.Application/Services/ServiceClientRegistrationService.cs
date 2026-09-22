using Identity.Application.Abstractions;
using Identity.Domain.Entities;

namespace Identity.Application.Services;

/// <summary>部署期注册服务客户端；不暴露 HTTP 管理端点。</summary>
public sealed class ServiceClientRegistrationService(
    IServiceClientRepository clients,
    IServiceSecretHasher secrets,
    IdentityOptions options)
{
    /// <summary>
    /// 仓库跟踪的 appsettings 里用的占位符。它出现在这里说明**真实 secret 没有注入**：
    /// 与其把 <c>__SET_VIA_ENV__</c> 当成一个真能用的凭据存进库里，不如拒绝启动（fail closed）。
    /// </summary>
    public const string EnvironmentPlaceholder = "__SET_VIA_ENV__";

    public async Task EnsureRegisteredAsync(CancellationToken ct = default)
    {
        foreach (var registration in options.ServiceClients)
        {
            Validate(registration);

            var existing = await clients.FindByIdAsync(registration.ClientId, ct);
            if (existing is null)
            {
                await clients.AddAsync(new ServiceClient
                {
                    ClientId = registration.ClientId,
                    ClientSecretHash = secrets.Hash(registration.ClientSecret),
                    DisplayName = registration.DisplayName,
                    Scopes = CreateScopes(registration),
                }, ct);
                continue;
            }

            existing.ClientSecretHash = secrets.Verify(registration.ClientSecret, existing.ClientSecretHash)
                ? existing.ClientSecretHash
                : secrets.Hash(registration.ClientSecret);
            existing.DisplayName = registration.DisplayName;
            existing.IsActive = true;
            existing.Scopes = CreateScopes(registration);
            clients.Update(existing);
        }

        if (options.ServiceClients.Count > 0)
            await clients.SaveChangesAsync(ct);
    }

    private static void Validate(IdentityOptions.ServiceClientRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(registration.ClientId))
            throw new InvalidOperationException("Identity:ServiceClients contains an empty ClientId.");

        if (string.IsNullOrWhiteSpace(registration.ClientSecret))
            throw new InvalidOperationException($"Service client '{registration.ClientId}' has no secret.");

        if (string.Equals(registration.ClientSecret, EnvironmentPlaceholder, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Service client '{registration.ClientId}' 仍是占位符 secret：请通过环境变量注入真实值。");

        if (string.IsNullOrWhiteSpace(registration.DisplayName))
            registration.DisplayName = registration.ClientId;

        if (registration.Scopes.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"Service client '{registration.ClientId}' contains an empty scope.");
    }

    private static List<ServiceClientScope> CreateScopes(IdentityOptions.ServiceClientRegistration registration)
        => registration.Scopes
            .Distinct(StringComparer.Ordinal)
            .Select(scope => new ServiceClientScope
            {
                ClientId = registration.ClientId,
                Scope = scope,
            })
            .ToList();
}