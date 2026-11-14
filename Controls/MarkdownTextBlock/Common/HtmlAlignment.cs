using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace CC98.Controls.MarkdownTextBlock.Common;

/// <summary>
///     HTML 的 <c>align</c> 属性与 <c>style</c> 的 <c>text-align</c> → WinUI 对齐枚举的转换。
/// </summary>
/// <remarks>
///     两条 HTML 渲染路径(块级 <see cref="Rendering.HtmlBlockRenderer" />、行内 <see cref="HtmlTagMapper" />)
///     共用这一份实现,避免同一个声明在两处解析出不同结果。
/// </remarks>
internal static class HtmlAlignment
{
    /// <summary>从属性集合解析对齐声明(<c>style</c> 的 <c>text-align</c> 优先,其次 <c>align</c> 属性)。</summary>
    /// <param name="attributes">标签属性集合。</param>
    /// <param name="alignment">解析结果。</param>
    /// <returns>元素声明了可识别的对齐时返回 <see langword="true" />。</returns>
    public static bool TryParse(IReadOnlyDictionary<string, string> attributes, out HorizontalAlignment alignment)
    {
        return TryParse(attributes.GetValueOrDefault("style"), attributes.GetValueOrDefault("align"), out alignment);
    }

    /// <summary>从 <c>style</c> 字符串与 <c>align</c> 属性值解析对齐声明。</summary>
    /// <param name="style"><c>style</c> 属性的原始值(可为 null)。</param>
    /// <param name="alignAttribute"><c>align</c> 属性的原始值(可为 null)。</param>
    /// <param name="alignment">解析结果。</param>
    /// <returns>声明了可识别的对齐时返回 <see langword="true" />。</returns>
    /// <remarks><c>justify</c> 等块级元素没有对应语义,按"未声明"处理。</remarks>
    public static bool TryParse(string? style, string? alignAttribute, out HorizontalAlignment alignment)
    {
        var declarations = HtmlStyle.Parse(style);
        var value = declarations.TryGetValue("text-align", out var fromStyle) ? fromStyle : alignAttribute;

        switch (value?.Trim().ToLowerInvariant())
        {
            case "center":
                alignment = HorizontalAlignment.Center;
                return true;
            case "right":
                alignment = HorizontalAlignment.Right;
                return true;
            case "left":
                alignment = HorizontalAlignment.Left;
                return true;
            default:
                alignment = default;
                return false;
        }
    }

    /// <summary>
    ///     标签**自带**的块级对齐语义(<c>&lt;center&gt;</c> 恒为居中):元素没有显式声明时用它兜底。
    /// </summary>
    /// <param name="tag">标签名。</param>
    /// <returns>标签自带的对齐;没有这种语义时返回 <see langword="null" />。</returns>
    public static HorizontalAlignment? DefaultFor(string? tag)
    {
        return tag?.ToLowerInvariant() switch
        {
            "center" => HorizontalAlignment.Center,
            _ => null
        };
    }

    /// <summary>块级对齐 → 文本对齐(用于 <c>RichTextBlock.TextAlignment</c>)。</summary>
    /// <param name="alignment">块级对齐。</param>
    /// <returns>对应的文本对齐。</returns>
    public static TextAlignment ToTextAlignment(HorizontalAlignment alignment)
    {
        return alignment switch
        {
            HorizontalAlignment.Center => TextAlignment.Center,
            HorizontalAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };
    }
}
