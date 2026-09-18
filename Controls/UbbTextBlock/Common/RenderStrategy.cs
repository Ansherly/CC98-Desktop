using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.UI;
using Windows.UI.Text;
using CC98.Controls.Primitives;
using CC98.Controls.UbbTextBlock.Parser;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using CC98.Controls.Extensions;

namespace CC98.Controls.UbbTextBlock.Common;

// 渲染策略接口
public interface IRenderStrategy
{
    void Render(UbbNode node, RenderContext context);
}

public class TextRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TextNode textNode && !string.IsNullOrWhiteSpace(textNode.Content))
        {
            var content = textNode.Content;
            if (node.Parent.Type == UbbNodeType.Document)
            {
                var prevIsBlock = node.PreviousSibling?.Type.IsBlock() ?? false;
                var nextIsBlock = node.NextSibling?.Type.IsBlock() ?? false;

                if (prevIsBlock) content = content.TrimStart('\r', '\n');
                if (nextIsBlock) content = content.TrimEnd('\r', '\n');
            }

            var run = new Run { Text = content };
            context.AddInline(run);
        }
    }
}

// 粗体渲染策略
public class BoldRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        var bold = new Bold();

        // 字体为空时不能赋值:FontFamily 赋 null 会抛异常,且应以宿主设置为准
        var boldFontFamily = RenderHelper.GetFontFamily(context, "BoldFontFamily");
        if (boldFontFamily is not null) bold.FontFamily = boldFontFamily;

        context.BeginInlineContainer(bold);
        foreach (var child in node.Children) context.RenderNode(child);
        context.EndInlineContainer();
    }
}

// 斜体渲染策略
public class ItalicRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        var italic = new Italic();
        context.BeginInlineContainer(italic);
        foreach (var child in node.Children) context.RenderNode(child);
        context.EndInlineContainer();
    }
}

// 下划线渲染策略
public class UnderlineRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        var underline = new Underline();
        context.BeginInlineContainer(underline);
        foreach (var child in node.Children) context.RenderNode(child);
        context.EndInlineContainer();
    }
}

// 删除线渲染策略
public class StrikethroughRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        var span = new Span
        {
            TextDecorations = TextDecorations.Strikethrough
        };
        context.BeginInlineContainer(span);
        foreach (var child in node.Children) context.RenderNode(child);
        context.EndInlineContainer();
    }

    private static string CollectText(UbbNode node)
    {
        var text = "";
        foreach (var child in node.Children)
            if (child is TextNode textNode)
                text += textNode.Content;
            else
                text += CollectText(child);

        return text;
    }
}

// 字体大小渲染策略
public class SizeRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TagNode tagNode)
        {
            var sizeStr = tagNode.GetAttribute("size");
            if (int.TryParse(sizeStr, out var sizeInt))
            {
                var pixels = sizeStr.Contains("px") ? sizeInt : ConvertUbbSizeToPixels(sizeInt);
                var span = new Span { FontSize = pixels };
                context.BeginInlineContainer(span);
                foreach (var child in node.Children) context.RenderNode(child);
                context.EndInlineContainer();
            }
            else
            {
                // 默认处理子节点
                foreach (var child in node.Children) context.RenderNode(child);
            }
        }
    }

    private static double ConvertUbbSizeToPixels(int ubbSize)
    {
        // 简单的分段线性插值
        if (ubbSize <= 1) return 8; // 最小尺寸
        if (ubbSize == 2) return 10; // 较小
        if (ubbSize == 3) return 13;
        if (ubbSize == 4) return 17; // 插值
        if (ubbSize == 5) return 22;
        if (ubbSize == 6) return 26; // 插值
        if (ubbSize == 7) return 30; // 插值
        if (ubbSize == 8) return 32; // 插值
        if (ubbSize == 9) return 34; // 插值
        if (ubbSize == 10) return 35; // 接近36
        if (ubbSize == 11) return 35.5;
        if (ubbSize == 12) return 35.8;
        if (ubbSize == 13) return 36;

        // 对于更大的值，使用渐进增长
        if (ubbSize > 13)
            // 超过13后缓慢增长
            return 36 + (ubbSize - 13) * 0.5;

        return 14; // 默认值
    }
}

