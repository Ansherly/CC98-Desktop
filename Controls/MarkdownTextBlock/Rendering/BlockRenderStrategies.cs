using System;
using System.Collections.Generic;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using CC98.Controls.MarkdownTextBlock.Common;
using CodeBlockControl = CC98.Controls.CodeBlock;

namespace CC98.Controls.MarkdownTextBlock.Rendering;

#region 标题

/// <summary>标题(H1–H6):按层级缩放字号并加粗。</summary>
public class HeadingRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not HeadingBlock heading) return;

        var baseFontSize = context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;
        var fontSize = baseFontSize * RenderHelper.GetHeadingScale(heading.Level);

        context.StartNewTextBlock(heading.Level <= 2 ? 10 : 6);
        if (context.CurrentRichTextBlock is { } richTextBlock)
            richTextBlock.Margin = new Thickness(context.Indent, heading.Level == 1 ? 14 : 10, 0,
                richTextBlock.Margin.Bottom);

        var span = new Span { FontSize = fontSize, FontWeight = FontWeights.SemiBold };
        var boldFontFamily = context.GetProperty<FontFamily>(RenderPropertyKeys.BoldFontFamily);
        if (boldFontFamily is not null) span.FontFamily = boldFontFamily;
        context.BeginInlineContainer(span);
        context.RenderChildren(heading);
        context.EndInlineContainer();
        context.FinalizeCurrentTextBlock();
    }
}

#endregion

#region 段落

/// <summary>段落。</summary>
public class ParagraphRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        context.StartNewTextBlock();
        context.RenderChildren(node);
        context.FinalizeCurrentTextBlock();
    }
}

#endregion

#region 引用块

/// <summary>引用块:左侧强调竖条 + 引用背景色(仅最外层使用背景色)。</summary>
public class QuoteRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        var isOutermost = context.QuoteNestingLevel == 0;

        var contentPanel = new StackPanel { Spacing = 0 };
        var border = new Border
        {
            Child = contentPanel,
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = context.GetBrush(RenderPropertyKeys.QuoteBorderBrush,
                BrushHelper.CreateSubtleBrush(context.Control.Foreground, 0x99)),
            Background = isOutermost ? context.GetProperty<Brush>(RenderPropertyKeys.QuoteBackground) : null,
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(isOutermost ? 12 : 8, 8, 8, 8),
            Margin = new Thickness(0, isOutermost ? 4 : 2, 0, isOutermost ? 10 : 2),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var previousSpacing = context.ParagraphSpacing;

        context.QuoteNestingLevel++;
        context.ParagraphSpacing = isOutermost ? 6 : 4;
        context.PushContainer(contentPanel);
        context.RenderChildren(node);
        context.PopContainer();
        context.ParagraphSpacing = previousSpacing;
        context.QuoteNestingLevel--;

        context.AddToContainer(border);
    }
}

#endregion

#region 列表

/// <summary>列表(有序 / 无序,支持嵌套与 GFM 任务列表)。</summary>
public class ListRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not ListBlock list) return;

        var start = 1;
        if (list.IsOrdered && int.TryParse(list.OrderedStart, out var parsedStart)) start = parsedStart;

        var index = 0;
        foreach (var child in list)
        {
            if (child is not ListItemBlock item) continue;

            var marker = list.IsOrdered
                ? $"{start + index}."
                : GetBullet(context.ListNestingLevel);

            index++;
            RenderItem(item, marker, list.IsOrdered, context);
        }
    }

    private static void RenderItem(ListItemBlock item, string marker, bool isOrdered, RenderContext context)
    {
        var baseFontSize = context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;

        var markerText = new TextBlock
        {
            Text = marker,
            FontSize = baseFontSize,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            MinWidth = isOrdered ? 18 : 10,
            TextAlignment = isOrdered ? TextAlignment.Right : TextAlignment.Center
        };

        if (context.Control.FontFamily is not null) markerText.FontFamily = context.Control.FontFamily;
        if (context.Control.Foreground is not null) markerText.Foreground = context.Control.Foreground;

        var contentPanel = new StackPanel();
        var grid = new Grid
        {
            Margin = new Thickness(context.Indent, 0, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(markerText);
        grid.Children.Add(contentPanel);
        Grid.SetColumn(contentPanel, 1);

        var previousSpacing = context.ParagraphSpacing;
        var previousIndent = context.Indent;

        context.ListNestingLevel++;
        context.ParagraphSpacing = 4;
        context.Indent = 0; // 缩进由 marker 列承担
        context.PushContainer(contentPanel);
        context.RenderChildren(item);
        context.PopContainer();
        context.Indent = previousIndent;
        context.ParagraphSpacing = previousSpacing;
        context.ListNestingLevel--;

        context.AddToContainer(grid);
    }

    private static string GetBullet(int nestingLevel)
    {
        return (nestingLevel % 3) switch
        {
            1 => "◦",
            2 => "▪",
            _ => "•"
        };
    }
}

/// <summary>列表项被独立渲染时(理论上不会发生)的兜底策略。</summary>
public class ListItemRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        context.RenderChildren(node);
    }
}

#endregion

#region 代码块

