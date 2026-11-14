using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Markdig.Syntax;
using CC98.Controls.MarkdownTextBlock.Common;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using CodeBlockControl = CC98.Controls.CodeBlock;
// AngleSharp.Dom 与 WinUI 存在同名枚举,这里显式指向 WinUI 版本
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using TextAlignment = Microsoft.UI.Xaml.TextAlignment;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace CC98.Controls.MarkdownTextBlock.Rendering;

/// <summary>
///     块级 HTML 渲染:用 AngleSharp 把 HTML 片段解析成 DOM,再按白名单映射为 WinUI 元素。
/// </summary>
/// <remarks>
///     <para>
///         分工:结构(div / p / 标题 / 列表 / 引用 / pre / 表格 / details)在这里按 DOM 处理;
///         行内格式(b / i / span style / a / code …)仍然交给 <see cref="HtmlTagMapper" /> ——
///         做法是把 DOM 元素序列化回"标签原文",复用已有的行内状态机,避免两套实现走偏。
///     </para>
///     <para>
///         标签策略:白名单内 → 渲染成元素;不在白名单 → **按原文显示**(标签与内容都作为纯文本);
///         <c>script</c>/<c>style</c> 等 → **连内容一起丢弃**(见 <see cref="HtmlTagFacts" />)。
///     </para>
/// </remarks>
internal sealed class HtmlBlockRenderer
{
    private static readonly HtmlParser Parser = new();

    private readonly RenderContext _context;

    /// <summary>当前所处的行内嵌套深度(&gt;0 表示正在行内容器里渲染,图片等元素要留行内)。</summary>
    private int _inlineDepth;

    public HtmlBlockRenderer(RenderContext context)
    {
        _context = context;
    }

    /// <summary>渲染一段 HTML 块。</summary>
    public void Render(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return;

        var document = Parser.ParseDocument(html);

        // Body 理论上总在(解析器会补全),但按可空契约走一遍,避免 CS8602
        var body = document.Body;
        if (body is not null)
            foreach (var child in body.ChildNodes)
                RenderNode(child);

        _context.HtmlMapper.Reset();
    }

    #region 遍历

    private void RenderNode(INode node)
    {
        switch (node)
        {
            case IText text:
                RenderText(text.Data);
                break;
            case IElement element:
                RenderElement(element);
                break;
        }
    }

    private void RenderChildren(IElement element)
    {
        foreach (var child in element.ChildNodes) RenderNode(child);
    }

