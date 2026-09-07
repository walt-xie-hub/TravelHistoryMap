using Travel.Application.Abstractions;
using Travel.Application.DTOs;
using Travel.Domain.Abstractions;
using Travel.Domain.Entities;

namespace Travel.Application.Services;

public sealed class TravelImageService(
    ITravelRepository repository,
    ITravelImageStorage storage) : ITravelImageService
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public async Task<IReadOnlyList<TravelImageDto>?> GetImagesAsync(
        int userId,
        int travelRecordId,
        CancellationToken cancellationToken = default)
    {
        if (!await OwnsRecordAsync(userId, travelRecordId, cancellationToken))
            return null;

        var images = await repository.GetImagesAsync(travelRecordId, cancellationToken);
        return images.Select(ToDto).ToList();
    }

    public async Task<TravelImageDto?> UploadAsync(
        int userId,
        int travelRecordId,
        TravelImageUpload upload,
        CancellationToken cancellationToken = default)
    {
        if (!await OwnsRecordAsync(userId, travelRecordId, cancellationToken))
            return null;
        if (upload.Length <= 0 || upload.Length > MaxFileSize
            || !AllowedContentTypes.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Only JPEG, PNG, or WebP images up to 10 MB are allowed.", nameof(upload));

        var existingImages = await repository.GetImagesAsync(travelRecordId, cancellationToken);
        if (existingImages.Count >= 9)
            throw new InvalidOperationException("A travel record can contain at most 9 images.");

        var extension = upload.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => ".webp",
        };
        var imageId = Guid.NewGuid().ToString("N");
        var stored = await storage.SaveAsync(upload.Content, extension, imageId, cancellationToken);

        try
        {
            var image = await repository.AddImageAsync(new TravelImage
            {
                TravelRecordId = travelRecordId,
                OriginalFileName = Path.GetFileName(upload.FileName),
                ContentType = upload.ContentType,
                FileSize = stored.FileSize,
                OriginalPath = stored.OriginalPath,
                ThumbnailPath = stored.ThumbnailPath,
            }, cancellationToken);
            return ToDto(image);
        }
        catch
        {
            storage.Delete(stored.OriginalPath);
            storage.Delete(stored.ThumbnailPath);
            throw;
        }
    }

    public async Task<TravelImageFile?> OpenAsync(
        int userId,
        int travelRecordId,
        int imageId,
        string variant,
        CancellationToken cancellationToken = default)
    {
        var record = await repository.GetByIdAsync(travelRecordId, cancellationToken);
        var image = await repository.GetImageAsync(imageId, cancellationToken);
        if (record is null || image is null || record.UserId != userId || image.TravelRecordId != travelRecordId)
            return null;

        var path = variant.Equals("thumbnail", StringComparison.OrdinalIgnoreCase)
            ? image.ThumbnailPath
            : variant.Equals("original", StringComparison.OrdinalIgnoreCase) ? image.OriginalPath : null;
        if (path is null)
            return null;

        var stream = storage.Open(path);
        return stream is null
            ? null
            : new TravelImageFile(stream, variant.Equals("thumbnail", StringComparison.OrdinalIgnoreCase)
                ? "image/webp"
                : image.ContentType);
    }

    public async Task DeleteForRecordAsync(
        int userId,
        int travelRecordId,
        CancellationToken cancellationToken = default)
    {
        if (!await OwnsRecordAsync(userId, travelRecordId, cancellationToken))
            return;

        var images = await repository.GetImagesAsync(travelRecordId, cancellationToken);
        foreach (var image in images)
        {
            try
            {
                storage.Delete(image.OriginalPath);
                storage.Delete(image.ThumbnailPath);
            }
            catch (IOException)
            {
                // Database deletion remains authoritative when media cleanup fails.
            }
        }
    }

    private async Task<bool> OwnsRecordAsync(int userId, int travelRecordId, CancellationToken cancellationToken)
    {
        var record = await repository.GetByIdAsync(travelRecordId, cancellationToken);
        return record is not null && record.UserId == userId;
    }

    private static TravelImageDto ToDto(TravelImage image) => new(
        image.Id,
        image.OriginalFileName,
        image.ContentType,
        image.FileSize,
        $"/api/travels/{image.TravelRecordId}/images/{image.Id}/thumbnail",
        $"/api/travels/{image.TravelRecordId}/images/{image.Id}/original");
}
