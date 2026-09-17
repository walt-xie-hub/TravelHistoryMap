using User.Application.Abstractions;
using User.Application.DTOs;
using User.Domain.Common;

namespace User.Api.Endpoints;

/// <summary>
/// 注册端点。**登录与验证码已迁到 identity-service**（ADR-0020）：
/// - POST /api/auth/register  注册（唯一创建用户途径，写入数据库；不签发 token）
///
/// 注册仍然留在本服务，因为它是 User 聚合（档案 + 凭据）的创建动作，
/// 而 user-service 是 Users 表的唯一写者。
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/register", RegisterAsync);
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
