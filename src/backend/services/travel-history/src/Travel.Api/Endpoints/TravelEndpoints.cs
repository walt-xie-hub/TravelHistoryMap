using Travel.Application.Abstractions;
using Travel.Application.DTOs;

namespace Travel.Api.Endpoints;

/// <summary>
/// 旅行记录微服务 API 端点（最小 API）。表现层只依赖应用层抽象。
/// </summary>
public static class TravelEndpoints
{
    public static IEndpointRouteBuilder MapTravelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/travels");

        // 按用户分页列出；from/to 为可选到达时间窗（UTC）；按到达时间倒序。
        // userId 为必填查询参数（无鉴权现状下客户端显式携带，与 user-service 约定一致）。
        group.MapGet("/", async (ITravelService svc, CancellationToken ct,
            [Microsoft.AspNetCore.Mvc.FromQuery] int userId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int page = 1,
            int pageSize = 10) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;
            return Results.Ok(await svc.GetPagedAsync(userId, from, to, page, pageSize, ct));
        });

        group.MapGet("/{id:int}", async (int id, ITravelService svc, CancellationToken ct) =>
        {
            var record = await svc.GetByIdAsync(id, ct);
            return record is null ? Results.NotFound() : Results.Ok(record);
        });

        group.MapPost("/", async (CreateTravelDto dto, ITravelService svc, CancellationToken ct) =>
        {
            // 领域校验：离开时间不得早于到达时间（到达未结束时应省略 departedAt）
            if (dto.DepartedAt is { } departed && departed < dto.ArrivedAt)
                return Results.BadRequest(new { error = "departedAt must not be earlier than arrivedAt." });

            var created = await svc.CreateAsync(dto, ct);
            return Results.Created($"/api/travels/{created.Id}", created);
        });

        group.MapPut("/{id:int}", async (int id, UpdateTravelDto dto, ITravelService svc, CancellationToken ct) =>
        {
            if (dto.DepartedAt is { } departed && departed < dto.ArrivedAt)
                return Results.BadRequest(new { error = "departedAt must not be earlier than arrivedAt." });

            var updated = await svc.UpdateAsync(id, dto, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("/{id:int}", async (int id, ITravelService svc, CancellationToken ct) =>
        {
            var deleted = await svc.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}
