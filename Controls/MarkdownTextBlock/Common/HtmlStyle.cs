using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.UI;

namespace CC98.Controls.MarkdownTextBlock.Common;

/// <summary>
///     HTML <c>style</c> 属性的最小实现:只解析渲染真正需要的少量 CSS 声明。
/// </summary>
/// <remarks>
///     <para>支持:color / background-color / font-weight / font-style / text-decoration / text-align / font-size。</para>
///     <para>其余声明一律忽略(不产生任何效果,也不报错);<c>url()</c> / <c>expression()</c> 等可执行形式
///     在解析阶段就被丢弃(见 <see cref="Parse" />)。</para>
///     <para>纯 C#(不引用 WinUI),可直接被测试工程编译。</para>
/// </remarks>
internal static class HtmlStyle
{
    /// <summary>常见颜色名(够用即可,未收录的名字按解析失败处理)。</summary>
    private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Color.FromArgb(255, 0, 0, 0),
        ["white"] = Color.FromArgb(255, 255, 255, 255),
        ["red"] = Color.FromArgb(255, 255, 0, 0),
        ["green"] = Color.FromArgb(255, 0, 128, 0),
        ["blue"] = Color.FromArgb(255, 0, 0, 255),
        ["yellow"] = Color.FromArgb(255, 255, 255, 0),
        ["orange"] = Color.FromArgb(255, 255, 165, 0),
        ["purple"] = Color.FromArgb(255, 128, 0, 128),
        ["pink"] = Color.FromArgb(255, 255, 192, 203),
        ["gray"] = Color.FromArgb(255, 128, 128, 128),
        ["grey"] = Color.FromArgb(255, 128, 128, 128),
        ["silver"] = Color.FromArgb(255, 192, 192, 192),
        ["maroon"] = Color.FromArgb(255, 128, 0, 0),
        ["olive"] = Color.FromArgb(255, 128, 128, 0),
        ["lime"] = Color.FromArgb(255, 0, 255, 0),
        ["teal"] = Color.FromArgb(255, 0, 128, 128),
        ["navy"] = Color.FromArgb(255, 0, 0, 128),
        ["aqua"] = Color.FromArgb(255, 0, 255, 255),
        ["cyan"] = Color.FromArgb(255, 0, 255, 255),
        ["magenta"] = Color.FromArgb(255, 255, 0, 255),
        ["fuchsia"] = Color.FromArgb(255, 255, 0, 255),
        ["brown"] = Color.FromArgb(255, 165, 42, 42),
        ["gold"] = Color.FromArgb(255, 255, 215, 0),
        ["transparent"] = Color.FromArgb(0, 0, 0, 0)
    };

    /// <summary>
    ///     解析 <c>style="a: b; c: d"</c>:返回小写属性名 → 去空白的值。
    /// </summary>
    /// <remarks>
    ///     含 <c>url(</c> 或 <c>expression(</c> 的声明直接丢弃(不允许任何资源引用 / 表达式),
    ///     <c>!important</c> 后缀会被剥掉(对渲染无意义)。
    /// </remarks>
    public static Dictionary<string, string> Parse(string? style)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style)) return result;

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0 || colon == declaration.Length - 1) continue;

            var name = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0) continue;

            // 安全:任何形式的资源引用 / 表达式都不接受
            if (value.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("expression(", StringComparison.OrdinalIgnoreCase) ||
                value.Contains('\\'))
                continue;

            var important = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
            if (important >= 0) value = value[..important].Trim();

            // 同名声明以后出现的为准(CSS 层叠规则)
            result[name] = value;
        }

        return result;
    }

    /// <summary>解析颜色值:<c>#rgb</c> / <c>#rrggbb</c> / <c>#aarrggbb</c> / <c>rgb()</c> / <c>rgba()</c> / 颜色名。</summary>
    public static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        value = value.Trim();

        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            switch (hex.Length)
            {
                case 3: // #rgb
                {
                    if (!TryHex(hex[0], out var r3) || !TryHex(hex[1], out var g3) || !TryHex(hex[2], out var b3))
                        return false;
                    color = Color.FromArgb(255, (byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17));
                    return true;
                }
                case 6: // #rrggbb
                    if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r6) ||
                        !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g6) ||
                        !byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b6))
                        return false;
                    color = Color.FromArgb(255, r6, g6, b6);
                    return true;
                case 8: // #aarrggbb
                    if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a8) ||
                        !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r8) ||
                        !byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g8) ||
                        !byte.TryParse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b8))
                        return false;
                    color = Color.FromArgb(a8, r8, g8, b8);
                    return true;
                default:
                    return false;
            }
        }

        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = value.IndexOf('(');
            var close = value.LastIndexOf(')');
            if (open < 0 || close <= open) return false;

            var parts = value[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length is < 3 or > 4) return false;

            if (!TryChannel(parts[0], out var r) || !TryChannel(parts[1], out var g) || !TryChannel(parts[2], out var b))
                return false;

            var a = (byte)255;
            if (parts.Length == 4 && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha))
                a = (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255);

            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        return NamedColors.TryGetValue(value, out color);
    }

    /// <summary>
    ///     解析长度值(用于 font-size):支持 <c>px</c> / <c>pt</c> / <c>em</c> / <c>rem</c> / <c>%</c>,缺省按 px。
    /// </summary>
    /// <param name="value">CSS 长度字符串。</param>
    /// <param name="baseSize">相对单位的基准字号。</param>
    /// <param name="size">解析结果(像素)。</param>
    public static bool TryParseFontSize(string? value, double baseSize, out double size)
    {
        size = 0;
        if (string.IsNullOrWhiteSpace(value) || baseSize <= 0) return false;

        value = value.Trim();
        double factor = 1;
        string number = value;

        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) number = value[..^2];
        else if (value.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) { number = value[..^2]; factor = 96.0 / 72.0; }
        else if (value.EndsWith("rem", StringComparison.OrdinalIgnoreCase)) { number = value[..^3]; factor = baseSize; }
        else if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase)) { number = value[..^2]; factor = baseSize; }
        else if (value.EndsWith('%')) { number = value[..^1]; factor = baseSize / 100.0; }
        else if (!char.IsDigit(value[^1])) return false; // 其它单位(vw/vh/calc 等)一律忽略

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return false;
        if (parsed <= 0) return false;

        // 夹取到合理区间,避免用户用一个 style 撑爆版面
        size = Math.Clamp(parsed * factor, 6, 96);
        return true;
    }

    private static bool TryHex(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1
        };
        return value >= 0;
    }

    private static bool TryChannel(string text, out byte value)
    {
        value = 0;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return false;
        value = (byte)Math.Clamp(Math.Round(parsed), 0, 255);
        return true;
    }
}
