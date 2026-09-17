using Microsoft.Extensions.Configuration;
using Xunit;

namespace Shared.Security.Tests;

/// <summary>
/// 共存窗口（ADR-0021）的解析与门控。
///
/// 这些断言就是「窗口会自己关掉」这句话的证据：默认关闭、开启必须给到期时间、
/// 上限 7 天、过期即拒绝启动。没有它们，这里就只是一段没人验证过的配置读取代码。
/// </summary>
public class TravelMapJwtTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new StubConfiguration(values.ToDictionary(v => v.Key, v => v.Value));

    [Fact]
    public void 未显式开启时默认关闭_即使配了旧的_Jwt_Key()
    {
        var window = TravelMapJwt.ResolveLegacyWindow(Config(("Jwt:Key", "old-key")), Now);

        Assert.False(window.Enabled);
        Assert.Null(window.Until);
    }

    [Fact]
    public void 显式开启但没给到期时间_拒绝启动()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            TravelMapJwt.ResolveLegacyWindow(
                Config(
                    (TravelMapJwt.AllowLegacyHs256ConfigKey, "true"),
                    ("Jwt:Key", "old-key")),
                Now);
        });

        Assert.Contains(TravelMapJwt.LegacyUntilConfigKey, ex.Message);
    }

    [Fact]
    public void 到期时间无法解析_拒绝启动()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            TravelMapJwt.ResolveLegacyWindow(
                Config(
                    (TravelMapJwt.AllowLegacyHs256ConfigKey, "true"),
                    (TravelMapJwt.LegacyUntilConfigKey, "下周再说"),
                    ("Jwt:Key", "old-key")),
                Now);
        });

        Assert.Contains("ISO-8601", ex.Message);
    }

    [Fact]
    public void 窗口已过期_拒绝启动并提示清理配置()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            TravelMapJwt.ResolveLegacyWindow(
                Config(
                    (TravelMapJwt.AllowLegacyHs256ConfigKey, "true"),
                    (TravelMapJwt.LegacyUntilConfigKey, "2026-09-17T00:00:00Z"),
                    ("Jwt:Key", "old-key")),
                Now);
        });

        Assert.Contains("已过期", ex.Message);
    }

    [Fact]
    public void 窗口超过七天上限_拒绝启动()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            // Now + 8 天：窗口不能比 refresh token 活得更久
            TravelMapJwt.ResolveLegacyWindow(
                Config(
                    (TravelMapJwt.AllowLegacyHs256ConfigKey, "true"),
                    (TravelMapJwt.LegacyUntilConfigKey, "2026-09-26T00:00:00Z"),
                    ("Jwt:Key", "old-key")),
                Now);
        });

        Assert.Contains("7 天", ex.Message);
    }

    [Fact]
    public void 开启了却没给密钥_拒绝启动()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            TravelMapJwt.ResolveLegacyWindow(
                Config(
                    (TravelMapJwt.AllowLegacyHs256ConfigKey, "true"),
                    (TravelMapJwt.LegacyUntilConfigKey, "2026-09-20T00:00:00Z")),
                Now);
        });

        Assert.Contains("Jwt:Key", ex.Message);
    }

    [Fact]
    public void 合法窗口_开启并带上密钥与旧_issuer()
    {
        var window = TravelMapJwt.ResolveLegacyWindow(
            Config(
                (TravelMapJwt.AllowLegacyHs256ConfigKey, "true"),
                (TravelMapJwt.LegacyUntilConfigKey, "2026-09-20T00:00:00Z"),
                ("Jwt:Key", "old-key")),
            Now);

        Assert.True(window.Enabled);
        Assert.Equal("old-key", window.Key);
        Assert.Equal("travel-map", window.Issuer);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), window.Until);
    }

    [Fact]
    public void 旧_issuer_可以覆盖_默认是_travel_map()
    {
        var window = TravelMapJwt.ResolveLegacyWindow(
            Config(
                (TravelMapJwt.AllowLegacyHs256ConfigKey, "true"),
                (TravelMapJwt.LegacyUntilConfigKey, "2026-09-20T00:00:00Z"),
                ("Jwt:Issuer", "legacy-issuer"),
                ("Jwt:Key", "old-key")),
            Now);

        Assert.Equal("legacy-issuer", window.Issuer);
    }

    [Fact]
    public void 上限就是七天_改大它会让这条测试失败()
    {
        Assert.Equal(TimeSpan.FromDays(7), TravelMapJwt.MaxLegacyWindow);
    }
}
