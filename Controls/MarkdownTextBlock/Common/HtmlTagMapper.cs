using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using CC98.Controls.MarkdownTextBlock.Rendering;
using Inline = Microsoft.UI.Xaml.Documents.Inline;

namespace CC98.Controls.MarkdownTextBlock.Common;

/// <summary>
///     HTML 基本解析:以白名单状态机把 Markdig 产出的行内 HTML 标签映射为行内格式。
///     白名单见 <see cref="HtmlTagFacts.InlineContainers" />(b / strong / i / em / u / s / del / code / a / span / mark / font …)。
///     白名单以外的标签按原文显示(不静默丢弃内容);<c>script</c>/<c>style</c> 一类由块级渲染器整段丢弃。
/// </summary>
public class HtmlTagMapper
{
    /// <summary>行内容器标签(与块级渲染器共用同一张表,避免两处白名单走偏)。</summary>
    private static readonly HashSet<string> ContainerTags = HtmlTagFacts.InlineContainers;

    private readonly RenderContext _context;

    /// <summary>当前打开的 HTML 容器标签栈(标签名 + 容器实例 + 打开时保存的块级对齐)。</summary>
    private readonly List<(string Name, Inline Container, HorizontalAlignment? SavedAlignment)> _openTags = new();

    public HtmlTagMapper(RenderContext context)
    {
        _context = context;
    }

    /// <summary>重置状态(每个块级节点结束后调用,避免未闭合标签影响后续内容)。</summary>
    public void Reset()
    {
        while (_openTags.Count > 0)
        {
            var closing = _openTags[^1];
            _openTags.RemoveAt(_openTags.Count - 1);
            _context.EndInlineContainer();
            _context.BlockAlignment = closing.SavedAlignment;
        }
    }

    /// <summary>
    ///     登记一个已打开的行内容器:同时记录当前块级对齐,并在标签声明了 <c>align</c> / <c>text-align</c> 时更新它。
    /// </summary>
    /// <param name="name">标签名。</param>
    /// <param name="container">容器实例。</param>
    /// <param name="attributes">标签属性。</param>
    private void PushOpenTag(string name, Inline container, IReadOnlyDictionary<string, string> attributes)
    {
        _openTags.Add((name, container, _context.BlockAlignment));

        if (HtmlAlignment.TryParse(attributes, out var declared)) _context.BlockAlignment = declared;
    }

    #region 行内 HTML

    /// <summary>追加一段纯文本(HTML 文本节点):解码实体后写入当前段落。</summary>
    public void ProcessText(string? text)
    {
        if (!string.IsNullOrEmpty(text)) AppendText(text);
    }

    /// <summary>按标签名闭合一个行内容器(供块级渲染器遍历 DOM 时调用)。</summary>
    public void ProcessEndTag(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return;
        CloseTag(tagName.Trim().TrimStart('/').ToLowerInvariant());
    }

    /// <summary>
    ///     处理单个行内标签。
    /// </summary>
    public void ProcessTag(string? rawTag, bool blockContext = false)
    {
        if (string.IsNullOrWhiteSpace(rawTag)) return;

        var tag = rawTag.Trim();
        if (!tag.StartsWith('<'))
        {
            AppendText(tag);
            return;
        }

        var isClosing = tag.StartsWith("</", StringComparison.Ordinal);
        var isSelfClosing = tag.EndsWith("/>", StringComparison.Ordinal);
        var name = ParseTagName(tag);

        if (name.Length == 0)
        {
            AppendText(tag);
            return;
        }

        // 空元素
        if (name is "br")
        {
            _context.AddInline(new LineBreak());
            return;
        }

        if (name is "img")
        {
            var attributes = ParseAttributes(tag);
            var src = attributes.GetValueOrDefault("src", string.Empty);
            var alt = attributes.GetValueOrDefault("alt", string.Empty);

            // 不可信地址(javascript: / data: …)一律不加载,按原文显示
            if (!HtmlTagFacts.IsSafeImageUrl(src))
            {
                AppendText(tag);
                return;
            }

            // 链接内不能放 InlineUIContainer(WinUI 限制:Hyperlink.Inlines 会抛 E_INVALIDARG),
            // 必须退出链接、按【块级】图片落地 —— 与 UBB 的 [url][img][/url] 行为一致;
            // 也因此不能套用行内图片工厂(它会给未指尺寸的图片按行高限高)。
            var width = HtmlTagFacts.ParsePixelLength(attributes.GetValueOrDefault("width"));
            var height = HtmlTagFacts.ParsePixelLength(attributes.GetValueOrDefault("height"));

            if (blockContext || _context.IsInsideHyperlink)
                _context.AddBlockElement(ElementFactory.CreateBlockImage(_context, src, alt, width, height));
            else
                _context.AddInline(ElementFactory.CreateInlineImage(_context, src, alt, width, height));

            return;
        }

        // 段落边界
        if (name is "p")
        {
            _context.FinalizeCurrentTextBlock();
            return;
        }

        if (!ContainerTags.Contains(name))
        {
            // 白名单以外的标签:按纯文本原样显示
            AppendText(tag);
            return;
        }

        if (isClosing)
        {
            CloseTag(name);
            return;
        }

        OpenTag(name, tag);

        // 形如 <b/> 的写法:立即闭合
        if (isSelfClosing) CloseTag(name);
    }

