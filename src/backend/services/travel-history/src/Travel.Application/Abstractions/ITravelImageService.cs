using Travel.Application.DTOs;

namespace Travel.Application.Abstractions;

public sealed record TravelImageUpload(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);

public sealed record TravelImageFile(Stream Content, string ContentType);

public interface ITravelImageService
{
    Task<IReadOnlyList<TravelImageDto>?> GetImagesAsync(
        int userId,
        int travelRecordId,
        CancellationToken cancellationToken = default);

    Task<TravelImageDto?> UploadAsync(
        int userId,
        int travelRecordId,
        TravelImageUpload upload,
        CancellationToken cancellationToken = default);

    Task<TravelImageFile?> OpenAsync(
        int userId,
        int travelRecordId,
        int imageId,
        string variant,
        CancellationToken cancellationToken = default);

    Task DeleteForRecordAsync(
        int userId,
        int travelRecordId,
        CancellationToken cancellationToken = default);
}
