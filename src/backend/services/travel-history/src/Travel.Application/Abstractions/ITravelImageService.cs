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

    /// <summary>
    /// 删除某条记录下的单张图片：清理原图/缩略图媒体文件并删除数据库行。
    /// 记录非本人所有、记录/图片不存在、或图片不属于该记录时返回 false（表现层按 404 处理，不泄露存在性）。
    /// </summary>
    Task<bool> DeleteAsync(
        int userId,
        int travelRecordId,
        int imageId,
        CancellationToken cancellationToken = default);

    Task DeleteForRecordAsync(
        int userId,
        int travelRecordId,
        CancellationToken cancellationToken = default);
}