    /// <summary>打开一个行内容器:按标签映射样式,并应用 style / font 等属性。</summary>
    private void OpenTag(string name, string rawTag)
    {
        var attributes = ParseAttributes(rawTag);
        var style = HtmlStyle.Parse(attributes.GetValueOrDefault("style"));
        var baseFontSize = _context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;

        // 链接:先做 URL 安全校验;不可信的地址整段按原文显示,不产生可点击元素
        if (name is "a")
        {
            var href = attributes.GetValueOrDefault("href", string.Empty);
            if (!HtmlTagFacts.IsSafeLinkUrl(href))
            {
                AppendText(rawTag);
                return;
            }

            var hyperlink = new Hyperlink();
            hyperlink.Click += (_, _) => _context.Control.RaiseLinkClicked(href, CollectText(hyperlink));
            HtmlInlineFormatting.ApplyStyle(hyperlink, _context, style);
            _context.BeginInlineContainer(hyperlink);
            PushOpenTag(name, hyperlink, attributes);
            return;
        }

        Span container = name switch
        {
            "b" or "strong" => CreateBold(),
            "i" or "em" => new Italic(),
            "u" or "ins" => new Underline(),
            "s" or "strike" or "del" => new Span { TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough },
            "code" or "kbd" or "samp" or "var" or "tt" => new Span
            {
                FontFamily = _context.GetProperty<FontFamily>(RenderPropertyKeys.MonoFontFamily)
            },
            "small" => new Span { FontSize = baseFontSize * 0.85 },
            "big" => new Span { FontSize = baseFontSize * 1.2 },
            "sub" => CreateSubSuperscript(false, baseFontSize),
            "sup" => CreateSubSuperscript(true, baseFontSize),
            // span / cite / q / abbr / dfn / time / bdi / bdo / label / font 等:只承载属性,不加默认格式
            _ => new Span()
        };

        // <font color= size= face=>:传统属性(face 忽略,不引入任意字体)
        if (name is "font") HtmlInlineFormatting.ApplyFontAttributes(container, _context, attributes);

        // <mark>:WinUI 的 Span 没有背景色,退化为"加粗"提示重点(见 HtmlInlineFormatting 的说明)
        if (name is "mark") container.FontWeight = FontWeights.Bold;

        HtmlInlineFormatting.ApplyStyle(container, _context, style);

        _context.BeginInlineContainer(container);
        PushOpenTag(name, container, attributes);
    }

    private Bold CreateBold()
    {
        var bold = new Bold();
        var boldFontFamily = _context.GetProperty<FontFamily>(RenderPropertyKeys.BoldFontFamily);
        if (boldFontFamily is not null) bold.FontFamily = boldFontFamily;
        return bold;
    }

    /// <summary>
    ///     上下标:缩小字号。
    ///     WinUI 的 Span 没有基线上移,字体变体(Typography.FontVariants)在不同字体下表现不一致,
    ///     这里只做缩小处理 —— 需要严格排版时请让作者用图片或公式。
    /// </summary>
    private static Span CreateSubSuperscript(bool isSuperscript, double baseFontSize)
    {
        return new Span { FontSize = baseFontSize * 0.75 };
    }

