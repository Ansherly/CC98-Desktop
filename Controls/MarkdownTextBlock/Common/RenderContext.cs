using System.Collections.Generic;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Inline = Microsoft.UI.Xaml.Documents.Inline;

namespace CC98.Controls.MarkdownTextBlock.Common;

/// <summary>
///     渲染上下文。持有当前正在构建的文本块 / 内联容器 / 目标面板,
///     对应 UbbTextBlock 架构中的四原语:FinalizeCurrentTextBlock / BeginInlineContainer / EndInlineContainer / AddToContainer。
/// </summary>
public class RenderContext
{
    private readonly Stack<Panel> _containerStack = new();

    /// <summary>当前正在构建的内联容器(Bold/Italic/Span/Hyperlink)。</summary>
    private Inline? _currentInline;

    public RenderContext()
    {
        HtmlMapper = new HtmlTagMapper(this);
    }

    /// <summary>HTML 白名单标签映射器(每个上下文独立维护状态)。</summary>
    public HtmlTagMapper HtmlMapper { get; }

    /// <summary>所属控件。</summary>
    public MarkdownTextBlock Control { get; set; } = null!;

    /// <summary>当前块级元素的目标面板。</summary>
    public Panel Container { get; set; } = null!;

    /// <summary>渲染期共享属性(字体、颜色、图片尺寸限制等)。</summary>
    public Dictionary<string, object?> Properties { get; set; } = new();

    /// <summary>内联容器栈。</summary>
    public Stack<Inline> InlineStack { get; } = new();

    /// <summary>当前正在构建的 RichTextBlock。</summary>
    public RichTextBlock? CurrentRichTextBlock { get; private set; }

    /// <summary>当前正在构建的 Paragraph。</summary>
    public Paragraph? CurrentParagraph { get; private set; }

    /// <summary>引用嵌套层级(仅最外层使用引用块背景色)。</summary>
    public int QuoteNestingLevel { get; set; }

    /// <summary>列表嵌套层级(用于缩进与项目符号样式)。</summary>
    public int ListNestingLevel { get; set; }

    /// <summary>当前块级元素的左侧缩进(列表 / 嵌套内容)。</summary>
    public double Indent { get; set; }

    /// <summary>段落之间的垂直间距。</summary>
    public double ParagraphSpacing { get; set; } = 8;

    #region 节点分发

    /// <summary>
    ///     按节点类型查找渲染策略;未注册类型则透明穿透,直接渲染子节点。
    /// </summary>
    public void RenderNode(MarkdownObject node)
    {
        if (MarkdownTextBlock.RenderStrategies.TryGetValue(node.GetType(), out var strategy))
            strategy.Render(node, this);
        else
            RenderChildren(node);
    }

    /// <summary>
    ///     渲染节点的子节点(容器块 / 叶子块的行内 / 容器行内)。
    /// </summary>
    public void RenderChildren(MarkdownObject node)
    {
        switch (node)
        {
            case ContainerBlock containerBlock:
                foreach (var child in containerBlock) RenderNode(child);
                break;
            case LeafBlock { Inline: not null } leafBlock:
                foreach (var child in leafBlock.Inline) RenderNode(child);
                break;
            case ContainerInline containerInline:
                foreach (var child in containerInline) RenderNode(child);
                break;
        }
    }

    #endregion

    #region 行内渲染原语

    /// <summary>
    ///     向当前文本块或内联容器添加内联元素(会自动接续当前正在构建的内联容器)。
    /// </summary>
    public void AddInline(Inline inline)
    {
        EnsureTextBlock();

        if (_currentInline is Span span)
            span.Inlines.Add(inline);
        else
            CurrentParagraph!.Inlines.Add(inline);
    }

    /// <summary>
    ///     开始构建新的内联容器(Bold / Italic / Span / Hyperlink)。
    /// </summary>
    public void BeginInlineContainer(Inline container)
    {
        EnsureTextBlock();

        if (_currentInline is not null) InlineStack.Push(_currentInline);
        _currentInline = container;
    }

