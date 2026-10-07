using System.Security.Claims;
using Shared.Security;

namespace Travel.Api.Endpoints;

/// <summary>
/// 内部端点（ADR-0019/0022）：一律挂在 <c>/internal/*</c>，**不得**落在网关全量转发的
/// <c>/api/*</c> 前缀下 —— 挂错前缀等于把内部能力直接对公网开放。
///
/// 与业务端点的差别：这里要求的是**用户委托令牌**（token exchange，ADR-0024）——
/// <c>sub</c> 是被代表的用户，<c>act</c> 是代理发起方，<c>aud</c> 必须就是本服务。
/// 因此既能做归属判断，又能审计“是谁以谁的名义调的”。
/// </summary>
public static class InternalEndpoints
{
    public static void MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal").RequireAuthorization(DelegatedIdentity.PolicyName);

        // 验收端点：只回显委托主体、代理服务与 scope，不返回任何业务数据。
        // 它的作用是让「token exchange 链路是否真的通了」可以被一条 curl 证明
        // （验收方式见 docs/security/service-identity.md）。
        //
        // 注意：真实业务端点必须在此之上再要求具体的 scope，例如
        //   .RequireAuthorization(DelegatedIdentity.PolicyName)
        //   .RequireClaim("scope", "travel:read")
        // 本端点不要求 scope，是因为它不暴露任何数据，且需要能被“刚开通链路”的环境直接验通。
        group.MapGet("/whoami", (ClaimsPrincipal principal) => Results.Ok(new
        {
            subject = principal.DelegatedUserId(),
            actingService = principal.ActingService(),
            scopes = (principal.FindFirstValue("scope") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries),
        }));
    }
}
