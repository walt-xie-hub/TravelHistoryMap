using SkiaSharp;
using Microsoft.Extensions.Configuration;
using Travel.Application.Abstractions;

namespace Travel.Infrastructure.Media;

public sealed class TravelImageStorage(IConfiguration configuration) : ITravelImageStorage
{
    private const int ThumbnailMaxEdge = 320;
    private readonly string root = Path.GetFullPath(configuration["TravelMedia:RootPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "media"));

    public async Task<StoredTravelImage> SaveAsync(Stream source, string extension, string imageId, CancellationToken ct = default)
    {
        Directory.CreateDirectory(root);
        var originalRelative = $"original/{imageId}{extension}";
        var thumbnailRelative = $"thumbnail/{imageId}.webp";
        var originalFull = Path.Combine(root, originalRelative.Replace('/', Path.DirectorySeparatorChar));
        var thumbnailFull = Path.Combine(root, thumbnailRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(originalFull)!);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailFull)!);

        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        using var inputStream = new MemoryStream(bytes, writable: false);
        using var input = SKBitmap.Decode(inputStream)
            ?? throw new InvalidDataException("The uploaded file is not a valid image.");
        await File.WriteAllBytesAsync(originalFull, bytes, ct);
        var scale = Math.Min(1f, ThumbnailMaxEdge / (float)Math.Max(input.Width, input.Height));
        var width = Math.Max(1, (int)Math.Round(input.Width * scale));
        var height = Math.Max(1, (int)Math.Round(input.Height * scale));
        using var resized = input.Resize(new SKImageInfo(width, height), SKFilterQuality.Medium)
            ?? throw new InvalidDataException("The image could not be resized.");
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 82)
            ?? throw new InvalidDataException("The thumbnail could not be encoded.");
        await File.WriteAllBytesAsync(thumbnailFull, encoded.ToArray(), ct);
        return new StoredTravelImage(originalRelative, thumbnailRelative, new FileInfo(originalFull).Length);
    }

    public FileStream? Open(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath)) return null;
        return File.OpenRead(fullPath);
    }

    public void Delete(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            File.Delete(fullPath);
    }
}