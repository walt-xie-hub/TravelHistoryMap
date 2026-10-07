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
    /// <summary>RFC 8693 的 token exchange grant type。</summary>
    private const string TokenExchangeGrantType = "urn:ietf:params:oauth:grant-type:token-exchange";

    /// <summary>仅接受 access token 作为主体令牌（用户令牌就是 access token）。</summary>
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    public static void MapTokenEndpoint(this IEndpointRouteBuilder app)
        => app.MapPost("/identity/token", TokenAsync);

    private static async Task<IResult> TokenAsync(
        HttpRequest request,
        RefreshTokenService refreshTokens,
        ClientCredentialsService clients,
        TokenExchangeService exchanges,
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
            TokenExchangeGrantType => await ExchangeAsync(form, exchanges, ct),
            _ => OAuthError("unsupported_grant_type", "仅支持 refresh_token、client_credentials 与 token exchange。"),
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

    /// <summary>
    /// token exchange（RFC 8693，ADR-0024）：调用方用**自己的服务凭证** + **用户的令牌**，
    /// 换一枚 sub=用户、act=自己、aud=目标服务的新令牌。
    ///
    /// 这里刻意不复用 client_credentials 的那条分支：两者的裁决完全不同 ——
    /// 委托多了“主体令牌是否有效”，而且 scope 是**必填**的（防止退化成“用户令牌的副本”）。
    /// </summary>
    private static async Task<IResult> ExchangeAsync(
        IFormCollection form,
        TokenExchangeService exchanges,
        CancellationToken ct)
    {
        // RFC 8693 要求显式声明主体令牌的类型；只接受 access token（用户令牌就是 access token）。
        if (form["subject_token_type"].ToString() != AccessTokenType)
            return OAuthError("invalid_request", $"subject_token_type 必须是 {AccessTokenType}。");

        if (string.IsNullOrWhiteSpace(form["subject_token"].ToString()))
            return OAuthError("invalid_request", "缺少 subject_token。");

        var result = await exchanges.ExchangeAsync(
            form["client_id"].ToString(),
            form["client_secret"].ToString(),
            form["subject_token"].ToString(),
            form["audience"].ToString(),
            form["scope"].ToString(),
            ct);

        return result.Succeeded
            ? Results.Ok(new
            {
                access_token = result.AccessToken,
                // RFC 8693 要求回显签发出来的令牌类型，客户端据此判断拿到的是什么
                issued_token_type = AccessTokenType,
                token_type = "Bearer",
                expires_in = result.ExpiresInSeconds,
                scope = result.Scopes,
            })
            : OAuthError(
                result.Error,
                "委托令牌请求被拒绝（默认拒绝：未注册的 client、未放行的 audience、未授予的 scope 或无效的主体令牌）。");
    }

    private static IResult OAuthError(string error, string description)
        => Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);
}
