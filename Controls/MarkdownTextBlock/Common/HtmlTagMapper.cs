using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using CC98.Controls.MarkdownTextBlock.Rendering;
using Inline = Microsoft.UI.Xaml.Documents.Inline;

namespace CC98.Controls.MarkdownTextBlock.Common;

/// <summary>
///     HTML 基本解析:以白名单状态机把 Markdig 产出的 HTML 标签 / 块映射为行内格式。
///     白名单:b / strong / i / em / u / s / strike / del / code / a / img / br / p。
///     白名单以外的标签按纯文本原样显示(不静默丢弃内容)。
/// </summary>
public class HtmlTagMapper
{
    private static readonly HashSet<string> ContainerTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "strong", "i", "em", "u", "s", "strike", "del", "code", "a"
    };

    private readonly RenderContext _context;

    /// <summary>当前打开的 HTML 容器标签栈(标签名 + 容器实例)。</summary>
    private readonly List<(string Name, Inline Container)> _openTags = new();

    public HtmlTagMapper(RenderContext context)
    {
        _context = context;
    }

    /// <summary>重置状态(每个块级节点结束后调用,避免未闭合标签影响后续内容)。</summary>
    public void Reset()
    {
        while (_openTags.Count > 0)
        {
            _openTags.RemoveAt(_openTags.Count - 1);
            _context.EndInlineContainer();
        }
    }

    #region 行内 HTML

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
            if (string.IsNullOrWhiteSpace(src)) return;

            if (blockContext)
                _context.AddToContainer(ElementFactory.CreateBlockImage(_context, src, alt));
            else
                _context.AddInline(ElementFactory.CreateInlineImage(_context, src, alt));

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

    private void OpenTag(string name, string rawTag)
    {
        switch (name)
        {
            case "b":
            case "strong":
            {
                var bold = new Bold();
                var boldFontFamily = _context.GetProperty<FontFamily>(RenderPropertyKeys.BoldFontFamily);
                if (boldFontFamily is not null) bold.FontFamily = boldFontFamily;
                _context.BeginInlineContainer(bold);
                break;
            }
            case "i":
            case "em":
                _context.BeginInlineContainer(new Italic());
                break;
            case "u":
                _context.BeginInlineContainer(new Underline());
                break;
            case "s":
            case "strike":
            case "del":
                _context.BeginInlineContainer(new Span { TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough });
                break;
            case "code":
                _context.BeginInlineContainer(new Span
                {
                    FontFamily = _context.GetProperty<FontFamily>(RenderPropertyKeys.MonoFontFamily)
                });
                break;
            case "a":
            {
                var attributes = ParseAttributes(rawTag);
                var href = attributes.GetValueOrDefault("href", string.Empty);
                var hyperlink = new Hyperlink();
                hyperlink.Click += (_, _) =>
                    _context.Control.RaiseLinkClicked(href, CollectText(hyperlink));
                _context.BeginInlineContainer(hyperlink);
                _openTags.Add((name, hyperlink));
                return;
            }
            default:
                return;
        }

        _openTags.Add((name, null!));
    }

    private void CloseTag(string name)
    {
        for (var i = _openTags.Count - 1; i >= 0; i--)
            if (string.Equals(_openTags[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                while (_openTags.Count > i)
                {
                    _openTags.RemoveAt(_openTags.Count - 1);
                    _context.EndInlineContainer();
                }

                return;
            }

        // 未找到匹配的开标签:忽略该闭合标签
    }

    #endregion

    #region 块级 HTML

    /// <summary>
    ///     处理整段 HTML 块:按标签切分,文本按纯文本追加。
    /// </summary>
    public void ProcessHtmlBlock(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return;

        var index = 0;
        while (index < html.Length)
        {
            var open = html.IndexOf('<', index);

            // 标签之前的纯文本
            if (open < 0)
            {
                AppendText(html[index..]);
                break;
            }

            if (open > index) AppendText(html[index..open]);

            var close = html.IndexOf('>', open);
            if (close < 0)
            {
                AppendText(html[open..]);
                break;
            }

            // 跳过注释 <!-- -->
            if (html.AsSpan(open, close - open + 1).StartsWith("<!--"))
            {
                var end = html.IndexOf("-->", open, StringComparison.Ordinal);
                index = end < 0 ? html.Length : end + 3;
                continue;
            }

            ProcessTag(html[open..(close + 1)], true);
            index = close + 1;
        }

        Reset();
    }

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