// 链接渲染策略
public class UrlRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TagNode tagNode)
        {
            var hyperlink = new Hyperlink();
            var url = tagNode.GetAttribute("href");
            if (!url.IsValidUrl())
                if (node.FirstChild is TextNode child)
                    url = child.Content;
            // 样式统一走 RenderHelper(颜色默认传统色,可由 LinkForeground 覆盖)
            RenderHelper.ApplyLinkStyle(hyperlink, context);

            // 点击事件
            hyperlink.Click += (sender, e) => { context.Control.OnMediaClicked(url, MediaType.Link); };
            context.BeginInlineContainer(hyperlink);
            foreach (var child in node.Children) context.RenderNode(child);
            context.EndInlineContainer();
        }
    }
}

// 图片渲染策略
public class ImageRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TagNode tagNode)
        {
            var src = "";
            var value = tagNode.GetAttribute("value");
            if (!value.IsValidUrl(false))
            {
                // 尝试从子节点获取URL（对于 [img]url[/img] 格式）
                var first = node.FirstChild;
                if (first is TextNode textNode) src = textNode.Content;
            }
            else
            {
                src = value;
            }

            if (src.IsValidUrl(false))
            {
                var image = new Picture.Picture
                {
                    //自定义图片加载器必须在Src设置之前赋值
                    //否则OnSrcChanged事件会触发，但此时将使用默认加载器
                    LoadImageCallback = SmartImageLoader.Default,
                    //UBB隐藏图片语法
                    Hide = context.Control.HideImage || value == "1",
                    Src = src,
                    MaxWidth = (double)context.Properties["ImageMaxWidth"],
                    MaxHeight = (double)context.Properties["ImageMaxHeight"],
                    Stretch = Stretch.Uniform
                };
                var hyperlinkButton = new HyperlinkButton
                {
                    Content = image,
                    Padding = new(1),
                    Background = new SolidColorBrush(Colors.Transparent),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                hyperlinkButton.Click += (s, e) => { context.Control.OnMediaClicked(src, MediaType.Image); };

                context.AddToContainer(hyperlinkButton);
            }
        }
    }
}

// 代码块渲染策略
public class CodeRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();
        var languageName = node is TagNode tagNode ? tagNode.GetAttribute("language") : "plaintext";
        var foreground = RenderHelper.GetBrush(context, "Foreground");
        var viewer = new CodeBlock
        {
            LanguageName = languageName,
            Code = RenderHelper.CollectText(node),
            Background = RenderHelper.GetBrush(context, "CodeBackground") ?? UbbBrushHelper.CreateFillBrush(foreground),
            // 共享的 CodeBlock 模板使用 TemplateBinding BorderBrush,不传画刷时边框不可见
            BorderBrush = UbbBrushHelper.CreateSubtleBrush(foreground),
            Padding = new(12),
            Margin = new(4, 2, 4, 2),
            BorderThickness = new(1),
            CornerRadius = new(6)
        };
        context.AddToContainer(viewer);
    }
}

// 引用渲染策略
public class QuoteRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();
        context.QuoteNestingLevel++;
        var isOutermostQuote = context.QuoteNestingLevel == 1;

        var border = CreateQuoteBorder(context, isOutermostQuote);
        var contentPanel = new StackPanel
        {
            Spacing = 8,
            Margin = new(0)
        };


        // 保存当前容器状态以便恢复
        var previousContainer = context.Container;
        var previousPanelStack = context.PanelStack != null ? new Stack<Panel>(context.PanelStack) : new Stack<Panel>();

        // 切换到新的内容容器
        context.Container = contentPanel;
        if (context.PanelStack != null)
        {
            context.PanelStack.Clear();
            context.PanelStack.Push(contentPanel);
        }

        // 渲染子节点
        foreach (var child in node.Children) context.RenderNode(child);

        // 确保最后的文本块被结束
        context.FinalizeCurrentTextBlock();

        // 恢复之前的容器状态
        context.Container = previousContainer;
        if (context.PanelStack != null)
        {
            context.PanelStack.Clear();
            foreach (var panel in previousPanelStack.Reverse()) context.PanelStack.Push(panel);
        }

        // 将内容面板添加到边框
        border.Child = contentPanel;

        // 将整个引用块添加到容器
        context.AddToContainer(border);
    }

    private static Border CreateQuoteBorder(RenderContext context, bool isOutermostQuote)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 196, 174)),
            BorderThickness = new(2, 0, 0, 0),
            Padding = new(6, 6, 6, 6)
        };
        // 只有最外层引用块有背景色
        if (isOutermostQuote)
        {
            border.Background = (Brush)context.Properties["QuoteBackground"] ??
                                new SolidColorBrush(Color.FromArgb(255, 232, 244, 249));
            border.Margin = new(4, 6, 4, 6);
        }
        else
        {
            border.Background = new SolidColorBrush(Colors.Transparent);
            border.BorderThickness = new(2, 0, 0, 0);
            border.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 176, 154));
            border.Margin = new(2, 2, 0, 2); // 内层缩进
        }

        return border;
    }
}

