using System.Security.Claims;
using User.Application.Abstractions;
using User.Application.DTOs;
using User.Domain.Common;

namespace User.Api.Endpoints;

/// <summary>
/// 本人档案端点（me 资源，ADR-0005）：全部要求有效 JWT，userId 一律取自 token 而非路径/查询参数。
/// 公开的 users CRUD（创建/改任意用户/删除/列表）已下线，注册是创建用户的唯一途径。
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();
        group.MapGet("/me", GetMeAsync);
        group.MapPut("/me", UpdateMeAsync);
        group.MapPut("/me/password", ChangePasswordAsync);
    }

    private static int CurrentUserId(ClaimsPrincipal principal)
        => int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private static async Task<IResult> GetMeAsync(ClaimsPrincipal principal, IUserService users, CancellationToken ct)
    {
        var me = await users.GetByIdAsync(CurrentUserId(principal), ct);
        return me is null
            ? Results.NotFound(new { message = "用户不存在。" })
            : Results.Ok(me);
    }

    private static async Task<IResult> UpdateMeAsync(ClaimsPrincipal principal, UpdateProfileDto dto, IUserService users, CancellationToken ct)
    {
        try
        {
            var me = await users.UpdateProfileAsync(CurrentUserId(principal), dto, ct);
            return Results.Ok(me);
        }
        catch (EmailAlreadyExistsException)
        {
            return Results.Conflict(new { message = "该邮箱已被其他账号使用。" });
        }
        catch (UserNotFoundException)
        {
            return Results.NotFound(new { message = "用户不存在。" });
        }
    }

    private static async Task<IResult> ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordDto dto, IUserService users, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 8)
            return Results.BadRequest(new { message = "新密码长度至少 8 位。" });

        try
        {
            await users.ChangePasswordAsync(CurrentUserId(principal), dto.CurrentPassword, dto.NewPassword, ct);
            return Results.NoContent();
        }
        catch (InvalidCredentialsException)
        {
            return Results.BadRequest(new { message = "当前密码不正确。" });
        }
        catch (UserNotFoundException)
        {
            return Results.NotFound(new { message = "用户不存在。" });
        }
    }
}
