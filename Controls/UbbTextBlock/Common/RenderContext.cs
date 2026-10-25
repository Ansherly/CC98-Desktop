using System.Collections.Generic;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using CC98.Controls.UbbTextBlock.Parser;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace CC98.Controls.UbbTextBlock.Common;

public class RenderContext
{
    // 临时存储当前正在构建的内联容器
    private Inline CurrentInline;

    /// <summary>未完成的内联容器栈(仅渲染实现内部使用,不对外暴露)。</summary>
    internal Stack<Inline> InlineStack = new();

    public UbbTextBlock Control { get; set; }

    public Panel Container { get; set; }

    // Use RichTextBlock to support InlineUIContainer children
    public RichTextBlock CurrentRichTextBlock { get; set; }
    private Paragraph CurrentParagraph { get; set; }

    public int QuoteNestingLevel { get; set; }
    public Dictionary<string, object> Properties { get; set; } = [];

    // 位于行内容器（如链接的 Hyperlink）内部时，需要忽略块级语义、直接行内渲染子节点的标签类型。
    // 这些标签的策略会终结当前文本块，把尚未填充内容的行内容器提前提交、架空链接。
    // 媒体类（图片/音视频等）没有行内形态、权限类（回复可见等）降级会绕过隐藏逻辑，均不在其列，
    // 它们按原有方式渲染（链接在该处终结，为既有行为）。
    private static readonly HashSet<UbbNodeType> InlineDegradableTypes =
    [
        UbbNodeType.Align, UbbNodeType.Left, UbbNodeType.Center, UbbNodeType.Right,
        UbbNodeType.Quote, UbbNodeType.Code, UbbNodeType.Paragraph,
        UbbNodeType.Table, UbbNodeType.TableRow, UbbNodeType.TableCell
    ];

    public void RenderNode(UbbNode node)
    {
        // 行内容器只能承载行内元素：此时忽略块级标签的块级语义（如对齐、引用样式），
        // 直接行内渲染其子节点，保住链接的完整性
        if (CurrentInline != null && InlineDegradableTypes.Contains(node.Type))
        {
            foreach (var child in node.Children)
                RenderNode(child);
            return;
        }

        if (UbbTextBlock.RenderStrategies.TryGetValue(node.Type, out var strategy))
            strategy.Render(node, this);
        else
            //当未匹配到渲染策略时，忽略此层级并渲染子节点
            foreach (var child in node.Children)
                RenderNode(child);
    }

    /// <summary>
    ///     向当前文本块或内联容器添加内联元素。
    /// </summary>
    /// <remarks>
    ///     此方法会续上接当前正在构建的内联容器（如 Bold、Italic 等）。
    /// </remarks>
    /// <param name="inline"></param>
    public void AddInline(Inline inline)
    {
        //如果还没有文本块，就新建一个
        if (CurrentParagraph == null || CurrentRichTextBlock == null) StartNewTextBlock();

        // 优先追加到正在构建的内联容器,否则落到当前段落
        var target = GetInlineCollection(CurrentInline) ?? CurrentParagraph?.Inlines;
        target?.Add(inline);
    }

    /// <summary>
    ///     取得内联元素承载子元素用的集合;非容器型内联元素返回 null。
    /// </summary>
    /// <remarks>
    ///     WinUI 里 Bold / Italic / Underline / Hyperlink 都派生自 Span,因此这里只需判断 Span 一种类型。
    /// </remarks>
    private static InlineCollection? GetInlineCollection(Inline? inline)
    {
        return inline is Span span ? span.Inlines : null;
    }

    /// <summary>
    ///     开始构建新的内联容器（如 Bold、Italic 等）
    /// </summary>
    /// <param name="container"></param>
    public void BeginInlineContainer(Inline container)
    {
        if (CurrentParagraph == null || CurrentRichTextBlock == null) StartNewTextBlock();
        if (CurrentInline != null) InlineStack.Push(CurrentInline);
        CurrentInline = container;
    }

    /// <summary>
    ///     结束当前内联容器的构建
    /// </summary>
    public void EndInlineContainer()
    {
        if (CurrentInline == null) return;

        var completedInline = CurrentInline;

        if (InlineStack.Count > 0)
        {
            // 从栈中取出父容器,把刚完成的容器挂进去
            var parentContainer = InlineStack.Pop();
            GetInlineCollection(parentContainer)?.Add(completedInline);
            CurrentInline = parentContainer;
            return;
        }

        // 没有父容器,直接落到当前段落
        if (CurrentParagraph == null || CurrentRichTextBlock == null)
        {
            // 这里不能调 StartNewTextBlock():它会回头再走一遍 EndInlineContainer
            CurrentRichTextBlock = CreateRichTextBlock();
            CurrentParagraph = new();
            CurrentRichTextBlock.Blocks.Add(CurrentParagraph);
        }

        CurrentParagraph.Inlines.Add(completedInline);
        CurrentInline = null;
    }


    public void AddToContainer(UIElement element)
    {
        while (CurrentInline != null) EndInlineContainer();
        FinalizeCurrentTextBlock();
        Container.Children.Add(element);
    }

    #region 嵌套容器

    // 嵌套块级容器时暂存的父容器(嵌套层级本身由调用栈表达)
    private readonly Stack<Panel> _containerStates = new();

    /// <summary>切入一个嵌套块级容器(引用 / 对齐等),与 <see cref="PopContainer" /> 成对使用。</summary>
    public void PushContainer(Panel container)
    {
        _containerStates.Push(Container);
        Container = container;
    }

    /// <summary>恢复到 <see cref="PushContainer" /> 之前的容器。</summary>
    public void PopContainer()
    {
        if (_containerStates.Count == 0) return;

        Container = _containerStates.Pop();
    }

    /// <summary>
    ///     为"独立渲染"的块创建子上下文(表格单元格、分层引用里的单条引用等):
    ///     共享控件引用与属性袋,但拥有独立的容器 / 段落 / 内联状态。
    /// </summary>
    public RenderContext CreateChildContext(Panel container)
    {
        return new RenderContext
        {
            Control = Control,
            Container = container,
            Properties = Properties,
            QuoteNestingLevel = QuoteNestingLevel
        };
    }

    #endregion

    //结束当前文本块的构建
    public void FinalizeCurrentTextBlock()
    {
        while (CurrentInline != null) EndInlineContainer();
        if (CurrentRichTextBlock != null && CurrentParagraph != null && CurrentParagraph.Inlines.Count != 0)
        {
            Container.Children.Add(CurrentRichTextBlock);
            CurrentRichTextBlock = null;
            CurrentParagraph = null;
        }

        //清理栈
        InlineStack.Clear();
        CurrentInline = null;
    }

    public void StartNewTextBlock()
    {
        FinalizeCurrentTextBlock();

        CurrentRichTextBlock = CreateRichTextBlock();
        CurrentParagraph = new();
        CurrentRichTextBlock.Blocks.Add(CurrentParagraph);
    }

    /// <summary>按控件当前的字号 / 前景色创建文本块(前景色为空时交给外层继承)。</summary>
    private RichTextBlock CreateRichTextBlock()
    {
        return new RichTextBlock
        {
            FontSize = Control.FontSize,
            Foreground = Control.Foreground,
            TextWrapping = TextWrapping.Wrap
        };
    }

    
}