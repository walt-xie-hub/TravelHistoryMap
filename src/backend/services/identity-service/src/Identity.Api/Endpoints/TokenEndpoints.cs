using Identity.Application.Services;
using Shared.Observability;

namespace Identity.Api.Endpoints;

/// <summary>
/// <c>POST /identity/token</c>：OAuth 风格的令牌端点（表单编码），
/// 承载 refresh_token 与 client_credentials 两种 grant。
///
/// 第一期前端换 token 走这里；P2 迁移到 Authorization Code + PKCE 后，
/// 这个端点继续作为 OIDC 的 token_endpoint。
/// </summary>
public static class TokenEndpoints
{
    public static void MapTokenEndpoint(this IEndpointRouteBuilder app)
        => app.MapPost("/identity/token", TokenAsync);

    private static async Task<IResult> TokenAsync(
        HttpRequest request,
        RefreshTokenService refreshTokens,
        ClientCredentialsService clients,
        HttpContext http,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
            return OAuthError("invalid_request", "token 端点要求 application/x-www-form-urlencoded。");

        var form = await request.ReadFormAsync(ct);

        return form["grant_type"].ToString() switch
        {
            "refresh_token" => await RefreshAsync(form["refresh_token"].ToString(), refreshTokens, http, ct),
            "client_credentials" => await ClientCredentialsAsync(form, clients, ct),
            _ => OAuthError("unsupported_grant_type", "仅支持 refresh_token 与 client_credentials。"),
        };
    }

    private static async Task<IResult> RefreshAsync(
        string refreshToken,
        RefreshTokenService refreshTokens,
        HttpContext http,
        CancellationToken ct)
    {
        var tokens = await refreshTokens.RotateAsync(
            refreshToken,
            RequestContext.ClientIp(http),
            http.Request.Headers.UserAgent.ToString(),
            ct);

        // 拒绝原因不外泄（不存在的令牌、已轮换的令牌、已撤销、凭据版本变化都在这条路径上）
        return tokens is null
            ? OAuthError("invalid_grant", "刷新令牌无效、已过期或已被撤销。")
            : Results.Ok(new TokenResponse(tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresInSeconds));
    }

    private static async Task<IResult> ClientCredentialsAsync(
        IFormCollection form,
        ClientCredentialsService clients,
        CancellationToken ct)
    {
        var result = await clients.CreateTokenAsync(
            form["client_id"].ToString(),
            form["client_secret"].ToString(),
            form["audience"].ToString(),
            form["scope"].ToString(),
            ct);

        return result.Succeeded
            ? Results.Ok(new
            {
                access_token = result.AccessToken,
                token_type = "Bearer",
                expires_in = result.ExpiresInSeconds,
                scope = result.Scopes,
            })
            : OAuthError(result.Error, "服务令牌请求被拒绝（默认拒绝：未注册的 client / audience 或未授予的 scope）。");
    }

    private static IResult OAuthError(string error, string description)
        => Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);
}
