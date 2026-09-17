using Identity.Api.Security;
using Identity.Application.Abstractions;
using Identity.Application.Services;
using Identity.Domain.Common;

namespace Identity.Api.Endpoints;

public sealed record LoginRequest(string Email, string Password, string? CaptchaId, string? CaptchaAnswer);

public sealed record RefreshRequest(string? RefreshToken);

/// <summary>
/// 令牌响应。**不含用户档案**：档案归 user-service，客户端登录后自行调
/// <c>GET /api/users/me</c> 获取（ADR-0020）。因此这里也不再复制 user 对象。
/// </summary>
public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds, string TokenType = "Bearer");

public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/identity");

        group.MapGet("/captcha", GetCaptchaAsync);
        group.MapPost("/login", LoginAsync);
        // 登出只需持有该 refresh token 即可撤销（RFC 7009 的语义），因此不要求 access token：
        // 少一条"必须携带有效 access token 才能登出"的耦合，令牌过期后依然能登出。
        group.MapPost("/logout", LogoutAsync);
    }

    private static IResult GetCaptchaAsync(CaptchaService captchas, HttpContext context)
    {
        var captcha = captchas.Create();

        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        context.Response.Headers.Pragma = "no-cache";

        return Results.Ok(new { captchaId = captcha.Id, captchaImage = $"data:image/png;base64,{captcha.ImageBase64}" });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest dto,
        CaptchaService captchas,
        AuthenticationService authentication,
        HttpContext http,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return Results.BadRequest(new { message = "邮箱和密码不能为空。" });

        // 图片验证码：先取后验；无论对错都消耗，防重放。
        if (string.IsNullOrWhiteSpace(dto.CaptchaId) || string.IsNullOrWhiteSpace(dto.CaptchaAnswer))
            return Results.BadRequest(new { message = "请输入图片验证码。" });
        if (!captchas.Validate(dto.CaptchaId, dto.CaptchaAnswer))
            return Results.BadRequest(new { message = "验证码错误或已过期，请刷新后重试。" });

        try
        {
            var tokens = await authentication.LoginAsync(
                dto.Email,
                dto.Password,
                RequestContext.ClientIp(http),
                http.Request.Headers.UserAgent.ToString(),
                ct);

            return Results.Ok(new TokenResponse(tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresInSeconds));
        }
        catch (InvalidCredentialsException)
        {
            // 不存在 / 口令错误 / 已停用 / 已锁定，对外都是同一个 401（不泄露账号状态）
            return Results.Json(new { message = "邮箱或密码错误。" }, statusCode: StatusCodes.Status401Unauthorized);
        }
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest dto,
        RefreshTokenService refreshTokens,
        CancellationToken ct)
    {
        await refreshTokens.RevokeByRawTokenAsync(dto.RefreshToken ?? string.Empty, ct);
        // 幂等：令牌不存在也返回 204，避免这个端点变成"令牌是否有效"的探测器
        return Results.NoContent();
    }
}

/// <summary>取请求上下文信息用于审计。IP 取自边缘追加的 X-Forwarded-For 最右一段。</summary>
internal static class RequestContext
{
    public static string? ClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var last = forwarded.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            if (!string.IsNullOrWhiteSpace(last))
                return last;
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
