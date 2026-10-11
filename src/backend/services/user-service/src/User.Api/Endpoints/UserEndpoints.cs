using System.Net.Http.Headers;
using System.Security.Claims;
using Shared.Observability;
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
        group.MapGet("/me/footprint", GetFootprintAsync);
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

    /// <summary>
    /// 足迹概览（ADR-0025）：user-service 以**当前登录用户的名义**（用户委托令牌）向 travel-service
    /// 读取聚合，而不是由浏览器直接跨服务拼装。
    ///
    /// 两条刻意选择：
    /// ① 独立端点而不是塞进 <c>GET /users/me</c> —— 下游抖动时只让这张卡片失败（503），
    ///    档案查看/改密不受影响；
    /// ② 硬失败而不是降级成"空概览" —— 静默降级会让链路故障伪装成"你还没记录过旅行"。
    /// </summary>
    private static async Task<IResult> GetFootprintAsync(
        HttpContext http,
        ITravelFootprintGateway footprint,
        CancellationToken ct)
    {
        // 用户令牌只交给委托客户端去 identity 换令牌，它**不会**流向 travel-service（ADR-0024）
        var subjectToken = ExtractBearerToken(http);
        if (subjectToken is null)
            return Results.Unauthorized();

        try
        {
            return Results.Ok(await footprint.GetAsync(subjectToken, ct));
        }
        catch (FootprintUnavailableException ex)
        {
            return Results.Problem(
                title: "足迹概览暂时不可用",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>从 Authorization 头取原始 access token —— 它是换取委托令牌的**主体令牌**。</summary>
    private static string? ExtractBearerToken(HttpContext http)
        => AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization, out var header)
           && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? header.Parameter
            : null;

    private static async Task<IResult> ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordDto dto, IUserService users, HttpContext http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 8)
            return Results.BadRequest(new { message = "新密码长度至少 8 位。" });

        try
        {
            await users.ChangePasswordAsync(
                CurrentUserId(principal),
                dto.CurrentPassword,
                dto.NewPassword,
                RequestContext.ClientIp(http),
                RequestContext.UserAgent(http),
                ct);
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
