using Microsoft.Extensions.Caching.Memory;
using User.Api.Security;
using Xunit;

namespace User.UnitTests;

public class CaptchaServiceTests
{
    private static CaptchaService CreateService() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void Create_ReturnsIdAndPngBase64()
    {
        var captchas = CreateService();

        var result = captchas.Create();

        Assert.False(string.IsNullOrWhiteSpace(result.Id));
        Assert.NotNull(result.ImageBase64);
        // PNG magic bytes: Base64 of "\x89PNG\r\n\x1a\n"
        Assert.StartsWith("iVBORw0KGgo", result.ImageBase64);
    }

    [Fact]
    public void Validate_WithCorrectAnswer_ReturnsTrue()
    {
        var captchas = CreateService();
        var result = captchas.Create();

        // 缓存里只有服务知道答案；对同一实例调用 Validate 需与生成一致。
        // 此处通过 Create 后再独立注入同答案不可行（答案不对外），
        // 因此直接构造一次校验，验证大小写不敏感路径使用自生成答案不可用——改为验证：
        Assert.True(captchas.Validate(result.Id, GetAnswerFromService(captchas, result.Id)));
    }

    [Fact]
    public void Validate_IsCaseInsensitive()
    {
        var captchas = CreateService();

        // 验证码一次性：小写/大写各用独立凭证验证
        var lower = captchas.Create();
        Assert.True(captchas.Validate(lower.Id, GetAnswerFromService(captchas, lower.Id).ToLowerInvariant()));

        var upper = captchas.Create();
        Assert.True(captchas.Validate(upper.Id, GetAnswerFromService(captchas, upper.Id).ToUpperInvariant()));
    }

    [Fact]
    public void Validate_WrongAnswer_ReturnsFalse()
    {
        var captchas = CreateService();
        var result = captchas.Create();

        Assert.False(captchas.Validate(result.Id, "0000"));
    }

    [Fact]
    public void Validate_IsSingleUse_SecondAttemptFails()
    {
        var captchas = CreateService();
        var result = captchas.Create();
        var answer = GetAnswerFromService(captchas, result.Id);

        Assert.True(captchas.Validate(result.Id, answer));
        Assert.False(captchas.Validate(result.Id, answer)); // 重放被拒
    }

    [Fact]
    public void Validate_UnknownOrEmpty_ReturnsFalse()
    {
        var captchas = CreateService();

        Assert.False(captchas.Validate("no-such-id", "1234"));
        Assert.False(captchas.Validate(null, "1234"));
        Assert.False(captchas.Validate("some-id", null));
        Assert.False(captchas.Validate("", ""));
    }

    /// <summary>测试辅助：读取仍缓存在内存中的答案（模拟“同一服务实例已知答案”）。</summary>
    private static string GetAnswerFromService(CaptchaService service, string id)
    {
        // CaptchaService 将答案缓存在 IMemoryCache；按类型查找私有字段，避免依赖编译器生成名。
        var field = typeof(CaptchaService)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .First(f => typeof(IMemoryCache).IsAssignableFrom(f.FieldType));
        var cache = (IMemoryCache)field.GetValue(service)!;
        cache.TryGetValue($"captcha:{id}", out var raw);
        return (string)raw!;
    }
}
