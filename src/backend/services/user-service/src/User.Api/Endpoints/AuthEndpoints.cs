using User.Api.Security;
using User.Application.Abstractions;
using User.Application.DTOs;
using User.Domain.Common;

namespace User.Api.Endpoints;

/// <summary>
/// 认证端点：注册（唯一创建用户途径，成功后直接签发 token 实现自动登录）与登录（ADR-0005）。
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequestDto dto,
        IUserService users,
        JwtTokenFactory tokens,
        CancellationToken ct)
    {
        if (ValidateCredentials(dto.Name, dto.Email, dto.Password) is { } error)
            return error;

        try
        {
            var user = await users.RegisterAsync(dto.Name, dto.Email, dto.Password, ct);
            return Results.Ok(new AuthResponseDto(tokens.Create(user), user));
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
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return Results.BadRequest(new { message = "邮箱和密码不能为空。" });

        try
        {
            var user = await users.LoginAsync(dto.Email, dto.Password, ct);
            return Results.Ok(new AuthResponseDto(tokens.Create(user), user));
        }
        catch (InvalidCredentialsException)
        {
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
