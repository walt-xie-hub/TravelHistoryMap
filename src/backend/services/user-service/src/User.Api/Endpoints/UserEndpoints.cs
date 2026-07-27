using User.Application.Abstractions;
using User.Application.DTOs;

namespace User.Api.Endpoints;

/// <summary>
/// 用户微服务 API 端点（最小 API）。表现层只依赖应用层抽象。
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapGet("/", async (IUserService svc, CancellationToken ct, int page = 1, int pageSize = 10) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;
            return Results.Ok(await svc.GetPagedAsync(page, pageSize, ct));
        });

        group.MapGet("/{id:int}", async (int id, IUserService svc, CancellationToken ct) =>
        {
            var user = await svc.GetByIdAsync(id, ct);
            return user is null ? Results.NotFound() : Results.Ok(user);
        });

        group.MapPost("/", async (CreateUserDto dto, IUserService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(dto, ct);
            return Results.Created($"/api/users/{created.Id}", created);
        });

        group.MapPut("/{id:int}", async (int id, UpdateUserDto dto, IUserService svc, CancellationToken ct) =>
        {
            var updated = await svc.UpdateAsync(id, dto, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("/{id:int}", async (int id, IUserService svc, CancellationToken ct) =>
        {
            var deleted = await svc.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}
