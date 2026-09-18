using System;
using System.Collections.Generic;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using CC98.Controls.MarkdownTextBlock.Common;
using CC98.Controls.MarkdownTextBlock.Common.Events;
using CC98.Controls.Picture;
using CC98.Controls.MarkdownTextBlock.Rendering;
using Windows.UI;
using Inline = Microsoft.UI.Xaml.Documents.Inline;

namespace CC98.Controls.MarkdownTextBlock;

/// <summary>
///     WinUI 3 的 Markdown 渲染控件。
///     架构参考 CC98.Controls.UbbTextBlock:Markdig 解析出 AST,再由策略字典渲染为
///     StackPanel 中交替排列的 RichTextBlock(文本流)与块级 UI 元素(图片 / 代码块 / 引用 / 表格)。
/// </summary>
public sealed partial class MarkdownTextBlock : Control
{
    private const string RootPanelPartName = "PART_RootPanel";

    private RenderContext? _context;
    private StackPanel? _rootPanel;

    public MarkdownTextBlock()
    {
        DefaultStyleKey = typeof(MarkdownTextBlock);
    }

    #region 渲染诊断(调试用)

    /// <summary>
    ///     渲染结果结构摘要:逐层列出生成的块级元素及其文本,便于排查渲染问题。
    /// </summary>
    public string GetRenderSummary()
    {
        if (_rootPanel is null) return "(控件模板尚未应用)";

        var builder = new System.Text.StringBuilder();
        DumpChildren(_rootPanel, builder, 0);
        return builder.ToString();
    }

    private static void DumpChildren(Panel panel, System.Text.StringBuilder builder, int depth)
    {
        var indent = new string(' ', depth * 2);

        foreach (var child in panel.Children)
            switch (child)
            {
                case RichTextBlock richTextBlock:
                {
                    var text = new System.Text.StringBuilder();
                    foreach (var block in richTextBlock.Blocks)
                        if (block is Paragraph paragraph)
                            foreach (var inline in paragraph.Inlines)
                                text.Append(DescribeInline(inline));

                    builder.AppendLine($"{indent}RichTextBlock  [{Truncate(text.ToString())}]");
                    break;
                }
                case CodeBlock codeBlock:
                    builder.AppendLine(
                        $"{indent}CodeBlock      [{codeBlock.LanguageName}] {Truncate(codeBlock.Code)} | {codeBlock.GetScrollDebugInfo()}");
                    break;
                case Controls.Picture.Picture picture:
                    builder.AppendLine($"{indent}Picture        {picture.Src}");
                    break;
                case Border border:
                    builder.AppendLine($"{indent}Border");
                    switch (border.Child)
                    {
                        case Panel nestedPanel:
                            DumpChildren(nestedPanel, builder, depth + 1);
                            break;
                        case CodeBlock codeBlock:
                            builder.AppendLine($"{indent}  CodeBlock    [{codeBlock.LanguageName}]");
                            break;
                    }

                    break;
                case Grid grid:
                    builder.AppendLine($"{indent}Grid ({grid.RowDefinitions.Count}行 × {grid.ColumnDefinitions.Count}列)");
                    foreach (var cell in grid.Children)
                        if (cell is TextBlock marker)
                            builder.AppendLine($"{indent}  Marker [{marker.Text}]");
                        else if (cell is Border { Child: Panel cellPanel })
                        {
                            builder.AppendLine($"{indent}  Cell");
                            DumpChildren(cellPanel, builder, depth + 2);
                        }

                    break;
                default:
                    builder.AppendLine($"{indent}{child.GetType().Name}");
                    break;
            }
    }

    private static string DescribeInline(Inline inline)
    {
        switch (inline)
        {
            case Run run:
                return run.Text;
            case LineBreak:
                return "\\n";
            case InlineUIContainer container:
                return $"[UI:{container.Child?.GetType().Name}]";
            case Span span:
            {
                var builder = new System.Text.StringBuilder();
                foreach (var child in span.Inlines) builder.Append(DescribeInline(child));
                return builder.ToString();
            }
            default:
                return string.Empty;
        }
    }

    private static string Truncate(string text)
    {
        text = text.Replace("\r", string.Empty).Replace("\n", "\\n");
        return text.Length <= 80 ? text : text[..80] + "…";
    }

    #endregion

    #region 全局配置

    /// <summary>Markdig 管线(默认开启 GFM 高级扩展与自动链接)。</summary>
    public static MarkdownPipeline Pipeline { get; set; } = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoLinks()
        .Build();

    /// <summary>图片加载器(默认支持远程 + 本地,宿主可替换)。</summary>
    public static IImageLoader ImageLoader { get; set; } = new DefaultImageLoader();

