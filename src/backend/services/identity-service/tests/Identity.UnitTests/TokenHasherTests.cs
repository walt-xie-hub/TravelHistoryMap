using Identity.Application;
using Xunit;

namespace Identity.UnitTests;

public class TokenHasherTests
{
    [Fact]
    public void NewToken_IsUrlSafeAndLongEnough()
    {
        var token = TokenHasher.NewToken();

        // 32 字节 base64url ≈ 43 字符；不允许出现 + / = 这类需要再转义的字符
        Assert.True(token.Length >= 40, $"令牌太短：{token.Length}");
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void NewToken_IsUniquePerCall()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => TokenHasher.NewToken()).ToHashSet();

        Assert.Equal(200, tokens.Count);
    }

    [Fact]
    public void Hash_IsStableLowercaseHex()
    {
        var first = TokenHasher.Hash("abc");
        var second = TokenHasher.Hash("abc");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(first.ToLowerInvariant(), first);
        Assert.NotEqual(first, TokenHasher.Hash("abd"));   // 雪崩：改一个字符哈希全变
    }
}
