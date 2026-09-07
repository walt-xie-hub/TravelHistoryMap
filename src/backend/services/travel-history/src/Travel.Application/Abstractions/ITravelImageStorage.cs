namespace Travel.Application.Abstractions;

public sealed record StoredTravelImage(
    string OriginalPath,
    string ThumbnailPath,
    long FileSize);

public interface ITravelImageStorage
{
    Task<StoredTravelImage> SaveAsync(
        Stream source,
        string extension,
        string imageId,
        CancellationToken cancellationToken = default);

    FileStream? Open(string relativePath);
    void Delete(string relativePath);
}