    /// <summary>渲染策略表:Markdig 节点类型 → 渲染策略。可在进程内替换单个策略以自定义渲染。</summary>
    public static readonly IReadOnlyDictionary<Type, IRenderStrategy> RenderStrategies =
        new Dictionary<Type, IRenderStrategy>
        {
            // 块级
            [typeof(HeadingBlock)] = new HeadingRenderStrategy(),
            [typeof(ParagraphBlock)] = new ParagraphRenderStrategy(),
            [typeof(QuoteBlock)] = new QuoteRenderStrategy(),
            [typeof(ListBlock)] = new ListRenderStrategy(),
            [typeof(ListItemBlock)] = new ListItemRenderStrategy(),
            [typeof(FencedCodeBlock)] = new CodeBlockRenderStrategy(),
            [typeof(CodeBlock)] = new CodeBlockRenderStrategy(),
            [typeof(ThematicBreakBlock)] = new ThematicBreakRenderStrategy(),
            [typeof(HtmlBlock)] = new HtmlBlockRenderStrategy(),
            [typeof(MathBlock)] = new MathBlockRenderStrategy(),
            [typeof(Table)] = new TableRenderStrategy(),
            [typeof(TableRow)] = new TableRowRenderStrategy(),
            [typeof(TableCell)] = new TableCellRenderStrategy(),

            // 行内
            [typeof(LiteralInline)] = new LiteralRenderStrategy(),
            [typeof(EmphasisInline)] = new EmphasisRenderStrategy(),
            [typeof(CodeInline)] = new CodeInlineRenderStrategy(),
            [typeof(LinkInline)] = new LinkRenderStrategy(),
            [typeof(AutolinkInline)] = new AutolinkRenderStrategy(),
            [typeof(LineBreakInline)] = new LineBreakRenderStrategy(),
            [typeof(HtmlInline)] = new HtmlInlineRenderStrategy(),
            [typeof(HtmlEntityInline)] = new HtmlEntityRenderStrategy(),
            [typeof(MathInline)] = new MathInlineRenderStrategy(),
            [typeof(Markdig.Extensions.TaskLists.TaskList)] = new TaskListRenderStrategy()
        };

    #endregion

    #region 依赖属性

    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.Register(
            nameof(MarkdownText),
            typeof(string),
            typeof(MarkdownTextBlock),
            new(string.Empty, OnRenderPropertyChanged));

    /// <summary>重新注册字体属性,以便变更时重建内容(与 UbbTextBlock 一致)。</summary>
    public new static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(
            nameof(FontSize),
            typeof(double),
            typeof(MarkdownTextBlock),
            new(14.0, OnRenderPropertyChanged));