// 段落渲染策略
public class ParagraphRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();
    }
}

// 对齐渲染策略
public class AlignRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();
        if (node is TagNode tagNode)
        {
            var align = tagNode.GetAttribute("value", "left").ToLower();
            RenderHelper.ApplyAlignToContext(align, node, context);
        }
    }
}

// 左对齐渲染策略
public class LeftRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();
        var align = "left";
        RenderHelper.ApplyAlignToContext(align, node, context);
    }
}

// 居中渲染策略
public class CenterRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();
        var align = "center";
        RenderHelper.ApplyAlignToContext(align, node, context);
    }
}

// 右对齐渲染策略
public class RightRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();
        var align = "right";
        RenderHelper.ApplyAlignToContext(align, node, context);
    }
}

public class ColorRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TagNode tagNode)
        {
            var colorStr = tagNode.GetAttribute("color");
            if (!string.IsNullOrEmpty(colorStr))
            {
                var span = new Span();

                // 尝试解析颜色
                try
                {
                    var color = ParseColor(colorStr);
                    span.Foreground = new SolidColorBrush(color);
                }
                catch
                {
                    // 解析失败，使用默认颜色
                }

                context.BeginInlineContainer(span);

                foreach (var child in node.Children) context.RenderNode(child);

                context.EndInlineContainer();
            }
            else
            {
                // 没有颜色值，直接渲染子节点
                foreach (var child in node.Children) context.RenderNode(child);
            }
        }
    }

    private static Color ParseColor(string colorStr)
    {
        // 移除可能的#
        colorStr = colorStr.Trim().TrimStart('#');
        var colorName = colorStr.ToLower();
        return colorName switch
        {
            "black" => Colors.Black,
            "white" => Colors.White,
            "red" => Colors.Red,
            "green" => Colors.Green,
            "blue" => Color.FromArgb(255, 142, 130, 254),
            "gray" => Colors.Gray,
            "grey" => Colors.Gray,
            "yellow" => Colors.Yellow,
            "purple" => Colors.Purple,
            "orange" => Colors.Orange,
            "transparent" => Colors.Transparent,
            "pink" => Colors.Pink,
            "gold" => Colors.Gold,
            _ => GetColorFromRgb(colorStr)
        };
    }

    private static Color GetColorFromRgb(string colorStr)
    {
        if (colorStr.Length == 6)
        {
            var r = Convert.ToByte(colorStr[..2], 16);
            var g = Convert.ToByte(colorStr.Substring(2, 2), 16);
            var b = Convert.ToByte(colorStr.Substring(4, 2), 16);
            return Color.FromArgb(255, r, g, b);
        }

        return Colors.Black;
    }
}

// 字体渲染策略
public class FontRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TagNode tagNode)
        {
            var fontName = tagNode.GetAttribute("font");
            if (!string.IsNullOrEmpty(fontName))
            {
                var span = new Span();

                try
                {
                    span.FontFamily = new(fontName);
                }
                catch
                {
                    // 字体无效，使用默认字体
                }

                context.BeginInlineContainer(span);

                foreach (var child in node.Children) context.RenderNode(child);

                context.EndInlineContainer();
            }
            else
            {
                // 没有字体值，直接渲染子节点
                foreach (var child in node.Children) context.RenderNode(child);
            }
        }
    }
}