/// <summary>代码块:ColorCode 语法高亮 + 代码背景色。</summary>
public class CodeBlockRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not LeafBlock leaf) return;

        var code = RenderHelper.GetLeafText(leaf);
        var language = leaf is FencedCodeBlock fenced
            ? RenderHelper.GetLanguage(fenced.Info)
            : "plaintext";

        var background = context.GetProperty<Brush>(RenderPropertyKeys.CodeBackground);
        var borderBrush = context.GetBrush(RenderPropertyKeys.BorderBrush,
            BrushHelper.CreateSubtleBrush(context.Control.Foreground, 0x33));

        var viewer = new CodeBlockControl
        {
            Code = code,
            LanguageName = language,
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 10),
            Margin = new Thickness(0, 4, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        context.AddToContainer(viewer);
    }
}

#endregion

#region 分割线

/// <summary>分割线。</summary>
public class ThematicBreakRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        var rule = new Border
        {
            Height = 1,
            Background = context.GetBrush(RenderPropertyKeys.SubtleBrush,
                BrushHelper.CreateSubtleBrush(context.Control.Foreground, 0x40)),
            Margin = new Thickness(0, 10, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        context.AddToContainer(rule);
    }
}

#endregion

#region 表格

/// <summary>GFM 表格:等宽列 Grid + 单元格边框 + 表头填充。</summary>
public class TableRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not Table table) return;

        var rows = new List<TableRow>();
        foreach (var child in table)
            if (child is TableRow row)
                rows.Add(row);

        if (rows.Count == 0) return;

        var columnCount = table.ColumnDefinitions.Count;
        foreach (var row in rows)
        {
            var cells = 0;
            foreach (var child in row)
                if (child is TableCell)
                    cells++;

            columnCount = Math.Max(columnCount, cells);
        }

        if (columnCount == 0) return;

        var borderBrush = context.GetBrush(RenderPropertyKeys.BorderBrush,
            BrushHelper.CreateSubtleBrush(context.Control.Foreground, 0x40));
        var headerFill = BrushHelper.CreateFillBrush(context.Control.Foreground);

        var grid = new Grid
        {
            Margin = new Thickness(context.Indent, 6, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        for (var i = 0; i < columnCount; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var columnIndex = 0;
            foreach (var child in row)
            {
                if (child is not TableCell cell) continue;

                var targetColumn = cell.ColumnIndex >= 0 ? cell.ColumnIndex : columnIndex;
                columnIndex = targetColumn + 1;

                var alignment = targetColumn < table.ColumnDefinitions.Count
                    ? table.ColumnDefinitions[targetColumn].Alignment
                    : null;

                var cellBorder = CreateCell(context, cell, row.IsHeader, alignment, borderBrush, headerFill);

                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetColumn(cellBorder, targetColumn);
                Grid.SetColumnSpan(cellBorder, Math.Max(1, cell.ColumnSpan));
                grid.Children.Add(cellBorder);
            }
        }

        context.AddToContainer(grid);
    }

    private static Border CreateCell(RenderContext context, TableCell cell, bool isHeader,
        TableColumnAlign? alignment, Brush borderBrush, Brush headerFill)
    {
        var contentPanel = new StackPanel();
        var cellBorder = new Border
        {
            Child = contentPanel,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Background = isHeader ? headerFill : null,
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var childContext = context.CreateChildContext(contentPanel);
        foreach (var child in cell) childContext.RenderNode(child);
        childContext.FinalizeCurrentTextBlock();

        // 单元格对齐(基于列定义)
        var textAlignment = alignment switch
        {
            TableColumnAlign.Center => TextAlignment.Center,
            TableColumnAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        if (isHeader || textAlignment != TextAlignment.Left)
            foreach (var child in contentPanel.Children)
                if (child is RichTextBlock richTextBlock)
                {
                    richTextBlock.TextAlignment = textAlignment;
                    if (isHeader)
                        foreach (var block in richTextBlock.Blocks)
                            if (block is Paragraph paragraph)
                                foreach (var inline in paragraph.Inlines)
                                    if (inline is Span span) span.FontWeight = FontWeights.SemiBold;
                                    else if (inline is Run run) run.FontWeight = FontWeights.SemiBold;
                }

        return cellBorder;
    }
}

/// <summary>表格行(由 <see cref="TableRenderStrategy" /> 统一处理)。</summary>
public class TableRowRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        context.RenderChildren(node);
    }
}

/// <summary>表格单元格(由 <see cref="TableRenderStrategy" /> 统一处理)。</summary>
public class TableCellRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        context.RenderChildren(node);
    }
}

#endregion

#region HTML 块

/// <summary>HTML 块:走白名单状态机,逐段解析标签与文本。</summary>
public class HtmlBlockRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not LeafBlock leaf) return;

        var html = RenderHelper.GetLeafText(leaf);
        context.StartNewTextBlock();
        context.HtmlMapper.ProcessHtmlBlock(html);
        context.FinalizeCurrentTextBlock();
    }
}

#endregion

#region 数学

/// <summary>
///     块级公式($$...$$)。
///     MathBlock 派生自 FencedCodeBlock(因此不会命中 CodeBlockRenderStrategy 的
///     精确类型匹配),内容同样存放在 Lines 中,复用围栏块的取文本逻辑。
///     内容可能多行,LatexBlock 的约定是「每行一个独立公式」,原样透传即可。
/// </summary>
public class MathBlockRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not MathBlock mathBlock) return;

        var latex = RenderHelper.GetLeafText(mathBlock).Trim();
        if (latex.Length == 0) return;

        context.AddToContainer(ElementFactory.CreateBlockLatex(context, latex));
    }
}

#endregion
