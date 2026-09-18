using Microsoft.AspNetCore.Http;

namespace Shared.Observability;

/// <summary>
/// 审计用的请求元数据。原先是 identity-service 的私有实现，改密也要审计（user-service）之后
/// 提上来共用 —— 来源 IP 该怎么取属于**全仓库统一的信任假设**，不该各写一份。
///
/// 来源 IP 取 <c>X-Forwarded-For</c> 的**最右一段**：入口是网关/边缘，它会把真实客户端地址
/// 追加到末尾，而左侧的值来自客户端自身、可以伪造（同 docs/security/network-and-edge.md 的限流口径）。
/// 没有 XFF 时回落 <c>RemoteIpAddress</c>。
/// </summary>
public static class RequestContext
{
    public static string? ClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var last = forwarded
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();

            if (!string.IsNullOrWhiteSpace(last))
                return last;
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    public static string? UserAgent(HttpContext context)
    {
        var userAgent = context.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;
    }
}
