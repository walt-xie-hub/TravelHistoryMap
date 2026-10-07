using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Shared.Security.Tests;

/// <summary>
/// 委托令牌接收侧（ADR-0024）的两件容易出错的事：
/// <list type="number">
/// <item><c>act</c> 的解析 —— 只认 RFC 8693 的嵌套 JSON，不接受扁平串（两种形状并存＝校验变得不可预测）；</item>
/// <item>本服务标识的解析 —— 缺失或仍是占位符时**拒绝启动**，而不是静默接受任意 aud 的令牌。</item>
/// </list>
/// </summary>
public class DelegatedIdentityTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new StubConfiguration(values.ToDictionary(v => v.Key, v => v.Value));

    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void act是嵌套JSON时_解析出代理服务()
    {
        var principal = Principal(new Claim(DelegatedIdentity.ActClaimType, """{"sub":"service:user-service"}"""));

        Assert.Equal("service:user-service", principal.ActingService());
    }

    [Fact]
    public void act缺失时_返回null()
    {
        Assert.Null(Principal().ActingService());
    }

    [Fact]
    public void act是扁平串时_返回null_不允许两种形状并存()
    {
        var principal = Principal(new Claim(DelegatedIdentity.ActClaimType, "service:user-service"));

        Assert.Null(principal.ActingService());
    }

    [Fact]
    public void act是JSON但不是对象时_返回null()
    {
        var principal = Principal(new Claim(DelegatedIdentity.ActClaimType, "\"service:user-service\""));

        Assert.Null(principal.ActingService());
    }

    [Fact]
    public void act是对象但没有sub时_返回null()
    {
        var principal = Principal(new Claim(DelegatedIdentity.ActClaimType, """{"act":"service:x"}"""));

        Assert.Null(principal.ActingService());
    }

    [Fact]
    public void sub是service前缀时_取不到用户id_防止服务令牌被当成用户()
    {
        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, "service:travel-service"),
            new Claim(DelegatedIdentity.ActClaimType, """{"sub":"service:travel-service"}"""));

        Assert.Null(principal.DelegatedUserId());
    }

    [Fact]
    public void 映射后的NameIdentifier与原始sub都能取到用户id()
    {
        Assert.Equal(42, Principal(new Claim(ClaimTypes.NameIdentifier, "42")).DelegatedUserId());
        Assert.Equal(42, Principal(new Claim("sub", "42")).DelegatedUserId());
    }

    [Fact]
    public void 缺少本服务标识_拒绝启动()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DelegatedIdentity.ResolveAudience(Config()));

        Assert.Contains("ClientId", ex.Message);
    }

    [Fact]
    public void 本服务标识仍是占位符_拒绝启动()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DelegatedIdentity.ResolveAudience(
            Config(($"{ServiceIdentityOptions.SectionName}:ClientId", ServiceIdentityOptions.EnvironmentPlaceholder))));

        Assert.Contains("ClientId", ex.Message);
    }

    [Fact]
    public void 本服务标识存在时_作为委托令牌的aud()
    {
        var audience = DelegatedIdentity.ResolveAudience(
            Config(($"{ServiceIdentityOptions.SectionName}:ClientId", "travel-service")));

        Assert.Equal("travel-service", audience);
    }
}
