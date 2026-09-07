using User.Api.Security;
using User.Application.Abstractions;
using User.Application.DTOs;
using User.Domain.Common;

namespace User.Api.Endpoints;

/// <summary>
/// 认证端点（ADR-0005）：
/// - GET  /api/auth/captcha   获取图片验证码（登录防自动化）
/// - POST /api/auth/register  注册（唯一创建用户途径，写入数据库；不签发 token，注册后由前端引导重新登录）
/// - POST /api/auth/login     登录（需携带图片验证码，成功签发 token）
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapGet("/captcha", GetCaptchaAsync);
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
    }

    private static IResult GetCaptchaAsync(CaptchaService captchas, HttpContext context)
    {
        var captcha = captchas.Create();

        // 验证码必须每次请求都重新生成，禁止浏览器/中间代理缓存，避免用户看到过期图片。
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        context.Response.Headers.Pragma = "no-cache";

        return Results.Ok(new { captchaId = captcha.Id, captchaImage = $"data:image/png;base64,{captcha.ImageBase64}" });
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequestDto dto,
        IUserService users,
        CancellationToken ct)
    {
        if (ValidateCredentials(dto.Name, dto.Email, dto.Password) is { } error)
            return error;

        try
        {
            var user = await users.RegisterAsync(dto.Name, dto.Email, dto.Password, ct);
            // 只落库返回用户信息，不签发 token —— 注册成功后前端跳转登录页由用户重新登录。
            return Results.Ok(new { user });
        }
        catch (EmailAlreadyExistsException)
        {
            return Results.Conflict(new { message = "该邮箱已注册，请直接登录。" });
        }
    }

    private static async Task<IResult> LoginAsync(
        LoginRequestDto dto,
        IUserService users,
        JwtTokenFactory tokens,
        CaptchaService captchas,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return Results.BadRequest(new { message = "邮箱和密码不能为空。" });

        // 图片验证码：必须先取后验；一次性凭证无论对错都会消耗，防重放。
        if (string.IsNullOrWhiteSpace(dto.CaptchaId) || string.IsNullOrWhiteSpace(dto.CaptchaAnswer))
            return Results.BadRequest(new { message = "请输入图片验证码。" });
        if (!captchas.Validate(dto.CaptchaId, dto.CaptchaAnswer))
            return Results.BadRequest(new { message = "验证码错误或已过期，请刷新后重试。" });

        try
        {
            var user = await users.LoginAsync(dto.Email, dto.Password, ct);
            return Results.Ok(new AuthResponseDto(tokens.Create(user), user));
        }
        catch (InvalidCredentialsException)
        {
            // 验证码已消耗，前端应在收到 401 后刷新验证码
            return Results.Json(new { message = "邮箱或密码错误。" }, statusCode: StatusCodes.Status401Unauthorized);
        }
    }

    private static IResult? ValidateCredentials(string name, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Results.BadRequest(new { message = "名称、邮箱和密码不能为空。" });
        if (!email.Contains('@'))
            return Results.BadRequest(new { message = "邮箱格式不正确。" });
        if (password.Length < 8)
            return Results.BadRequest(new { message = "密码长度至少 8 位。" });
        return null;
    }
}