    /// <summary>
    ///     结束当前内联容器的构建,并挂载到父容器或当前段落。
    /// </summary>
    public void EndInlineContainer()
    {
        if (_currentInline is null) return;

        var completed = _currentInline;
        if (InlineStack.Count > 0)
        {
            var parent = InlineStack.Pop();
            if (parent is Span parentSpan) parentSpan.Inlines.Add(completed);
            _currentInline = parent;
            return;
        }

        EnsureTextBlock();
        CurrentParagraph!.Inlines.Add(completed);
        _currentInline = null;
    }

    #endregion

    #region 块级渲染原语

    /// <summary>
    ///     添加块级 UI 元素(会先收尾所有未完成的内联 / 文本块)。
    /// </summary>
    public void AddToContainer(UIElement element)
    {
        while (_currentInline is not null) EndInlineContainer();
        FinalizeCurrentTextBlock();
        Container.Children.Add(element);
    }

    /// <summary>
    ///     收尾当前文本块。
    /// </summary>
    public void FinalizeCurrentTextBlock()
    {
        while (_currentInline is not null) EndInlineContainer();
        InlineStack.Clear();
        _currentInline = null;

        if (CurrentRichTextBlock is not null && CurrentParagraph is { } paragraph && paragraph.Inlines.Count > 0)
            Container.Children.Add(CurrentRichTextBlock);

        CurrentRichTextBlock = null;
        CurrentParagraph = null;
    }

    /// <summary>
    ///     开启新的文本块并设置其外边距(左侧缩进 + 底部段落间距)。
    /// </summary>
    public void StartNewTextBlock(double? spacing = null)
    {
        FinalizeCurrentTextBlock();

        var control = Control;
        var bottom = spacing ?? ParagraphSpacing;

        var richTextBlock = new RichTextBlock
        {
            FontSize = control.FontSize,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = control.IsSelectionEnabled,
            Margin = new Thickness(Indent, 0, 0, bottom)
        };

        // 为空时不设置,交由视觉树继承主题默认值
        if (control.FontFamily is not null) richTextBlock.FontFamily = control.FontFamily;
        if (control.Foreground is not null) richTextBlock.Foreground = control.Foreground;

        CurrentRichTextBlock = richTextBlock;
        CurrentParagraph = new Paragraph();
        CurrentRichTextBlock.Blocks.Add(CurrentParagraph);
    }

    /// <summary>
    ///     切入嵌套块级容器(引用块 / 列表项 / 表格单元格以外的场景)。
    /// </summary>
    public void PushContainer(Panel container)
    {
        while (_currentInline is not null) EndInlineContainer();
        FinalizeCurrentTextBlock();

        _containerStack.Push(Container);
        Container = container;
    }

    /// <summary>
    ///     恢复到上一层块级容器。
    /// </summary>
    public void PopContainer()
    {
        while (_currentInline is not null) EndInlineContainer();
        FinalizeCurrentTextBlock();

        if (_containerStack.Count > 0) Container = _containerStack.Pop();
    }

    /// <summary>
    ///     确保存在可用的文本块。
    /// </summary>
    private void EnsureTextBlock()
    {
        if (CurrentRichTextBlock is null || CurrentParagraph is null) StartNewTextBlock();
    }

    #endregion

    /// <summary>
    ///     创建一个共享渲染状态的子上下文(用于表格单元格等隔离场景)。
    /// </summary>
    public RenderContext CreateChildContext(Panel container)
    {
        return new RenderContext
        {
            Control = Control,
            Container = container,
            Properties = Properties,
            QuoteNestingLevel = QuoteNestingLevel,
            ListNestingLevel = ListNestingLevel,
            Indent = 0,
            ParagraphSpacing = 2
        };
    }

    #region 属性访问辅助

    public T? GetProperty<T>(string key)
    {
        return Properties.TryGetValue(key, out var value) && value is T typed ? typed : default;
    }

    public Brush GetBrush(string key, Brush fallback)
    {
        return GetProperty<Brush>(key) ?? fallback;
    }

    #endregion
}
