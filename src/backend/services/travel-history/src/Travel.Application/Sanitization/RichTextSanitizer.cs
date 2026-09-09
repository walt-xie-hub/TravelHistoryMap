using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;

namespace Travel.Application.Sanitization;

/// <summary>
/// Travel detail（正文）的消毒与长度口径（ADR-0009）。
/// 正文由前端富文本编辑器（TipTap）产出 HTML，入库前做标签/属性 allow-list 消毒以防 XSS；
/// 长度按「去掉标签、HTML 实体解码、空白折叠后的可见字符」≤4000 校验，
/// 与前端字数统计口径一致（HTML/Markdown 源码可略长，见 ADR-0008/0009 关于扩列的决定）。
/// </summary>
public static partial class RichTextSanitizer
{
    public const int MaxVisibleCharacters = 4000;

    /// <summary>编辑器的 schema 子集：允许的排版标签（不含图片/代码/脚本等）。</summary>
    private static readonly string[] AllowedTags =
        ["p", "br", "strong", "em", "u", "s", "span", "mark", "h2", "h3", "ul", "ol", "li", "blockquote", "a"];

    /// <summary>行内样式允许的 CSS 属性（颜色/字体/字号/对齐/高亮），其余一律剥离。</summary>
    private static readonly string[] AllowedCssProperties =
        ["color", "background-color", "font-family", "font-size", "text-align"];

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in AllowedTags)
            sanitizer.AllowedTags.Add(tag);

        // 只保留 <a href> 与行内 style；其余属性（class/id/事件/on*）一律剥离。
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("style");

        // style 只允许能力画像内的 CSS 属性（颜色/背景/字体族/字号/对齐）；其余（position 等）被 Ganss 剥离。
        sanitizer.AllowedCssProperties.Clear();
        foreach (var css in AllowedCssProperties)
            sanitizer.AllowedCssProperties.Add(css);

        // href 只允许 http/https/mailto；javascript:、data: 等一律被剥离。
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");

        // 安全敏感标签即使被白名单放行也必须连同内容一起移除
        //（HtmlSanitizer 默认对 script/style/iframe/object/embed 等整块移除）。
        return sanitizer;
    }

    /// <summary>
    /// 准备入库的正文：空/空白/纯空段落返回 null；否则返回消毒后的 HTML 片段。
    /// 原始输入过长直接抛 ArgumentException（表现层转 400）。
    /// </summary>
    public static string? Prepare(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        if (html.Length > 20_000)
            throw new ArgumentException("Travel detail is too long.");

        var cleaned = Sanitizer.Sanitize(html).Trim();
        if (string.IsNullOrWhiteSpace(cleaned) || VisibleCharacterCount(cleaned) == 0)
            return null;
        return cleaned;
    }

    /// <summary>可见字符数：去标签 → HTML 解码 → 连续空白折叠为一个空格 → 计数。空串返回 0。</summary>
    public static int VisibleCharacterCount(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 0;
        var withoutTags = TagPattern().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsed = WhitespacePattern().Replace(decoded, " ").Trim();
        return collapsed.Length;
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.Singleline)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
