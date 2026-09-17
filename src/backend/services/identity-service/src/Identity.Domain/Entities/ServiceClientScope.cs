namespace Identity.Domain.Entities;

/// <summary>
/// Client 被允许的 scope。默认拒绝：起步阶段一个 scope 都不预置（ADR-0022），
/// 新增跨服务能力时按需开并记录在案。
/// </summary>
public class ServiceClientScope
{
    public string ClientId { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public ServiceClient? Client { get; set; }
}