    /// <summary>
    ///     渲染 HTML 块里的一段文本。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         CommonMark 规定"块级 HTML 内部不解析 Markdown",所以 Markdig 会把整个
    ///         <c>&lt;center&gt;…&lt;/center&gt;</c> 原样交给我们。但论坛里在 <c>&lt;div&gt;</c> / <c>&lt;center&gt;</c>
    ///         里写 <c>![](图片)</c>、<c>**粗体**</c> 是很常见的写法,因此这里补做一次**行内** Markdown 解析。
    ///     </para>
    ///     <para>
    ///         只解析行内:块级 HTML 遇到空行就结束了,一个块里本来也放不下块级结构;
    ///         行内容器(如 <c>&lt;span&gt;</c> / <c>&lt;code&gt;</c>)里的文本不重解析,
    ///         避免把本该原样显示的写法改写掉。
    ///     </para>
    /// </remarks>
    private void RenderText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_inlineDepth == 0 && TryRenderInlineMarkdown(text)) return;

        _context.HtmlMapper.ProcessText(text);
    }

    /// <summary>
    ///     把一段文本按行内 Markdown 解析后,交给行内渲染策略。
    /// </summary>
    /// <returns>解析出了内容返回 <see langword="true" />;纯空白等情况下返回 <see langword="false" />(调用方按原文输出)。</returns>
    private bool TryRenderInlineMarkdown(string text)
    {
        var document = Markdown.Parse(text, MarkdownTextBlock.Pipeline);

        var rendered = false;
        foreach (var leaf in document.Descendants<LeafBlock>())
        {
            if (leaf.Inline is not { } inline) continue;

            _context.RenderNode(inline);
            rendered = true;
        }

        return rendered;
    }

    private void RenderElement(IElement element)
    {
        var tag = element.LocalName.ToLowerInvariant();

        // 1) script / style / 文档头:连内容丢弃(显示出来只会造成误解)
        if (HtmlTagFacts.IsDroppedWithContent(tag)) return;

        // 2) 白名单外:整段按原文显示(标签 + 内容都是纯文本)
        if (!HtmlTagFacts.IsRenderable(tag))
        {
            EmitRaw(element.OuterHtml);
            return;
        }

        // 3) 空元素
        switch (tag)
        {
            case "br":
                _context.AddInline(new LineBreak());
                return;
            case "wbr":
                return;
            case "img":
                RenderImage(element);
                return;
            case "hr":
                RenderSeparator();
                return;
        }

        // 4) 行内容器:交给行内状态机
        if (HtmlTagFacts.IsInlineContainer(tag))
        {
            RenderInlineElement(element);
            return;
        }

        // 5) 块级
        switch (tag)
        {
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                RenderHeading(element);
                return;
            case "p":
                RenderParagraph(element);
                return;
            case "ul" or "ol":
                RenderList(element, 0);
                return;
            case "blockquote":
                RenderQuote(element);
                return;
            case "pre":
                RenderPre(element);
                return;
            case "table":
                RenderTable(element);
                return;
            case "details":
                RenderDetails(element);
                return;
            default:
                // 透明容器:div / section / li / td / dt / figure / caption / tr …
                RenderChildrenAligned(element);
                return;
        }
    }

    /// <summary>行内元素:把 DOM 元素序列化回标签原文,交给 <see cref="HtmlTagMapper" /> 处理。</summary>
    private void RenderInlineElement(IElement element)
    {
        var tag = element.LocalName.ToLowerInvariant();

        // 行内元素上的 align / text-align(如 <a align=center>):继承给内部落地的块级内容
        var previousAlignment = _context.BlockAlignment;
        if (HtmlAlignment.TryParse(element.GetAttribute("style"), element.GetAttribute("align"), out var declared))
            _context.BlockAlignment = declared;

        _context.HtmlMapper.ProcessTag(ToStartTag(element));
        _inlineDepth++; // 内部若有 <img> 等元素,需要按行内方式渲染
        RenderChildren(element);
        _inlineDepth--;
        _context.HtmlMapper.ProcessTag($"</{tag}>");

        _context.BlockAlignment = previousAlignment;
    }

    #endregion

    #region 块级元素

    private void RenderHeading(IElement element)
    {
        var level = element.LocalName[1] - '0';
        var baseFontSize = _context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;

        _context.StartNewTextBlock(level <= 2 ? 10 : 6);

        var span = new Span
        {
            FontSize = baseFontSize * RenderHelper.GetHeadingScale(level),
            FontWeight = FontWeights.SemiBold
        };
        var boldFontFamily = _context.GetProperty<FontFamily>(RenderPropertyKeys.BoldFontFamily);
        if (boldFontFamily is not null) span.FontFamily = boldFontFamily;
        HtmlInlineFormatting.ApplyStyle(span, _context, HtmlStyle.Parse(element.GetAttribute("style")));

        _context.BeginInlineContainer(span);
        RenderChildrenAligned(element);
        _context.EndInlineContainer();
        _context.FinalizeCurrentTextBlock();
    }

    private void RenderParagraph(IElement element)
    {
        _context.StartNewTextBlock();

        // 有 background-color 时子节点已被渲染进带背景的面板里,不能再渲染一次
        if (!ApplyBlockBackground(element)) RenderChildrenAligned(element);

        _context.FinalizeCurrentTextBlock();
    }

    private void RenderList(IElement element, int depth)
    {
        var ordered = element.LocalName.Equals("ol", StringComparison.OrdinalIgnoreCase);
        var index = 1;

        foreach (var child in element.Children)
        {
            var childTag = child.LocalName.ToLowerInvariant();

            // 列表项之间可能有嵌套列表,递归处理
            if (childTag is "ul" or "ol")
            {
                RenderList(child, depth + 1);
                continue;
            }

            if (childTag != "li") continue;

            _context.StartNewTextBlock();
            _context.AddInline(new Run
            {
                Text = new string(' ', depth * 4) + (ordered ? $"{index++}. " : "• ")
            });
            RenderChildren(child);
            _context.FinalizeCurrentTextBlock();
        }
    }

    /// <summary>引用块:左侧竖条 + 引用背景色(与 Markdown 引用保持同一套视觉)。</summary>
    private void RenderQuote(IElement element)
    {
        var isOutermost = _context.QuoteNestingLevel == 0;
        var contentPanel = new StackPanel { Spacing = 0 };

        var border = new Border
        {
            Child = contentPanel,
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = _context.GetBrush(RenderPropertyKeys.QuoteBorderBrush,
                BrushHelper.CreateSubtleBrush(_context.Control.Foreground, 0x99)),
            Background = isOutermost ? _context.GetProperty<Brush>(RenderPropertyKeys.QuoteBackground) : null,
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(isOutermost ? 12 : 8, 8, 8, 8),
            Margin = new Thickness(0, isOutermost ? 4 : 2, 0, isOutermost ? 10 : 2),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var previousSpacing = _context.ParagraphSpacing;

        _context.QuoteNestingLevel++;
        _context.ParagraphSpacing = isOutermost ? 6 : 4;
        _context.PushContainer(contentPanel);
        RenderChildren(element);
        _context.PopContainer();
        _context.QuoteNestingLevel--;
        _context.ParagraphSpacing = previousSpacing;

        // 与 Markdown 引用块保持一致:抹平首尾外边距,让文字上下留白对称
        RenderHelper.TrimVerticalEdgeMargins(contentPanel);

        _context.AddToContainer(border);
    }

    /// <summary><c>&lt;pre&gt;</c>:等宽代码块(与 Markdown 围栏代码块同一套视觉)。</summary>
    private void RenderPre(IElement element)
    {
        var viewer = new CodeBlockControl
        {
            Code = element.TextContent,
            LanguageName = "plaintext",
            Background = _context.GetProperty<Brush>(RenderPropertyKeys.CodeBackground),
            BorderBrush = _context.GetBrush(RenderPropertyKeys.BorderBrush,
                BrushHelper.CreateSubtleBrush(_context.Control.Foreground, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 10),
            Margin = new Thickness(0, 4, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _context.AddToContainer(viewer);
    }

    /// <summary>HTML 表格:等宽列 Grid + 单元格边框(表头加底色)。</summary>
    private void RenderTable(IElement element)
    {
        var rows = element.QuerySelectorAll("tr").ToArray();
        if (rows.Length == 0) return;

        var columnCount = 0;
        foreach (var row in rows)
            columnCount = Math.Max(columnCount, row.Children.Count(c => c.LocalName is "td" or "th"));

        if (columnCount == 0) return;

        var borderBrush = _context.GetBrush(RenderPropertyKeys.BorderBrush,
            BrushHelper.CreateSubtleBrush(_context.Control.Foreground, 0x40));
        var headerFill = BrushHelper.CreateFillBrush(_context.Control.Foreground);

        var grid = new Grid
        {
            Margin = new Thickness(0, 6, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        for (var i = 0; i < columnCount; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var columnIndex = 0;
            foreach (var cell in rows[rowIndex].Children)
            {
                var cellTag = cell.LocalName.ToLowerInvariant();
                if (cellTag is not ("td" or "th")) continue;

                var isHeader = cellTag == "th";
                var cellPanel = new StackPanel();
                var border = new Border
                {
                    Child = cellPanel,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    Background = isHeader ? headerFill : null,
                    Padding = new Thickness(8, 4, 8, 4)
                };

                var cellSpan = isHeader ? Math.Max(GetSpan(cell), 1) : 1;
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex);
                if (cellSpan > 1) Grid.SetColumnSpan(border, Math.Min(cellSpan, columnCount - columnIndex));

                _context.PushContainer(cellPanel);
                if (isHeader)
                {
                    var bold = new Bold();
                    var boldFontFamily = _context.GetProperty<FontFamily>(RenderPropertyKeys.BoldFontFamily);
                    if (boldFontFamily is not null) bold.FontFamily = boldFontFamily;
                    _context.BeginInlineContainer(bold);
                    RenderChildren(cell);
                    _context.EndInlineContainer();
                }
                else
                {
                    RenderChildren(cell);
                }

                _context.PopContainer();

                // 与 Markdown 表格一致:抹平首尾外边距,避免文字在单元格里偏上
                RenderHelper.TrimVerticalEdgeMargins(cellPanel);

                grid.Children.Add(border);
                columnIndex += cellSpan;
            }
        }

        _context.AddToContainer(grid);
    }

    /// <summary><c>&lt;details&gt;/&lt;summary&gt;</c>:可折叠区块(不带第三方控件,用 ToggleButton + 面板实现)。</summary>
    private void RenderDetails(IElement element)
    {
        var summary = element.Children.FirstOrDefault(c => c.LocalName.Equals("summary", StringComparison.OrdinalIgnoreCase));
        var content = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };

        _context.PushContainer(content);
        foreach (var child in element.Children)
            if (!child.LocalName.Equals("summary", StringComparison.OrdinalIgnoreCase))
                RenderNode(child);
        _context.PopContainer();

        var toggle = new ToggleButton
        {
            Content = summary?.TextContent.Trim() is { Length: > 0 } text ? text : "详情",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4)
        };

        toggle.Checked += (_, _) => content.Visibility = Visibility.Visible;
        toggle.Unchecked += (_, _) => content.Visibility = Visibility.Collapsed;

        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        root.Children.Add(toggle);
        root.Children.Add(content);
        _context.AddToContainer(root);
    }

    private void RenderImage(IElement element)
    {
        var src = element.GetAttribute("src");
        if (!HtmlTagFacts.IsSafeImageUrl(src))
        {
            // 不可信的图片地址:不加载,按原文显示
            EmitRaw(element.OuterHtml);
            return;
        }

        var alt = element.GetAttribute("alt") ?? string.Empty;
        var width = GetLength(element, "width");
        var height = GetLength(element, "height");

        // 行内上下文(如 <span><img/></span>):留行内,避免打断段落。
        // 若外层是 <a>,Hyperlink 不接受 InlineUIContainer(WinUI 限制:E_INVALIDARG),
        // 必须退出链接、按【块级】图片落地 —— 这也与 UBB 的 [url][img][/url] 行为一致。
        // (注意不能套用行内图片工厂:它会给未指尺寸的图片按行高限高,落地成块级后会变得很小。)
        if (_inlineDepth > 0 && !_context.IsInsideHyperlink)
        {
            _context.AddInline(ElementFactory.CreateInlineImage(_context, src!, alt, width, height));
            return;
        }

        _context.FinalizeCurrentTextBlock();
        // 祖先声明了 align / text-align 时(如 <a align=center><img/></a>),图片跟着对齐
        _context.AddBlockElement(ElementFactory.CreateBlockImage(_context, src!, alt, width, height));
    }

    private void RenderSeparator()
    {
        _context.AddToContainer(new Border
        {
            Height = 1,
            Background = _context.GetBrush(RenderPropertyKeys.SubtleBrush,
                BrushHelper.CreateSubtleBrush(_context.Control.Foreground, 0x40)),
            Margin = new Thickness(0, 10, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
    }

    #endregion

    #region 辅助

    /// <summary>把整个元素(标签 + 内容)作为纯文本输出。</summary>
    private void EmitRaw(string? html)
    {
        if (string.IsNullOrEmpty(html)) return;
        _context.HtmlMapper.ProcessText(html);
    }

    /// <summary>把 DOM 元素序列化回标签原文(属性值做转义,交给行内状态机时不会被二次解码)。</summary>
    private static string ToStartTag(IElement element)
    {
        var builder = new StringBuilder("<").Append(element.LocalName);

        foreach (var attribute in element.Attributes)
        {
            builder.Append(' ').Append(attribute.Name).Append("=\"");
            builder.Append(attribute.Value.Replace("&", "&amp;").Replace("\"", "&quot;"));
            builder.Append('"');
        }

        return builder.Append('>').ToString();
    }

    /// <summary>
    ///     在元素声明的对齐下渲染子内容:文本对齐落到当前文本块,块级子元素(图片等)由
    ///     <see cref="_blockAlignment" /> 带上。
    /// </summary>
    private void RenderChildrenAligned(IElement element)
    {
        var previous = _context.BlockAlignment;

        // 元素显式声明的对齐优先;<center> 这类标签自带对齐语义,没有声明时用它兜底
        if (HtmlAlignment.TryParse(element.GetAttribute("style"), element.GetAttribute("align"), out var declared))
            _context.BlockAlignment = declared;
        else if (HtmlAlignment.DefaultFor(element.LocalName) is { } implicitAlignment)
            _context.BlockAlignment = implicitAlignment;

        // 尚未创建文本块时先建一个,否则对齐会落到上一段
        if (_context.BlockAlignment is { } alignment)
        {
            if (_context.CurrentRichTextBlock is null) _context.StartNewTextBlock();
            if (_context.CurrentRichTextBlock is { } richTextBlock)
                richTextBlock.TextAlignment = HtmlAlignment.ToTextAlignment(alignment);
        }

        RenderChildren(element);
        _context.BlockAlignment = previous;
    }

    /// <summary>读取 HTML 长度属性(width / height)。</summary>
    private static double? GetLength(IElement element, string name)
    {
        return HtmlTagFacts.ParsePixelLength(element.GetAttribute(name));
    }

    /// <summary>把容器级 <c>background-color</c> 落到段落背景上(用 Border 包一层)。</summary>
    /// <returns>是否已经渲染并消费了子节点(调用方据此避免重复渲染)。</returns>
    private bool ApplyBlockBackground(IElement element)
    {
        var style = HtmlStyle.Parse(element.GetAttribute("style"));
        if (!style.TryGetValue("background-color", out var value)) return false;
        if (!HtmlStyle.TryParseColor(value, out var color)) return false;

        _context.FinalizeCurrentTextBlock();
        var panel = new StackPanel();
        _context.PushContainer(panel);
        RenderChildrenAligned(element);
        _context.PopContainer();

        _context.AddToContainer(new Border
        {
            Child = panel,
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        return true;
    }

    private static int GetSpan(IElement cell)
    {
        return int.TryParse(cell.GetAttribute("colspan"), out var span) ? span : 1;
    }

    #endregion
}
