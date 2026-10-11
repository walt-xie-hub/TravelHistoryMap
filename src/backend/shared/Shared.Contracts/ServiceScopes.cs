namespace Shared.Contracts;

/// <summary>
/// 服务间调用使用的 scope 名（ADR-0022/0024/0025）。
///
/// 这些字符串是**跨服务契约**：签发侧（identity-service 的 Client 注册）按它授予，
/// 接收侧（资源服务的端点）按它要求，发起侧（委托令牌客户端）按它请求。
/// 三处各写一遍字面量就等于埋一个"只在运行时才发现"的错别字，因此集中在这里。
///
/// 命名沿用 <c>&lt;resource&gt;:&lt;action&gt;</c>，与既有的 <c>travel:read</c> 示例一致。
/// </summary>
public static class ServiceScopes
{
    /// <summary>读取某个用户的旅行足迹聚合（ADR-0025：仅供用户委托调用）。</summary>
    public const string TravelRead = "travel:read";
}
