namespace Travel.Domain.Entities;

/// <summary>
/// 只读分享快照（达人分享，见 ADR-0012）。
/// 创建时复制所选记录的可分享行（地点/时间/正文），生成不可猜测的 Token；
/// 通过公开端点按 Token 读取（无归属校验，只读，不暴露其他数据）。
/// </summary>
public class TravelShareSnapshot
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>分享行 JSON（不含图片/坐标细节，正文为已消毒 HTML）。</summary>
    public string RowsJson { get; set; } = "[]";
    public int RecordCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