    private void CloseTag(string name)
    {
        for (var i = _openTags.Count - 1; i >= 0; i--)
            if (string.Equals(_openTags[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                while (_openTags.Count > i)
                {
                    var closing = _openTags[^1];
                    _openTags.RemoveAt(_openTags.Count - 1);
                    _context.EndInlineContainer();
                    _context.BlockAlignment = closing.SavedAlignment; // 恢复外层对齐
                }

                return;
            }

        // 未找到匹配的开标签:忽略该闭合标签
    }

    #endregion

    #region 块级 HTML

    // 块级 HTML 自 2026-09-19 起由 `Rendering/HtmlBlockRenderer`(AngleSharp DOM + 白名单映射)负责,
    // 原先"按 < > 切分"的 ProcessHtmlBlock 已移除 —— 需要块级能力请走 HtmlBlockRenderer。

    #endregion

    #region 文本与实体

    private void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _context.AddInline(new Run { Text = DecodeEntities(text) });
    }

    /// <summary>
    ///     解码常见 HTML 实体与数字实体。
    /// </summary>
    public static string DecodeEntities(string text)
    {
        if (text.IndexOf('&') < 0) return text;

        var builder = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var ampersand = text.IndexOf('&', index);
            if (ampersand < 0 || ampersand == text.Length - 1)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, ampersand - index);

            var semicolon = text.IndexOf(';', ampersand + 1);
            if (semicolon < 0 || semicolon - ampersand > 10)
            {
                builder.Append('&');
                index = ampersand + 1;
                continue;
            }

            var entity = text[(ampersand + 1)..semicolon];
            var decoded = DecodeEntity(entity);
            if (decoded is null)
            {
                builder.Append(text, ampersand, semicolon - ampersand + 1);
            }
            else
            {
                builder.Append(decoded);
            }

            index = semicolon + 1;
        }

        return builder.ToString();
    }

    private static string? DecodeEntity(string entity)
    {
        switch (entity.ToLowerInvariant())
        {
            case "amp": return "&";
            case "lt": return "<";
            case "gt": return ">";
            case "quot": return "\"";
            case "apos": return "'";
            case "nbsp": return "\u00A0";
            case "mdash": return "—";
            case "ndash": return "–";
            case "hellip": return "…";
            case "copy": return "©";
            case "reg": return "®";
            case "trade": return "™";
            case "laquo": return "«";
            case "raquo": return "»";
            case "times": return "×";
            case "divide": return "÷";
            case "deg": return "°";
            case "middot": return "·";
        }

        if (entity.StartsWith('#'))
        {
            var isHex = entity.Length > 1 && (entity[1] == 'x' || entity[1] == 'X');
            var digits = isHex ? entity[2..] : entity[1..];
            if (digits.Length == 0) return null;

            var style = isHex ? NumberStyles.HexNumber : NumberStyles.Integer;
            return int.TryParse(digits, style, CultureInfo.InvariantCulture, out var codePoint)
                   && codePoint is > 0 and <= 0x10FFFF
                ? char.ConvertFromUtf32(codePoint)
                : null;
        }

        return null;
    }

    #endregion

    #region 解析辅助

    private static string ParseTagName(string tag)
    {
        var start = tag.StartsWith("</", StringComparison.Ordinal) ? 2 : 1;
        var end = start;

        while (end < tag.Length && (char.IsLetterOrDigit(tag[end]) || tag[end] == '-' || tag[end] == ':')) end++;

        return tag[start..end].ToLowerInvariant();
    }

    /// <summary>
    ///     解析标签属性(支持双引号 / 单引号 / 无引号三种写法)。
    /// </summary>
    public static Dictionary<string, string> ParseAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        while (index < tag.Length)
        {
            // 找到属性名起点(跳过标签名)
            while (index < tag.Length && (char.IsWhiteSpace(tag[index]) || tag[index] == '<' || tag[index] == '/'))
                index++;
            if (index >= tag.Length || tag[index] == '>') break;

            var nameStart = index;
            while (index < tag.Length && tag[index] != '=' && !char.IsWhiteSpace(tag[index]) && tag[index] != '>' && tag[index] != '/')
                index++;

            var name = tag[nameStart..index].Trim();
            if (name.Length == 0)
            {
                index++;
                continue;
            }

            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;

            if (index >= tag.Length || tag[index] != '=')
            {
                attributes.TryAdd(name, string.Empty);
                continue;
            }

            index++; // 跳过 '='
            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;

            string value;
            if (index < tag.Length && (tag[index] == '"' || tag[index] == '\''))
            {
                var quote = tag[index];
                index++;
                var valueStart = index;
                while (index < tag.Length && tag[index] != quote) index++;
                value = tag[valueStart..index];
                if (index < tag.Length) index++;
            }
            else
            {
                var valueStart = index;
                while (index < tag.Length && !char.IsWhiteSpace(tag[index]) && tag[index] != '>') index++;
                value = tag[valueStart..index];
            }

            attributes[name] = DecodeEntities(value);
        }

        return attributes;
    }

    private static string CollectText(Inline inline)
    {
        var builder = new StringBuilder();
        CollectTextCore(inline, builder);
        return builder.ToString();
    }

    private static void CollectTextCore(Inline inline, StringBuilder builder)
    {
        if (inline is Run run)
            builder.Append(run.Text);
        else if (inline is Span span)
            foreach (var child in span.Inlines)
                CollectTextCore(child, builder);
        else if (inline is InlineUIContainer { Child: Microsoft.UI.Xaml.Controls.TextBlock textBlock })
            builder.Append(textBlock.Text);
    }

    #endregion
}
