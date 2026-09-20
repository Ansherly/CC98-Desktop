using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace CC98.Controls.MarkdownTextBlock.Common;

/// <summary>
///     把 HTML 的 <c>style</c> / <c>font</c> 属性应用到行内元素上。
/// </summary>
/// <remarks>
///     <para>支持的声明:color / font-size / font-weight / font-style / text-decoration。</para>
///     <para>
///         <b>行内背景色(background-color)本轮不支持</b>:WinUI 的 <see cref="Span" /> 没有背景色,
///         要实现只能用 <c>InlineUIContainer + Border</c> 包一层,会牺牲行内换行与文本选择 ——
///         块级元素(div / p / td)的背景色是支持的(用 Border 承载),需要行内高亮时请用块级写法。
///     </para>
/// </remarks>
internal static class HtmlInlineFormatting
{
    /// <summary>把 style 声明应用到行内容器。</summary>
    public static void ApplyStyle(Span span, RenderContext context, Dictionary<string, string> style)
    {
        if (style.Count == 0) return;

        var baseFontSize = context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;
        var decorations = TextDecorations.None;

        foreach (var (name, value) in style)
            switch (name)
            {
                case "color":
                    if (HtmlStyle.TryParseColor(value, out var color))
                        span.Foreground = new SolidColorBrush(color);
                    break;

                case "font-size":
                    if (HtmlStyle.TryParseFontSize(value, baseFontSize, out var size))
                        span.FontSize = size;
                    break;

                case "font-weight":
                    if (IsBoldWeight(value)) span.FontWeight = FontWeights.Bold;
                    break;

                case "font-style":
                    if (value.StartsWith("italic") || value.StartsWith("oblique"))
                        span.FontStyle = FontStyle.Italic;
                    break;

                case "text-decoration":
                case "text-decoration-line":
                    if (value.Contains("underline", System.StringComparison.OrdinalIgnoreCase))
                        decorations |= TextDecorations.Underline;
                    if (value.Contains("line-through", System.StringComparison.OrdinalIgnoreCase))
                        decorations |= TextDecorations.Strikethrough;
                    break;
            }

        if (decorations != TextDecorations.None) span.TextDecorations = decorations;
    }

    /// <summary>把 <c>&lt;font&gt;</c> 的 color / size / face 属性应用到行内容器(face 忽略:不引入任意字体)。</summary>
    public static void ApplyFontAttributes(Span span, RenderContext context, Dictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("color", out var colorValue) && HtmlStyle.TryParseColor(colorValue, out var color))
            span.Foreground = new SolidColorBrush(color);

        // HTML 传统的 size=1..7:按比例映射到基准字号
        if (attributes.TryGetValue("size", out var sizeValue) &&
            int.TryParse(sizeValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
        {
            var baseFontSize = context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;
            var scale = level switch
            {
                <= 1 => 0.75,
                2 => 0.85,
                3 => 1.0,
                4 => 1.15,
                5 => 1.3,
                6 => 1.5,
                _ => 1.8
            };
            span.FontSize = baseFontSize * scale;
        }
    }

    private static bool IsBoldWeight(string value)
    {
        value = value.Trim();
        if (value.Equals("bold", System.StringComparison.OrdinalIgnoreCase) ||
            value.Equals("bolder", System.StringComparison.OrdinalIgnoreCase))
            return true;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) && weight >= 600;
    }
}
