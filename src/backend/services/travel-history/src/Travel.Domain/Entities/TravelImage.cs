namespace Travel.Domain.Entities;

/// <summary>旅行记录关联的图片元数据；文件内容由基础设施层保存。</summary>
public class TravelImage
{
    public int Id { get; set; }
    public int TravelRecordId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string OriginalPath { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}