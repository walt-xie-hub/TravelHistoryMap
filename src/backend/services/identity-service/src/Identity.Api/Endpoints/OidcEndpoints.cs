using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using Identity.Api.Security;
using Identity.Application;
using Identity.Application.Abstractions;

namespace Identity.Api.Endpoints;

public static class OidcEndpoints
{
    /// <summary>
    /// OIDC 发现与 JWKS（ADR-0021）。
    ///
    /// 刻意**不公布** authorization_endpoint / userinfo_endpoint：它们属于 P2（PKCE 迁移），
    /// 公布了却没实现比不公布更糟——客户端会照着写。
    /// </summary>
    public static void MapOidcEndpoints(this IEndpointRouteBuilder app, IdentityOptions options)
    {
        var issuer = options.Issuer.TrimEnd('/');

        app.MapGet("/identity/.well-known/openid-configuration", () => Results.Ok(new
        {
            issuer = options.Issuer,
            jwks_uri = $"{issuer}/jwks",
            token_endpoint = $"{issuer}/token",
            id_token_signing_alg_values_supported = new[] { SecurityAlgorithms.RsaSha256 },
            grant_types_supported = new[] { "refresh_token", "client_credentials" },
            response_types_supported = new[] { "token" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post", "none" },
            // 默认拒绝：当前不开放任何 scope（ADR-0022）
            scopes_supported = options.AllowedServiceAudiences.Count == 0
                ? []
                : options.AllowedServiceAudiences.ToArray(),
        }));

        app.MapGet("/identity/jwks", (ISigningKeyStore signingKeys) => Results.Ok(new
        {
            keys = signingKeys.GetKeys()
                .OrderBy(k => k.Kid, StringComparer.Ordinal)
                .Select(key =>
                {
                    var parameters = key.Key.ExportParameters(includePrivateParameters: false);
                    return new
                    {
                        kty = "RSA",
                        use = "sig",
                        alg = SecurityAlgorithms.RsaSha256,
                        kid = key.Kid,
                        n = Base64UrlEncoder.Encode(parameters.Modulus),
                        e = Base64UrlEncoder.Encode(parameters.Exponent),
                    };
                })
                .ToArray(),
        }));
    }
}

public static class InternalEndpoints
{
    /// <summary>
    /// 内部端点骨架（ADR-0019/0022）：统一挂在 <c>/internal/*</c>，**不在网关转发的
    /// <c>/api/*</c> 前缀下**，并且必须要求服务身份——无 token 401、用户 token 403、aud 不匹配 401。
    /// </summary>
    public static void MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal").RequireAuthorization(ServiceIdentity.PolicyName);

        group.MapGet("/whoami", (ClaimsPrincipal user) => Results.Ok(new
        {
            subject = user.CallerSubject(),
            scopes = (user.FindFirstValue("scope") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries),
        }));
    }
}
