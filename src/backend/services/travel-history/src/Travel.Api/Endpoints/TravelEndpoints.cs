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

        // 回收站（ADR-0013）：已软删除记录，按删除时间倒序
        group.MapGet("/trash", async (ClaimsPrincipal principal, ITravelService svc, CancellationToken ct,
            int page = 1,
            int pageSize = 10) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;
            return Results.Ok(await svc.GetTrashAsync(CurrentUserId(principal), page, pageSize, ct));
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

        // 删除单张图片：清理媒体文件并删除数据库行；非本人/不存在一律 404（不泄露存在性）。
        group.MapDelete("/{id:int}/images/{imageId:int}", async (
            int id,
            int imageId,
            ClaimsPrincipal principal,
            ITravelImageService imageService,
            CancellationToken ct) =>
        {
            var deleted = await imageService.DeleteAsync(CurrentUserId(principal), id, imageId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/", async (CreateTravelDto dto, ClaimsPrincipal principal, ITravelService svc, CancellationToken ct) =>
        {
            // 领域校验：离开时间不得早于到达时间（到达未结束时应省略 departedAt）
            if (dto.DepartedAt is { } departed && departed < dto.ArrivedAt)
                return Results.BadRequest(new { error = "departedAt must not be earlier than arrivedAt." });

            try
            {
                var created = await svc.CreateAsync(CurrentUserId(principal), dto, ct);
                return Results.Created($"/api/travels/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                // 正文过长等字段校验失败（如超过 4000 可见字符）→ 400
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapPut("/{id:int}", async (int id, UpdateTravelDto dto, ClaimsPrincipal principal, ITravelService svc, CancellationToken ct) =>
        {
            if (dto.DepartedAt is { } departed && departed < dto.ArrivedAt)
                return Results.BadRequest(new { error = "departedAt must not be earlier than arrivedAt." });

            try
            {
                var updated = await svc.UpdateAsync(CurrentUserId(principal), id, dto, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 移入回收站（软删除，ADR-0013）：仅置 DeletedAt，不清理图片，可恢复
        group.MapDelete("/{id:int}", async (
            int id,
            ClaimsPrincipal principal,
            ITravelService svc,
            CancellationToken ct) =>
        {
            var moved = await svc.DeleteAsync(CurrentUserId(principal), id, ct);
            return moved ? Results.NoContent() : Results.NotFound();
        });

        // 从回收站恢复
        group.MapPost("/{id:int}/restore", async (
            int id,
            ClaimsPrincipal principal,
            ITravelService svc,
            CancellationToken ct) =>
        {
            var restored = await svc.RestoreAsync(CurrentUserId(principal), id, ct);
            return restored ? Results.NoContent() : Results.NotFound();
        });

        // 彻底删除：先清理媒体文件与图片行，再物理删除记录
        group.MapDelete("/{id:int}/permanent", async (
            int id,
            ClaimsPrincipal principal,
            ITravelService svc,
            ITravelImageService imageService,
            CancellationToken ct) =>
        {
            await imageService.DeleteForRecordAsync(CurrentUserId(principal), id, ct);
            var deleted = await svc.DeletePermanentlyAsync(CurrentUserId(principal), id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        // 创建只读分享快照（登录用户）；不可猜测 token，供 /s/{token} 只读页使用
        group.MapPost("/share", async (
            ShareCreateRequest request,
            ClaimsPrincipal principal,
            ITravelService svc,
            CancellationToken ct) =>
        {
            try
            {
                var token = await svc.CreateShareAsync(CurrentUserId(principal), request?.TravelIds ?? [], ct);
                return token is null
                    ? Results.BadRequest(new { error = "所选记录不可分享。" })
                    : Results.Ok(new { token, url = $"/s/{token}" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 公开只读分享页数据（无鉴权）：仅凭不可猜测 token 返回快照内容，无任何写操作
        app.MapGet("/api/share-snapshots/{token}", async (
            string token,
            ITravelService svc,
            CancellationToken ct) =>
        {
            var dto = await svc.GetShareSnapshotAsync(token, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        return app;
    }

    private static int CurrentUserId(ClaimsPrincipal principal)
        => int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

}