public class EmojiRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        // 不要FinalizeCurrentTextBlock(),保持在当前文本流
        if (node is TagNode tagNode)
        {
            var emoticonCode = tagNode.GetAttribute("code");
            if (!string.IsNullOrEmpty(emoticonCode))
            {
                var imageUrl = GetEmoticonUrl(emoticonCode);
                try
                {
                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        var image = new Picture.Picture
                        {
                            LoadImageCallback = SmartImageLoader.Default,
                            Src = imageUrl,
                            Margin = new(4, 0, 4, 0),
                            MaxWidth = 32,
                            MaxHeight = 32,
                            Stretch = Stretch.UniformToFill
                        };
                        var inlineContainer = new InlineUIContainer { Child = image };
                        context.AddInline(inlineContainer);
                    }
                    else
                    {
                        var run = new Run { Text = $"[{emoticonCode}]" };
                        context.AddInline(run);
                    }
                }
                catch (Exception ex)
                {
                    var run = new Run { Text = $"[{ex.Message}]" };
                    context.AddInline(run);
                }
            }
        }
    }

    private static string GetEmoticonUrl(string code)
    {
        return EmoticonRules.GetEmoticonUrl(code);
    }
}

public class LatexRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is LatexNode latexNode)
        {
            var codeText = latexNode.Latex;
            context.AddInline(InlineLatex(codeText));
        }
        else
        {
            context.FinalizeCurrentTextBlock();
            var codeText = RenderHelper.CollectText(node);
            var block = BlockLatex(codeText);
            context.AddToContainer(block);
        }
    }

    private static InlineUIContainer InlineLatex(string latex)
    {
        var textBlock = new LatexBlock.LatexBlock
        {
            FontSize = 14,
            LaTeX = latex
        };

        var container = new InlineUIContainer { Child = textBlock };
        return container;
    }

    private static LatexBlock.LatexBlock BlockLatex(string latex)
    {
        var textBlock = new LatexBlock.LatexBlock
        {
            FontSize = 14,
            LaTeX = latex
        };
        return textBlock;
    }
}

public class DividerRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        var border = new Border();
        var line = new Line
        {
            X1 = 0,
            Y1 = 0,
            X2 = 1, // 相对坐标，Stretch.Fill会处理
            Y2 = 0,
            // 由前景色推导,深浅主题都可见
            Stroke = UbbBrushHelper.CreateSubtleBrush(RenderHelper.GetBrush(context, "Foreground")),
            StrokeThickness = 2,
            StrokeDashArray = { 2, 2 },
            Stretch = Stretch.Fill, // 自动拉伸
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new(5, 10, 5, 10)
        };
        border.Child = line;
        context.AddToContainer(border);
    }
}

public class MarkdownRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();

        // 不再外包 ScrollViewer:嵌套滚动容器在横向有溢出时会吞掉纵向滚轮(见 microsoft-ui-xaml#10172),
        // 且 MarkdownTextBlock 本身是随外层页面滚动的。
        var textBlock = new MarkdownTextBlock.MarkdownTextBlock
        {
            FontSize = 14,
            Background = UbbBrushHelper.Transparent
        };

        var codeText = RenderHelper.CollectText(node);
        textBlock.MarkdownText = codeText;

        context.AddToContainer(textBlock);
    }
}

public class RenderHelper
{
    /// <summary>从渲染属性袋中取出画刷(键不存在或类型不符时返回 null)。</summary>
    public static Brush? GetBrush(RenderContext context, string key)
    {
        return context.Properties.TryGetValue(key, out var value) ? value as Brush : null;
    }

    /// <summary>从渲染属性袋中取出字体(键不存在或类型不符时返回 null)。</summary>
    public static FontFamily? GetFontFamily(RenderContext context, string key)
    {
        return context.Properties.TryGetValue(key, out var value) ? value as FontFamily : null;
    }

    /// <summary>
    ///     给链接统一应用样式(颜色 + 可选下划线)。所有链接类策略都应经由本方法,便于后续统一调整。
    /// </summary>
    /// <remarks>颜色读取属性袋的 <c>LinkForeground</c>(M2 会由同名依赖属性填充);未设置时保持 CC98 传统色。</remarks>
    public static void ApplyLinkStyle(Hyperlink hyperlink, RenderContext context, bool underline = true)
    {
        hyperlink.Foreground = GetBrush(context, "LinkForeground") ??
                               new SolidColorBrush(Colors.LightSeaGreen);
        hyperlink.TextDecorations = underline ? TextDecorations.Underline : TextDecorations.None;
    }

