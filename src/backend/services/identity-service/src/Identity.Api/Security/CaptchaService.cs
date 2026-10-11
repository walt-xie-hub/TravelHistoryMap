using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace Identity.Api.Security;

/// <summary>验证码结果：id 供登录时回传，ImageBase64 为 PNG 图片的 Base64。</summary>
public sealed record CaptchaResult(string Id, string ImageBase64);

/// <summary>
/// 图片验证码服务（数字验证码）。
/// 图形采用内置 5x7 像素字模直接绘制，不依赖系统字体/字体文件，
/// 因此 Linux 容器与本地 Windows 渲染效果一致。
/// 答案以 id 为键缓存（一次性、5 分钟过期），验证后立即销毁，防重放。
///
/// 自 user-service 迁入（ADR-0020：认证归 identity-service）；随机数改用
/// <see cref="RandomNumberGenerator"/> —— 原来的 <c>Random.Shared</c> 不是密码学安全的。
///
/// **可读性调整（2026-10-07）**：验证码的职责是拦住脚本，不是拦住人。
/// 旧参数（5px 点、字距 10px、旋转 ±24°、130 噪点、4 条干扰线）下，
/// 2 与 7 、8 与 9 这类字形在旋转后会互相混淆 —— 实测把图放大 6 倍、不限时间读，
/// 仍会读错，用户只能反复重来。现在：点放大到 8px、旋转收到 ±10°、字距拉到 18px
/// （确保旋转后的外接框不重叠）、干扰线减到 2 条。
/// 抗脚本能力仍靠“随机 4 位 + 噪点 + 干扰线 + 一次性 5 分钟”，而不是靠让人看不清。
/// </summary>
public sealed class CaptchaService(IMemoryCache cache)
{
    private const string CodeChars = "23456789"; // 去掉 0/1，避免混淆
    private const int CodeLength = 4;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const string CachePrefix = "captcha:";

    // 数字 0-9 的 5x7 点阵，每行字符串从左到右为像素列（'1' = 实心）。
    // 字模取自公共领域 LED/LCD 风格点阵，可自由使用。
    private static readonly string[][] DigitMaps =
    [
        ["01110", "10001", "10011", "10101", "11001", "10001", "01110"], // 0
        ["00100", "01100", "00100", "00100", "00100", "00100", "01110"], // 1
        ["01110", "10001", "00001", "00010", "00100", "01000", "11111"], // 2
        ["11110", "00001", "00001", "01110", "00001", "00001", "11110"], // 3
        ["00010", "00110", "01010", "10010", "11111", "00010", "00010"], // 4
        ["11111", "10000", "11110", "00001", "00001", "10001", "01110"], // 5
        ["00110", "01000", "10000", "11110", "10001", "10001", "01110"], // 6
        ["11111", "00001", "00010", "00100", "01000", "01000", "01000"], // 7
        ["01110", "10001", "10001", "01110", "10001", "10001", "01110"], // 8
        ["01110", "10001", "10001", "01111", "00001", "00010", "01100"], // 9
    ];

