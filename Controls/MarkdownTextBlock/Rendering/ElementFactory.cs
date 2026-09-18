using CC98.Controls.MarkdownTextBlock.Common;
using CC98.Controls.Picture;
using CC98.Controls.Primitives;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using LatexBlockControl = CC98.Controls.LatexBlock.LatexBlock;

namespace CC98.Controls.MarkdownTextBlock.Rendering;

/// <summary>
///     XAML 元素工厂:链接、图片、任务列表勾选框、公式。
/// </summary>
internal static class ElementFactory
{
    /// <summary>
    ///     创建可点击的超链接(点击仅抛出事件,由宿主决定导航行为)。
    /// </summary>
    public static Hyperlink CreateLink(RenderContext context, string url, string text, bool isAutolink = false)
    {
        var hyperlink = new Hyperlink();
        hyperlink.Click += (_, _) => context.Control.RaiseLinkClicked(url, text, isAutolink);
        return hyperlink;
    }

    /// <summary>
    ///     创建图片控件。
    /// </summary>
    public static Picture.Picture CreatePicture(RenderContext context, string src, string alt)
    {
        var maxWidth = context.GetProperty<double?>(RenderPropertyKeys.ImageMaxWidth) ?? double.PositiveInfinity;
        var maxHeight = context.GetProperty<double?>(RenderPropertyKeys.ImageMaxHeight) ?? double.PositiveInfinity;
        var hideImage = context.GetProperty<bool?>(RenderPropertyKeys.HideImage) ?? false;

        return new Picture.Picture
        {
            LoadImageCallback = SmartImageLoader.Default,
            Hide = hideImage, // 无图模式,与 UBB 的 Hide = Control.HideImage 对齐
            Src = src,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            Stretch = Stretch.Uniform
        };
    }

    /// <summary>
    ///     创建块级图片(独占段落时使用)。
    /// </summary>
    public static UIElement CreateBlockImage(RenderContext context, string src, string alt)
    {
        var picture = CreatePicture(context, src, alt);
        return CreateImageButton(context, picture, alt);
    }

    /// <summary>
    ///     创建行内图片(限高,避免撑破段落行高)。
    /// </summary>
    public static Inline CreateInlineImage(RenderContext context, string src, string alt)
    {
        var picture = CreatePicture(context, src, alt);
        var fontSize = context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;
        picture.MaxHeight = fontSize * 1.5;

        return new InlineUIContainer { Child = CreateImageButton(context, picture, alt) };
    }

    /// <summary>
    ///     创建图片外层的可点击按钮。
    /// </summary>
    /// <remarks>
    ///     样式刻意与 UBB 的 ImageRenderStrategy 保持一致:1 DIP 内边距 + 透明背景 + 内容拉伸,
    ///     这样悬停时 HyperlinkButton 自带的圆角高亮会从图片四周露出 1 DIP,两处控件的图片观感一致。
    ///     (垂直方向保留 Top:行内图片需要按内容高度对齐,不能撑满行高。)
    /// </remarks>
    private static HyperlinkButton CreateImageButton(RenderContext context, Picture.Picture picture, string alt)
    {
        var button = new HyperlinkButton
        {
            Content = picture,
            Padding = new Thickness(1),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            VerticalContentAlignment = VerticalAlignment.Top
        };

        button.Click += (_, _) => context.Control.RaiseImageClicked(picture.Src, alt);
        return button;
    }

    /// <summary>
    ///     创建行内代码(等宽字体 + 代码背景的小色块)。
    ///     WinUI 的 Span 不支持背景色,这里用 InlineUIContainer 承载带背景的 Border。
    /// </summary>
    public static Inline CreateInlineCode(RenderContext context, string text)
    {
        var fontSize = context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;

        var textBlock = new TextBlock
        {
            Text = text,
            FontFamily = context.GetProperty<FontFamily>(RenderPropertyKeys.MonoFontFamily),
            FontSize = fontSize * 0.92,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsTextSelectionEnabled=true
        };

        if (context.Control.Foreground is not null) textBlock.Foreground = context.Control.Foreground;

        var border = new Border
        {
            Background = context.GetProperty<Brush>(RenderPropertyKeys.CodeBackground),
            BorderBrush = context.GetBrush(RenderPropertyKeys.BorderBrush,
                BrushHelper.CreateSubtleBrush(context.Control.Foreground, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, 1),
            Margin = new Thickness(1, 0, 1, -1),
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = textBlock
        };

        return new InlineUIContainer { Child = border };
    }

    /// <summary>
    ///     创建只读任务列表勾选框。
    /// </summary>
    public static Inline CreateTaskListCheckBox(bool isChecked)
    {
        var checkBox = new CheckBox
        {
            IsChecked = isChecked,
            IsHitTestVisible = false,
            MinWidth = 0,
            MinHeight = 0,
            Margin = new Thickness(0, 0, 4, -4),
            VerticalAlignment = VerticalAlignment.Center
        };

        return new InlineUIContainer { Child = checkBox };
    }

    /// <summary>
    ///     创建行内公式($...$)。字号跟随正文。
    ///     颜色刻意不在这里指定:LatexBlock 会按控件【实际主题】自行解析;
    ///     若在此处透传 MarkdownTextBlock.Foreground,主题切换后公式会一直用着旧画刷实例。
    /// </summary>
    public static Inline CreateInlineLatex(RenderContext context, string latex)
    {
        var fontSize = context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;

        return new InlineUIContainer
        {
            Child = new LatexBlockControl
            {
                FontSize = fontSize,
                LaTeX = latex,
                DisplayStyle = false   // 行内公式用紧凑的文本样式,避免撑高行高
            }
        };
    }

    /// <summary>创建块级公式($$...$$)。LatexBlock 自身水平居中,这里只补上下外边距。</summary>
    public static UIElement CreateBlockLatex(RenderContext context, string latex)
    {
        var fontSize = context.GetProperty<double?>(RenderPropertyKeys.FontSize) ?? 14;

        return new LatexBlockControl
        {
            FontSize = fontSize,
            LaTeX = latex,
            DisplayStyle = true,   // 独立公式块用 Display 样式(分式展开、上下限居中)
            Margin = new Thickness(0, 4, 0, 8)
        };
    }
}