    public static void ApplyAlignToContext(string align, UbbNode node, RenderContext context)
    {
        //使用Grid进行对齐控制
        var panel = new StackPanel
        {
            HorizontalAlignment = align switch
            {
                "center" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left
            }
        };

        // 保存当前容器
        var previousContainer = context.Container;
        var previousPanelStack = context.PanelStack != null ? new Stack<Panel>(context.PanelStack) : null;

        context.Container = panel;
        context.PanelStack ??= new();
        context.PanelStack.Push(panel);

        // 渲染子节点
        foreach (var child in node.Children) context.RenderNode(child);

        // 确保当前文本块结束
        context.FinalizeCurrentTextBlock();

        // 恢复之前的容器
        context.Container = previousContainer;
        if (previousPanelStack != null)
        {
            context.PanelStack.Clear();
            foreach (var p in previousPanelStack.Reverse())
                context.PanelStack.Push(p);
        }
        else
        {
            // 如果之前没有 PanelStack，则清空当前栈
            context.PanelStack.Clear();
        }

        // 将 panel 添加回之前的容器
        context.AddToContainer(panel);
    }


    public static string CollectText(UbbNode node)
    {
        var sb = new StringBuilder();
        CollectTextRecursive(node, sb);
        return sb.ToString().Trim();
    }

    private static void CollectTextRecursive(UbbNode node, StringBuilder sb)
    {
        foreach (var child in node.Children)
            if (child is TextNode textNode)
                sb.Append(textNode.Content);
            else
                CollectTextRecursive(child, sb);
    }
}

public class FlatQuoteRenderStrategy : IRenderStrategy
{
    private const int MaxVisibleDepth = 2;

    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();

        // 提取引用链（所有层级）
        var quoteChain = ExtractQuoteChain(node);

        if (quoteChain.Count == 0) return;

        // 创建主容器
        var mainContainer = CreateQuoteContainer(context, quoteChain);