    /// <summary>生成一个新的验证码（随机 4 位数字 PNG 图），并缓存答案。</summary>
    public CaptchaResult Create()
    {
        var code = GenerateCode();
        var id = Guid.NewGuid().ToString("N");
        cache.Set($"{CachePrefix}{id}", code, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
        });
        return new CaptchaResult(id, RenderPng(code));
    }

    /// <summary>
    /// 校验验证码：无论正确与否都会销毁缓存（一次性）。
    /// 校验通过返回 true，否则 false。
    /// </summary>
    public bool Validate(string? id, string? answer)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var key = $"{CachePrefix}{id}";
        var expected = cache.Get<string>(key);
        cache.Remove(key); // 一次性：验证后立即作废，防止重放

        if (expected is null)
        {
            return false;
        }

        return string.Equals(expected, answer.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateCode()
    {
        Span<char> buf = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            buf[i] = CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)];
        }
        return new string(buf);
    }

    // ---- 图形渲染 ----
    // 注：下面几个常量共同决定“能不能读出来”，改动前先看 Create/Validate 的实测证据（类注释）。
    private const int Cell = 8;          // 每个像素点放大为 8x8 矩形（原 5：笔画太细，旋转后难辨）
    private const int GlyphW = 5 * Cell; // 字模宽 = 40
    private const int GlyphH = 7 * Cell; // 字模高 = 56
    private const int Gap = 18;          // 字间距（必须容得下旋转后的外接框：步进 58 > 外接宽 ~49）
    private const int PadX = 16;         // 左右留白（容纳旋转溢出）
    private const int PadY = 18;         // 上下留白（同上）

    /// <summary>最大旋转角（度）。±24° 会让 2/7、8/9 这类字形互相混淆；±10° 足以对抗模板匹配。</summary>
    private const int MaxRotationDegrees = 10;

    private const int ImageW = CodeLength * GlyphW + (CodeLength - 1) * Gap + PadX * 2;
    private const int ImageH = GlyphH + PadY * 2;

    private static string RenderPng(string code)
    {
        using var bitmap = new SKBitmap(ImageW, ImageH);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);

        // 背景噪点
        using var noisePaint = new SKPaint { Color = SKColors.LightGray, Style = SKPaintStyle.Fill };
        for (var i = 0; i < 180; i++)
        {
            var x = RandomNumberGenerator.GetInt32(ImageW);
            var y = RandomNumberGenerator.GetInt32(ImageH);
            noisePaint.Color = new SKColor(
                (byte)RandomNumberGenerator.GetInt32(150, 225),
                (byte)RandomNumberGenerator.GetInt32(150, 225),
                (byte)RandomNumberGenerator.GetInt32(150, 225));
            canvas.DrawPoint(x, y, noisePaint);
        }

        // 干扰线
        using var linePaint = new SKPaint
        {
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        for (var i = 0; i < 2; i++)
        {
            linePaint.Color = new SKColor(
                (byte)RandomNumberGenerator.GetInt32(150, 210),
                (byte)RandomNumberGenerator.GetInt32(150, 210),
                (byte)RandomNumberGenerator.GetInt32(150, 210));
            canvas.DrawLine(
                RandomNumberGenerator.GetInt32(ImageW), RandomNumberGenerator.GetInt32(ImageH),
                RandomNumberGenerator.GetInt32(ImageW), RandomNumberGenerator.GetInt32(ImageH),
                linePaint);
        }

        // 逐字符绘制：随机颜色 + 随机旋转
        using var glyphPaint = new SKPaint
        {
            Color = SKColors.DarkBlue,
            Style = SKPaintStyle.Fill,
        };

        for (var i = 0; i < code.Length; i++)
        {
            var cx = PadX + i * (GlyphW + Gap) + GlyphW / 2;
            var cy = ImageH / 2;

            glyphPaint.Color = RandomGlyphColor();
            canvas.Save();
            canvas.Translate(cx, cy);
            canvas.RotateDegrees(RandomNumberGenerator.GetInt32(-MaxRotationDegrees, MaxRotationDegrees + 1));
            DrawDigit(canvas, code[i] - '0', -GlyphW / 2, -GlyphH / 2, glyphPaint);
            canvas.Restore();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }

    private static SKColor RandomGlyphColor() =>
        RandomNumberGenerator.GetInt32(3) switch
        {
            0 => new SKColor(0x1a, 0x3c, 0x6e), // 深蓝
            1 => new SKColor(0x1e, 0x5e, 0x2d), // 深绿
            _ => new SKColor(0x4a, 0x2a, 0x0a), // 深棕
        };

    private static void DrawDigit(SKCanvas canvas, int digit, float x, float y, SKPaint paint)
    {
        var rows = DigitMaps[digit];
        for (var r = 0; r < rows.Length; r++)
        {
            var row = rows[r];
            for (var c = 0; c < row.Length; c++)
            {
                if (row[c] == '1')
                {
                    canvas.DrawRect(x + c * Cell, y + r * Cell, Cell, Cell, paint);
                }
            }
        }
    }
}
