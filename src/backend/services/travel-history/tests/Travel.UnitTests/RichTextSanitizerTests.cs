using Xunit;
using Travel.Application.Sanitization;

namespace Travel.UnitTests;

/// <summary>
/// RichTextSanitizer（ADR-0009）单元测试：富文本正文入库前的 allow-list 消毒与
/// “可见字符 ≤4000”的长度口径。
/// </summary>
public class RichTextSanitizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Prepare_NullOrWhitespace_ReturnsNull(string? html)
    {
        Assert.Null(RichTextSanitizer.Prepare(html));
    }

    [Theory]
    [InlineData("<p></p>")]
    [InlineData("<p> </p>")]
    [InlineData("<p><br></p>")]
    public void Prepare_EmptyParagraphs_ReturnsNull(string html)
    {
        Assert.Null(RichTextSanitizer.Prepare(html));
    }

    [Fact]
    public void Prepare_StripsScriptAndEventHandlers_KeepsAllowedFormatting()
    {
        var html = "<p>你好 <strong>世界</strong></p><ul><li>一</li></ul>"
                   + "<script>alert(1)</script><div onclick=\"x()\" style=\"color:red\">保留文字</div>";

        var result = RichTextSanitizer.Prepare(html);

        Assert.NotNull(result);
        Assert.Contains("<p>", result);
        Assert.Contains("<strong>", result);
        Assert.Contains("<ul>", result);
        Assert.Contains("<li>", result);
        // 危险内容整块移除
        Assert.DoesNotContain("script", result);
        Assert.DoesNotContain("alert", result);
        Assert.DoesNotContain("onclick", result);
        Assert.DoesNotContain("style", result);
        // 未在白名单的标签（div 等）整块连同内容移除（更严格，只保留白名单标签）
        Assert.DoesNotContain("保留文字", result);
    }

    [Fact]
    public void Prepare_StripsJavascriptHref()
    {
        var html = "<p><a href=\"javascript:alert(1)\">链接</a></p>";

        var result = RichTextSanitizer.Prepare(html);

        Assert.NotNull(result);
        Assert.DoesNotContain("javascript:", result);
        Assert.Contains("链接", result);
    }

    [Fact]
    public void Prepare_KeepsSafeHttpHref()
    {
        var html = "<p><a href=\"https://example.com/x\">官网</a></p>";

        var result = RichTextSanitizer.Prepare(html);

        Assert.NotNull(result);
        Assert.Contains("https://example.com/x", result);
        Assert.Contains("<a ", result);
    }

    [Fact]
    public void Prepare_RawInputTooLong_Throws()
    {
        var tooLong = new string('a', 20_001);
        Assert.Throws<ArgumentException>(() => RichTextSanitizer.Prepare(tooLong));
    }

    [Fact]
    public void VisibleCharacterCount_CountsVisibleTextIgnoringMarkup()
    {
        Assert.Equal(5, RichTextSanitizer.VisibleCharacterCount("<p>你好 <strong>世界</strong></p>"));
        Assert.Equal(0, RichTextSanitizer.VisibleCharacterCount("<p><br></p>"));
        // 两段之间（标签被空白折叠成一个空格）：第一行 + 空格 + 第二行
        Assert.Equal(7, RichTextSanitizer.VisibleCharacterCount("<p>第一行</p><p>第二行</p>"));
        // 实体解码后计数
        Assert.Equal(3, RichTextSanitizer.VisibleCharacterCount("<p>a&amp;b</p>"));
    }

    [Fact]
    public void Prepare_VisibleTextLongerThanLimit_StillReturnsHtmlButCountExposed()
    {
        // Prepare 本身不设上限，长度校验在 TravelService.PrepareDescription 里做
        var longHtml = $"<p>{new string('字', 5000)}</p>";
        var result = RichTextSanitizer.Prepare(longHtml);
        Assert.NotNull(result);
        Assert.Equal(5000, RichTextSanitizer.VisibleCharacterCount(result));
    }

    [Fact]
    public void Prepare_AllowsProfileCssProps_StripsOthers()
    {
        // Arrange（能力画像内联样式：颜色/字体族/字号允许；position 等被剥）
        var html = "<p>Hi <span style=\"color:red;font-size:14px;font-family:SimSun;position:fixed\">字</span></p>";

        var result = RichTextSanitizer.Prepare(html);

        Assert.NotNull(result);
        Assert.Contains("<span", result);
        Assert.Contains("color", result);
        Assert.Contains("font-size", result);
        Assert.Contains("font-family", result);
        Assert.DoesNotContain("position", result);
    }

    [Fact]
    public void Prepare_KeepsUnderlineStrikeHighlightMarks()
    {
        // Arrange（全功能富文本新增标记：下划线/删除线/高亮）
        var html = "<p><u>下划线</u> <s>删除线</s> <mark style=\"background-color:yellow\">高亮</mark></p>";

        var result = RichTextSanitizer.Prepare(html);

        Assert.NotNull(result);
        Assert.Contains("<u>", result);
        Assert.Contains("<s>", result);
        Assert.Contains("<mark", result);
        Assert.Contains("background-color", result);
    }

    [Fact]
    public void Prepare_KeepsTextAlignStyleOnBlock()
    {
        // Arrange（块级对齐走 text-align）
        var html = "<p style=\"text-align:center\">居中</p><h2 style=\"text-align:right\">右</h2>";

        var result = RichTextSanitizer.Prepare(html);

        Assert.NotNull(result);
        Assert.Contains("text-align", result);
        Assert.Contains("<h2", result);
    }
}