        // 添加到渲染上下文
        context.AddToContainer(mainContainer);
    }

    // 提取所有引用层级
    private static List<UbbNode> ExtractQuoteChain(UbbNode node)
    {
        var chain = new List<UbbNode>();
        ExtractAllQuotes(node, chain);
        return chain;
    }

    private static void ExtractAllQuotes(UbbNode node, List<UbbNode> chain)
    {
        if (node.Type == UbbNodeType.Quote)
        {
            chain.Add(node);

            // 查找所有直接子引用
            foreach (var child in node.Children)
                if (child.Type == UbbNodeType.Quote)
                    ExtractAllQuotes(child, chain);
        }
    }

    // 创建引用容器
    private static Border CreateQuoteContainer(RenderContext context, List<UbbNode> quoteChain)
    {
        // 判断是否需要折叠
        var needCollapse = quoteChain.Count > MaxVisibleDepth;
        var visibleCount = Math.Min(quoteChain.Count, MaxVisibleDepth);

        var container = new StackPanel
        {
            Spacing = 0,
            Margin = new(0)
        };

        // 如果需要折叠
        if (needCollapse)
        {
            // 创建折叠部分（第3层及以后）
            var collapsedSection = CreateCollapsedSection(
                [.. quoteChain.Skip(MaxVisibleDepth)],
                context
            );
            container.Children.Add(collapsedSection);

            // 在按钮和下方内容之间添加分割线
            container.Children.Add(CreateSeparator(context));
        }

        // 添加可见部分（前两层）
        for (var i = visibleCount - 1; i >= 0; i--)
        {
            // 渲染单个引用
            var quoteContainer = RenderSingleQuote(quoteChain[i], context);
            container.Children.Add(quoteContainer);

            // 在引用之间添加分割线（除了最后一个）
            if (i > 0) container.Children.Add(CreateSeparator(context));
        }

        // 添加外部边框
        return new()
        {
            Background = RenderHelper.GetBrush(context, "QuoteBackground"),
            Padding = new(12, 8, 12, 8),
            Margin = new(0, 8, 0, 8),
            CornerRadius = new(4),
            Child = container
        };
    }

    // 创建折叠部分
    private static StackPanel CreateCollapsedSection(List<UbbNode> collapsedQuotes, RenderContext context)
    {
        var section = new StackPanel
        {
            Spacing = 4
        };

        // 创建展开按钮
        var expandButton = new ToggleButton
        {
            Content = $"展开{collapsedQuotes.Count}条引用",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new(0, 0, 0, 0),
            Padding = new(12, 6, 12, 6),
            FontSize = 12,
            CornerRadius = new(4),
            IsChecked = false
        };

        // 创建折叠内容容器
        var collapsedContent = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Spacing = 8,
            Margin = new(0, 8, 0, 0)
        };

        // 添加折叠的引用（按从深到浅的顺序）
        for (var i = collapsedQuotes.Count - 1; i >= 0; i--)
        {
            var quoteContainer = RenderSingleQuote(collapsedQuotes[i], context);
            collapsedContent.Children.Add(quoteContainer);

            // 在折叠的引用之间也添加分割线
            if (i > 0) collapsedContent.Children.Add(CreateSeparator(context));
        }

        // 按钮点击事件
        expandButton.Click += (sender, e) =>
        {
            ToggleCollapsedContent(expandButton, collapsedContent, collapsedQuotes.Count);
        };

        section.Children.Add(expandButton);
        section.Children.Add(collapsedContent);

        return section;
    }

    // 切换折叠状态
    private static void ToggleCollapsedContent(ToggleButton button, StackPanel content, int quoteCount)
    {
        if (content.Visibility == Visibility.Collapsed)
        {
            button.IsChecked = true;
            content.Visibility = Visibility.Visible;
            button.Content = "收起引用";
        }
        else
        {
            button.IsChecked = false;
            content.Visibility = Visibility.Collapsed;
            button.Content = $"展开{quoteCount}条引用";
        }
    }


    // 创建分隔线(由前景色推导,替代原先写死的 #33000000)
    private static Border CreateSeparator(RenderContext context)
    {
        return new()
        {
            Height = 1,
            Background = UbbBrushHelper.CreateSubtleBrush(RenderHelper.GetBrush(context, "Foreground")),
            Margin = new(0, 5, 0, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    // 渲染单个引用
    private static Border RenderSingleQuote(UbbNode quoteNode, RenderContext context)
    {
        var contentPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new(0)
        };

        // 创建临时渲染上下文，确保复制 Control
        var tempContext = new RenderContext
        {
            Control = context.Control, // 关键：复制 Control 引用
            Container = contentPanel,
            Properties = context.Properties,
            PanelStack = new(),
            QuoteNestingLevel = context.QuoteNestingLevel
        };

        // 渲染引用内容（跳过嵌套的引用，因为它们已经被提取出来了）
        RenderQuoteContent(quoteNode, tempContext);

        // 左侧竖条作为引用视觉标识(颜色由 QuoteBorderBrush 控制,默认由前景色推导)
        return new()
        {
            Background = UbbBrushHelper.Transparent,
            Padding = new(8, 6, 8, 6),
            Margin = new(0, 2, 0, 2),
            Child = contentPanel
        };
    }

    // 渲染引用内容（跳过嵌套引用）
    private static void RenderQuoteContent(UbbNode node, RenderContext context)
    {
        foreach (var child in node.Children)
            // 跳过嵌套的引用节点（它们已经被提取出来单独渲染）
            if (child.Type != UbbNodeType.Quote)
                context.RenderNode(child);

        context.FinalizeCurrentTextBlock();
    }
}

public class AudioRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TagNode tagNode)
        {
            var title = tagNode.GetAttribute("value");
            if (node.FirstChild is TextNode child)
            {
                var src = child.Content;
                if (src.IsValidUrl(false))
                {
                    var player = new MusicPlayer.MusicPlayer
                    {
                        Title = title == "" ? "音频" : title.Replace("title=", ""),
                        LoadMediaCallback = new SmartMediaLoader(),
                        Src = src,
                        Margin = new(10)
                    };
                    player.DownloadStarted += (s, e) => { context.Control.OnMediaClicked(src, MediaType.Audio); };
                    context.AddToContainer(player);
                }
                else
                {
                    var run = new Run { Text = $"[无效音频链接: {src}]" };
                    context.AddInline(run);
                }
            }
        }
    }
}