    public new static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(
            nameof(FontFamily),
            typeof(FontFamily),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    public new static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(
            nameof(Foreground),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>粗体字体。为空时使用系统默认字体的粗体。</summary>
    public static readonly DependencyProperty BoldFontFamilyProperty =
        DependencyProperty.Register(
            nameof(BoldFontFamily),
            typeof(FontFamily),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>代码块背景。</summary>
    public static readonly DependencyProperty CodeBackgroundProperty =
        DependencyProperty.Register(
            nameof(CodeBackground),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>引用块背景(仅最外层引用块生效,嵌套引用透明)。</summary>
    public static readonly DependencyProperty QuoteBackgroundProperty =
        DependencyProperty.Register(
            nameof(QuoteBackground),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>引用块左侧竖条画刷(为空时由 <see cref="Control.Foreground" /> 推导)。</summary>
    public static readonly DependencyProperty QuoteBorderBrushProperty =
        DependencyProperty.Register(
            nameof(QuoteBorderBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>图片最大宽度。</summary>
    public static readonly DependencyProperty ImageMaxWidthProperty =
        DependencyProperty.Register(
            nameof(ImageMaxWidth),
            typeof(double),
            typeof(MarkdownTextBlock),
            new(double.PositiveInfinity, OnRenderPropertyChanged));

    /// <summary>图片最大高度。</summary>
    public static readonly DependencyProperty ImageMaxHeightProperty =
        DependencyProperty.Register(
            nameof(ImageMaxHeight),
            typeof(double),
            typeof(MarkdownTextBlock),
            new(double.PositiveInfinity, OnRenderPropertyChanged));

    /// <summary>是否允许选中文本。</summary>
    public static readonly DependencyProperty IsSelectionEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSelectionEnabled),
            typeof(bool),
            typeof(MarkdownTextBlock),
            new(true, OnRenderPropertyChanged));

    /// <summary>Markdown 原文。</summary>
    public string MarkdownText
    {
        get => (string)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    public new double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public new FontFamily? FontFamily
    {
        get => GetValue(FontFamilyProperty) as FontFamily;
        set => SetValue(FontFamilyProperty, value);
    }

    public new Brush? Foreground
    {
        get => GetValue(ForegroundProperty) as Brush;
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily? BoldFontFamily
    {
        get => GetValue(BoldFontFamilyProperty) as FontFamily;
        set => SetValue(BoldFontFamilyProperty, value);
    }

    public Brush? CodeBackground
    {
        get => GetValue(CodeBackgroundProperty) as Brush;
        set => SetValue(CodeBackgroundProperty, value);
    }

    public Brush? QuoteBackground
    {
        get => GetValue(QuoteBackgroundProperty) as Brush;
        set => SetValue(QuoteBackgroundProperty, value);
    }

    public Brush? QuoteBorderBrush
    {
        get => GetValue(QuoteBorderBrushProperty) as Brush;
        set => SetValue(QuoteBorderBrushProperty, value);
    }

    public double ImageMaxWidth
    {
        get => (double)GetValue(ImageMaxWidthProperty);
        set => SetValue(ImageMaxWidthProperty, value);
    }

    public double ImageMaxHeight
    {
        get => (double)GetValue(ImageMaxHeightProperty);
        set => SetValue(ImageMaxHeightProperty, value);
    }

    public bool IsSelectionEnabled
    {
        get => (bool)GetValue(IsSelectionEnabledProperty);
        set => SetValue(IsSelectionEnabledProperty, value);
    }

    #endregion

    #region 事件

    /// <summary>链接点击(Markdown 链接 / 自动链接 / HTML &lt;a&gt;)。控件本身不做导航。</summary>
    public event EventHandler<LinkClickedEventArgs>? LinkClicked;

    /// <summary>图片点击(Markdown 图片 / HTML &lt;img&gt;)。</summary>
    public event EventHandler<ImageClickedEventArgs>? ImageClicked;

    public void RaiseLinkClicked(string url, string text, bool isAutolink = false)
    {
        LinkClicked?.Invoke(this, new LinkClickedEventArgs(url ?? string.Empty, text ?? string.Empty, isAutolink));
    }

    public void RaiseImageClicked(string src, string alt)
    {
        ImageClicked?.Invoke(this, new ImageClickedEventArgs(src ?? string.Empty, alt ?? string.Empty));
    }

    #endregion

    #region 渲染

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _rootPanel = GetTemplateChild(RootPanelPartName) as StackPanel;
        RenderContent();
    }

    private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextBlock control) control.RenderContent();
    }

    /// <summary>重新解析并重建内容。</summary>
    public void RenderContent()
    {
        if (_rootPanel is null) return;

        _rootPanel.Children.Clear();

        var text = MarkdownText;
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            var document = Markdown.Parse(text, Pipeline);

            _context = new RenderContext
            {
                Control = this,
                Container = _rootPanel
            };

            FillProperties(_context);

            // 逐个顶层节点渲染:单个节点出错不影响其余内容
            foreach (var node in document)
                try
                {
                    _context.RenderNode(node);
                }
                catch (Exception nodeException)
                {
                    _context.FinalizeCurrentTextBlock();
                    _rootPanel.Children.Add(CreateErrorText($"{node.GetType().Name} 渲染失败:{nodeException.Message}"));
                }

            _context.FinalizeCurrentTextBlock();
        }
        catch (Exception ex)
        {
            _rootPanel.Children.Add(CreateErrorText($"Markdown 渲染失败:{ex.Message}"));
        }
    }

    private static TextBlock CreateErrorText(string message)
    {
        return new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6),
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C))
        };
    }

    /// <summary>
    ///     填充本次渲染使用的属性袋。
    /// </summary>
    private void FillProperties(RenderContext context)
    {
        var properties = context.Properties;

        properties[RenderPropertyKeys.FontSize] = FontSize;
        properties[RenderPropertyKeys.Foreground] = Foreground;
        properties[RenderPropertyKeys.BoldFontFamily] = BoldFontFamily;
        properties[RenderPropertyKeys.ImageMaxWidth] = ImageMaxWidth;
        properties[RenderPropertyKeys.ImageMaxHeight] = ImageMaxHeight;

        properties[RenderPropertyKeys.CodeBackground] =
            CodeBackground ?? new SolidColorBrush(Color.FromArgb(0x1F, 0x88, 0x88, 0x88));

        properties[RenderPropertyKeys.QuoteBackground] =
            QuoteBackground ?? new SolidColorBrush(Color.FromArgb(0x14, 0x88, 0x88, 0x88));

        properties[RenderPropertyKeys.QuoteBorderBrush] =
            QuoteBorderBrush ?? BrushHelper.CreateSubtleBrush(Foreground, 0x99);

        properties[RenderPropertyKeys.BorderBrush] =
            BrushHelper.CreateSubtleBrush(Foreground, 0x40);

        properties[RenderPropertyKeys.SubtleBrush] =
            BrushHelper.CreateSubtleBrush(Foreground, 0x40);

        properties[RenderPropertyKeys.MonoFontFamily] =
            new FontFamily(MonoFontFamilyNames);
    }

    /// <summary>等宽字体(行内代码与代码块使用)。</summary>
    private const string MonoFontFamilyNames = "Consolas";

    #endregion
}
