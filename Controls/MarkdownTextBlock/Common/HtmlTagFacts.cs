using System;
using System.Collections.Generic;
using System.Globalization;

namespace CC98.Controls.MarkdownTextBlock.Common;

/// <summary>
///     HTML 标签 / 属性的能力表:哪些标签渲染成元素、哪些整段丢弃、哪些按原文显示,以及 URL 是否安全。
/// </summary>
/// <remarks>
///     <para>
///         策略(已确认):**只有下表中的标签会被渲染**;不在表内的标签一律**按原文显示**(标签与内容都作为纯文本),
///         因此不会出现"内容被静默吞掉"的情况。
///     </para>
///     <para>
///         唯一的例外是 <see cref="DroppedWithContent" /> 里的标签(script / style / 文档头相关):
///     它们连内容一起丢弃 —— 这些内容对读者没有意义,且显示出来只会造成误解。
///     </para>
///     <para>纯 C#(不引用 WinUI),可直接被测试工程编译。</para>
/// </remarks>
internal static class HtmlTagFacts
{
    /// <summary>会被渲染成"行内容器"的标签(内部 Markdown / HTML 行内内容照常处理)。</summary>
    public static readonly HashSet<string> InlineContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "b", "bdi", "bdo", "big", "cite", "code", "del", "dfn", "em", "font", "i", "ins",
        "kbd", "label", "mark", "q", "s", "samp", "small", "span", "strike", "strong", "sub", "sup",
        "time", "tt", "u", "var"
    };

    /// <summary>会被渲染成"块级 / 结构化"元素的标签。</summary>
    public static readonly HashSet<string> BlockContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        // 透明容器(只影响换行与样式);center 除了"透明"还自带"居中"语义
        "div", "section", "article", "nav", "aside", "header", "footer", "main", "figure", "figcaption",
        "center",
        // 语义块
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "pre", "blockquote",
        "ul", "ol", "li", "dl", "dt", "dd",
        "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption",
        "details", "summary"
    };

    /// <summary>空元素(void):没有内容,也不会闭合。</summary>
    public static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"
    };

    /// <summary>
    ///     连内容一起丢弃的标签:脚本 / 样式 / 文档头相关元素。
    /// </summary>
    /// <remarks>
    ///     这些标签会把内容一起丢掉(见类注释);其余未支持的标签(含 iframe / form / video 等不可交互元素)
    ///     一律按原文显示 —— 可见、但不会产生任何可交互物。
    /// </remarks>
    public static readonly HashSet<string> DroppedWithContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "template", "noscript", "title", "meta", "link", "base", "head"
    };

    /// <summary>是否需要"原样文本"语义(内容不解析标签,例如 pre 里的 &lt;)。</summary>
    public static bool IsInlineContainer(string? tag) => tag is not null && InlineContainers.Contains(tag);

    public static bool IsBlockContainer(string? tag) => tag is not null && BlockContainers.Contains(tag);

    public static bool IsVoid(string? tag) => tag is not null && VoidElements.Contains(tag);

    public static bool IsDroppedWithContent(string? tag) => tag is not null && DroppedWithContent.Contains(tag);

    /// <summary>是否需要渲染成元素(行内容器 + 块级容器 + 会渲染的空元素)。</summary>
    public static bool IsRenderable(string? tag)
    {
        return IsInlineContainer(tag) || IsBlockContainer(tag) || tag is "br" or "img" or "wbr";
    }

    /// <summary>
    ///     链接(<c>href</c>)是否安全:允许 <c>http</c> / <c>https</c> / <c>mailto</c> 与站内绝对路径,
    ///     其余(含 <c>javascript:</c> / <c>data:</c> / <c>vbscript:</c> / <c>file:</c>)一律拒绝。
    /// </summary>
    public static bool IsSafeLinkUrl(string? url) => IsSafeUrl(url, true);

    /// <summary>图片(<c>src</c>)是否安全:只允许 <c>http</c> / <c>https</c>(站内绝对路径也放行)。</summary>
    public static bool IsSafeImageUrl(string? url) => IsSafeUrl(url, false);

    /// <summary>
    ///     解析 HTML 的像素长度属性(<c>width</c> / <c>height</c>):接受 <c>144</c> 与 <c>144px</c>。
    /// </summary>
    /// <param name="value">属性原始值。</param>
    /// <returns>正数像素值;无法解析或非正数时为 <see langword="null" />。</returns>
    public static double? ParsePixelLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();

        // 未加引号的写法 `width=144/` 会把自闭合斜杠算进属性值(HTML 规范如此),
        // 这里容忍一下,否则 width=144/> 这种很常见的写法会被判为非法而失效。
        if (text.EndsWith('/')) text = text[..^1].Trim();

        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
               && parsed > 0
               && !double.IsInfinity(parsed)
               && !double.IsNaN(parsed)
            ? parsed
            : null;
    }

    private static bool IsSafeUrl(string? url, bool allowMailto)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var value = url.Trim();

        // 站内绝对路径(但排除协议相对写法 //host)
        if (value.StartsWith('/')) return !value.StartsWith("//", StringComparison.Ordinal);

        var colon = value.IndexOf(':');
        if (colon <= 0) return false; // 没有 scheme 的相对地址:不做猜测,直接拒绝

        // 去掉 scheme 里的空白与不可见字符,避免 "java\nscript:" 这类绕过
        var scheme = value[..colon];
        var cleaned = new System.Text.StringBuilder(scheme.Length);
        foreach (var c in scheme)
            if (!char.IsWhiteSpace(c) && !char.IsControl(c))
                cleaned.Append(char.ToLowerInvariant(c));

        return cleaned.ToString() switch
        {
            "http" or "https" => true,
            "mailto" => allowMailto,
            _ => false
        };
    }
}