public class VideoRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TagNode tagNode)
        {
            var src = tagNode.GetAttribute("value");
            if (string.IsNullOrEmpty(src))
                // 尝试从子节点获取URL
                if (node.FirstChild is TextNode child)
                    src = child.Content;

            if (!string.IsNullOrEmpty(src))
            {
                var player = new VideoPlayer.VideoPlayer
                {
                    LoadVideoCallback = new SmartMediaLoader(),
                    Src = src,
                    MaxHeight = 400,
                    Margin = new(10),
                    AutoPlay = false
                };

                context.AddToContainer(player);
            }
        }
    }
}

public class BilibiliRenderStrategy : IRenderStrategy
{
    /// <summary>BV 号补全用的视频页前缀。</summary>
    private const string VideoUrlPrefix = "https://www.bilibili.com/video/";

    public void Render(UbbNode node, RenderContext context)
    {
        // 取值顺序:属性 [bili=BVxxx] → 子文本 [bili]BVxxx[/bili]
        var raw = node is TagNode tagNode ? tagNode.GetAttribute("value") : string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) raw = RenderHelper.CollectText(node);
        raw = raw.Trim();

        if (string.IsNullOrEmpty(raw)) return;

        // 已是完整地址则原样使用,否则按 BV/av 号补全为视频页地址
        var url = raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? raw
            : VideoUrlPrefix + raw.TrimStart('/');

        var hyperlink = new Hyperlink();
        RenderHelper.ApplyLinkStyle(hyperlink, context);
        hyperlink.Inlines.Add(new Run { Text = raw });
        hyperlink.Click += (_, _) => context.Control.OnMediaClicked(url, MediaType.Link);

        context.AddInline(hyperlink);
    }
}

public class TableRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        context.FinalizeCurrentTextBlock();

        if (node is TagNode tagNode)
        {
            // 解析表格属性（边框、宽度、对齐等）
            var border = tagNode.GetAttribute("border", "1");
            var width = tagNode.GetAttribute("width", "auto");
            var align = tagNode.GetAttribute("align", "left");

            // 创建 Grid 作为表格容器
            var grid = CreateTableGrid(node, context);

            // 设置表格样式
            ApplyTableStyle(grid, border, width, align, context);

            context.AddToContainer(grid);
        }
    }

    private static Grid CreateTableGrid(UbbNode tableNode, RenderContext context)
    {
        // 解析表格结构
        var rows = ExtractRows(tableNode);
        var columns = GetMaxColumns(rows);

        if (rows.Count == 0 || columns == 0)
            return new();

        var grid = new Grid();

        // 添加列定义
        for (var i = 0; i < columns; i++)
            grid.ColumnDefinitions.Add(new()
            {
                Width = new(1, GridUnitType.Star) // 默认等宽
            });

        // 添加行定义并填充内容
        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });

            var cells = rows[i];
            for (var j = 0; j < cells.Count; j++)
            {
                var cell = cells[j];
                var cellContent = RenderCellContent(cell, context);

                Grid.SetRow(cellContent, i);
                Grid.SetColumn(cellContent, j);
                grid.Children.Add(cellContent);
            }
        }

        return grid;
    }

    private static List<List<UbbNode>> ExtractRows(UbbNode tableNode)
    {
        var rows = new List<List<UbbNode>>();

        foreach (var child in tableNode.Children)
            if (child.Type == UbbNodeType.TableRow)
            {
                var cells = new List<UbbNode>();
                foreach (var cell in child.Children)
                    if (cell.Type == UbbNodeType.TableCell)
                        cells.Add(cell);

                rows.Add(cells);
            }

        return rows;
    }

    private static int GetMaxColumns(List<List<UbbNode>> rows)
    {
        var max = 0;
        foreach (var row in rows) max = Math.Max(max, row.Count);
        return max;
    }

    private static Border RenderCellContent(UbbNode cellNode, RenderContext context)
    {
        var container = new Border
        {
            // 由前景色推导,替代原先写死的 #DDDDDD
            BorderBrush = UbbBrushHelper.CreateSubtleBrush(RenderHelper.GetBrush(context, "Foreground")),
            BorderThickness = new(1),
            Padding = new(8, 4, 8, 4)
        };

        var contentPanel = new StackPanel();
        container.Child = contentPanel;

        // 创建临时上下文渲染单元格内容
        var tempContext = new RenderContext
        {
            Control = context.Control,
            Container = contentPanel,
            Properties = context.Properties,
            PanelStack = new()
        };

        foreach (var child in cellNode.Children) tempContext.RenderNode(child);
        tempContext.FinalizeCurrentTextBlock();

        return container;
    }

    private static void ApplyTableStyle(Grid grid, string border, string width, string align, RenderContext context)
    {
        // 设置表格宽度
        if (width != "auto" && width.EndsWith('%'))
            if (double.TryParse(width.TrimEnd('%'), out var percent))
                grid.Width = context.Container?.ActualWidth * percent / 100 ?? 0;

        // 设置对齐方式
        grid.HorizontalAlignment = align.ToLower() switch
        {
            "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left
        };

        // 设置边框（Grid 本身不显示边框，边框在单元格上）
        grid.Margin = new(0, 8, 0, 8);

        // 如果有需要，可以为整个表格添加外边框
        if (border != "0")
        {
            // 已经在单元格上设置了边框
        }
    }
}

