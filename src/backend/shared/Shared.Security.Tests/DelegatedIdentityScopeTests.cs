using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Shared.Security;
using Xunit;

namespace Shared.Security.Tests;

/// <summary>
/// scope 判断与"委托 + scope"策略（ADR-0024/0025）。
///
/// 这组断言存在的理由：签发侧把请求到的 scope 拼成**一个**空格分隔的声明
/// （形如 <c>"travel:read user:read"</c>），而 <c>RequireClaim("scope", ...)</c> 做的是整串精确匹配 ——
/// 用它判 scope 会在"令牌恰好只请求一个 scope"时通过、在请求两个时**静默 403**。
/// </summary>
public class DelegatedIdentityScopeTests
{
    private static ClaimsPrincipal Principal(params string[] scopeClaims)
        => new(new ClaimsIdentity(
            scopeClaims.Select(s => new Claim(DelegatedIdentity.ScopeClaimType, s)),
            authenticationType: "test"));

    [Fact]
    public void HasScope_SingleScope_IsTrue()
        => Assert.True(Principal("travel:read").HasScope("travel:read"));

    [Fact]
    public void HasScope_MultipleScopesInOneClaim_IsTrueForEach()
    {
        var principal = Principal("travel:read user:read");

        Assert.True(principal.HasScope("travel:read"));
        Assert.True(principal.HasScope("user:read"));
    }

    [Fact]
    public void HasScope_MultipleClaims_IsTrueForEach()
    {
        var principal = Principal("travel:read", "user:read");

        Assert.True(principal.HasScope("user:read"));
    }

    [Fact]
    public void HasScope_OtherScope_IsFalse()
        => Assert.False(Principal("travel:write").HasScope("travel:read"));

    [Fact]
    public void HasScope_NoClaim_IsFalse()
        => Assert.False(new ClaimsPrincipal(new ClaimsIdentity()).HasScope("travel:read"));

    [Fact]
    public void HasScope_IsNotAPrefixMatch()
        => Assert.False(Principal("travel:read:everything").HasScope("travel:read"));

    [Fact]
    public void RequireDelegatedScope_RequiresSchemeAuthenticationActAndScope()
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireDelegatedScope("travel:read")
            .Build();

        // 与 DelegatedIdentity 策略同一套前置条件：委托方案 + 已认证 + 必须有 act
        Assert.Contains(DelegatedIdentity.Scheme, policy.AuthenticationSchemes);
        Assert.Contains(policy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(
            policy.Requirements,
            r => r is ClaimsAuthorizationRequirement claim && claim.ClaimType == DelegatedIdentity.ActClaimType);
        // 再加一条 scope 断言（用 HasScope，而不是整串匹配）
        Assert.Contains(policy.Requirements, r => r is AssertionRequirement);
    }
}
