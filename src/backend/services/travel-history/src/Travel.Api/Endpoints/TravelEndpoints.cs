using System.Security.Claims;
using Travel.Application.Abstractions;
using Travel.Application.DTOs;

namespace Travel.Api.Endpoints;

/// <summary>
/// 旅行记录微服务 API 端点（最小 API）。表现层只依赖应用层抽象。
/// ADR-0005：整个 group 要求有效 JWT；当前用户（userId）一律取自 token 的 NameIdentifier，
/// 不信任客户端在 query/body 中声明的归属者——记录天然隔离到登录用户。
/// </summary>
public static class TravelEndpoints
{
    public static IEndpointRouteBuilder MapTravelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/travels").RequireAuthorization();

        // 当前用户的分页列表（只能看到自己的记录）；from/to 为可选到达时间窗（UTC）；按到达时间倒序。
        group.MapGet("/", async (ClaimsPrincipal principal, ITravelService svc, CancellationToken ct,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int page = 1,
            int pageSize = 10) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;
            return Results.Ok(await svc.GetPagedAsync(CurrentUserId(principal), from, to, page, pageSize, ct));
        });

        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal principal, ITravelService svc, CancellationToken ct) =>
        {
            var record = await svc.GetByIdAsync(CurrentUserId(principal), id, ct);
            return record is null ? Results.NotFound() : Results.Ok(record);
        });

        group.MapGet("/{id:int}/images", async (int id, ClaimsPrincipal principal, ITravelImageService imageService, CancellationToken ct) =>
        {
            var images = await imageService.GetImagesAsync(CurrentUserId(principal), id, ct);
            return images is null ? Results.NotFound() : Results.Ok(images);
        });

        group.MapPost("/{id:int}/images", async (
            int id,
            IFormFile file,
            ClaimsPrincipal principal,
            ITravelImageService imageService,
            CancellationToken ct) =>
        {
            await using var input = file.OpenReadStream();
            try
            {
                var image = await imageService.UploadAsync(
                    CurrentUserId(principal),
                    id,
                    new TravelImageUpload(input, file.FileName, file.ContentType, file.Length),
                    ct);
                return image is null ? Results.NotFound() : Results.Ok(image);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(new { error = "The uploaded file is not a valid image." });
            }
        }).DisableAntiforgery();

        group.MapGet("/{id:int}/images/{imageId:int}/{variant}", async (
            int id,
            int imageId,
            string variant,
            ClaimsPrincipal principal,
            ITravelImageService imageService,
            CancellationToken ct) =>
        {
            var image = await imageService.OpenAsync(CurrentUserId(principal), id, imageId, variant, ct);
            return image is null ? Results.NotFound() : Results.File(image.Content, image.ContentType);
        });

        group.MapPost("/", async (CreateTravelDto dto, ClaimsPrincipal principal, ITravelService svc, CancellationToken ct) =>
        {
            // 领域校验：离开时间不得早于到达时间（到达未结束时应省略 departedAt）
            if (dto.DepartedAt is { } departed && departed < dto.ArrivedAt)
                return Results.BadRequest(new { error = "departedAt must not be earlier than arrivedAt." });

            var created = await svc.CreateAsync(CurrentUserId(principal), dto, ct);
            return Results.Created($"/api/travels/{created.Id}", created);
        });

        group.MapPut("/{id:int}", async (int id, UpdateTravelDto dto, ClaimsPrincipal principal, ITravelService svc, CancellationToken ct) =>
        {
            if (dto.DepartedAt is { } departed && departed < dto.ArrivedAt)
                return Results.BadRequest(new { error = "departedAt must not be earlier than arrivedAt." });

            var updated = await svc.UpdateAsync(CurrentUserId(principal), id, dto, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            ClaimsPrincipal principal,
            ITravelService svc,
            ITravelImageService imageService,
            CancellationToken ct) =>
        {
            await imageService.DeleteForRecordAsync(CurrentUserId(principal), id, ct);
            var deleted = await svc.DeleteAsync(CurrentUserId(principal), id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    private static int CurrentUserId(ClaimsPrincipal principal)
        => int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

}