public class TableRowRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        // TableRow 由 TableRenderStrategy 统一处理
        // 这里不需要做任何事情，因为表格行是在表格上下文中整体渲染的
        // 如果独立出现，则渲染子节点
        foreach (var child in node.Children) context.RenderNode(child);
    }
}

public class TableCellRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        // TableCell 由 TableRenderStrategy 统一处理
        // 如果独立出现，则渲染子节点
        foreach (var child in node.Children) context.RenderNode(child);
    }
}

public class TopicRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TagNode tagNode)
        {
            var hyperlink = new Hyperlink();
            RenderHelper.ApplyLinkStyle(hyperlink, context);
            var topicId = tagNode.GetAttribute("value");
            if (!topicId.All(char.IsDigit))
                //必须是纯数字，否则不渲染
                return;
            // 点击事件
            hyperlink.Click += (sender, e) =>
            {
                context.Control.OnMediaClicked($"https://www.cc98.org/topic/{topicId}", MediaType.Link);
            };
            context.BeginInlineContainer(hyperlink);
            foreach (var child in node.Children) context.RenderNode(child);
            context.EndInlineContainer();
        }
    }
}

public class AtRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is AtNode atNode)
        {
            var hyperlink = new Hyperlink();
            // @ 提及沿用链接色,但保持无下划线
            RenderHelper.ApplyLinkStyle(hyperlink, context, underline: false);
            hyperlink.Inlines.Add(new Run { Text = $"@{atNode.Username}" });
            // 点击事件
            hyperlink.Click += (sender, e) => { context.Control.OnMediaClicked(atNode.Username, MediaType.AtUser); };
            context.BeginInlineContainer(hyperlink);
            //不存在子元素
            context.EndInlineContainer();
        }
    }
}

public class FileRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        if (node is TagNode tagNode)
        {
            var src = tagNode.GetAttribute("value");
            if (!src.IsValidUrl())
                // 尝试从子节点获取URL
                if (node.FirstChild is TextNode child)
                    src = child.Content;

            if (src.IsValidUrl())
            {
                var card = new FileCard.FileCard
                {
                    Src = src,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new(0, 10, 0, 10)
                };
                card.DownloadRequested += (s, e) => { context.Control.OnMediaClicked(src, MediaType.File); };
                context.AddToContainer(card);
            }
        }
    }
}

public class ReplyViewRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        var run = new Run
        {
            Text = "此消息回复可见",
            Foreground = RenderHelper.GetBrush(context, "LinkForeground") ??
                         new SolidColorBrush(Colors.LightSeaGreen)
        };
        context.AddInline(run);
    }
}

public class PosterOnlyRenderStrategy : IRenderStrategy
{
    public void Render(UbbNode node, RenderContext context)
    {
        var run = new Run { Text = "本楼开启了仅楼主可见", Foreground = new SolidColorBrush(Colors.Red) };
        context.AddInline(run);
    }
}