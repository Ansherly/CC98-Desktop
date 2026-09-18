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
    public Stack<Inline> InlineStack = new();
    public UbbTextBlock Control { get; set; }

    public Panel Container { get; set; }

    // Use RichTextBlock to support InlineUIContainer children
    public RichTextBlock CurrentRichTextBlock { get; set; }
    private Paragraph CurrentParagraph { get; set; }

    public Stack<Panel> PanelStack { get; set; } = new();
    public int QuoteNestingLevel { get; set; }
    public Dictionary<string, object> Properties { get; set; } = [];

    public void RenderNode(UbbNode node)
    {
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
        //如果有当前正在构建的内联容器，根据容器类型添加到其中
        if (CurrentInline != null)
        {
            if (CurrentInline is Span span)
                span.Inlines.Add(inline);
            else if (CurrentInline is Bold bold)
                bold.Inlines.Add(inline);
            else if (CurrentInline is Italic italic)
                italic.Inlines.Add(inline);
            else if (CurrentInline is Underline underline)
                underline.Inlines.Add(inline);
            else if (CurrentInline is Hyperlink hyperlink) hyperlink.Inlines.Add(inline);
        }
        else
        {
            // 否则添加到当前段落
            CurrentParagraph?.Inlines.Add(inline);
        }
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
            // 从栈中取出父容器
            var parentContainer = InlineStack.Pop();

            // 将当前容器添加到父容器
            if (parentContainer is Span parentSpan)
            {
                parentSpan.Inlines.Add(completedInline);
                CurrentInline = parentSpan;
            }
            else if (parentContainer is Bold parentBold)
            {
                parentBold.Inlines.Add(completedInline);
                CurrentInline = parentBold;
            }
            else if (parentContainer is Italic parentItalic)
            {
                parentItalic.Inlines.Add(completedInline);
                CurrentInline = parentItalic;
            }
            //其他容器类型
            else if (parentContainer is Underline parentUnderline)
            {
                parentUnderline.Inlines.Add(completedInline);
                CurrentInline = parentUnderline;
            }
            else if (parentContainer is Hyperlink parentHyperlink)
            {
                parentHyperlink.Inlines.Add(completedInline);
                CurrentInline = parentHyperlink;
            }
        }
        else
        {
            // 如果没有父容器，添加到当前段落
            if (CurrentParagraph == null || CurrentRichTextBlock == null)
            {
                //直接创建新的 RichTextBlock，避免循环调用 StartNewTextBlock()
                CurrentRichTextBlock = new()
                {
                    FontSize = Control.FontSize,
                    Foreground = Control.Foreground, // 为空则继承外层,不写死颜色
                    TextWrapping = TextWrapping.Wrap
                };
                CurrentParagraph = new();
                CurrentRichTextBlock.Blocks.Add(CurrentParagraph);
            }

            CurrentParagraph?.Inlines.Add(completedInline);
            CurrentInline = null;
        }
    }


    public void AddToContainer(UIElement element)
    {
        while (CurrentInline != null) EndInlineContainer();
        FinalizeCurrentTextBlock();
        Container.Children.Add(element);
    }

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

        CurrentRichTextBlock = new()
        {
            FontSize = Control.FontSize,
            // Foreground 为空时不要赋值:交给外层继承,避免在深色主题下写死成黑色
            Foreground = Control.Foreground,
            TextWrapping = TextWrapping.Wrap,
        };

        CurrentParagraph = new();
        CurrentRichTextBlock.Blocks.Add(CurrentParagraph);
    }

    
}