using CC98.Controls.MarkdownTextBlock.Common;
using CC98.Controls;
using CC98.Controls.Primitives;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using LatexBlockControl = CC98.Controls.LatexBlock;

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
    /// <param name="context">渲染上下文。</param>
    /// <param name="src">图片地址。</param>
    /// <param name="alt">替代文本。</param>
    /// <param name="width">HTML <c>width</c> 属性指定的宽度(像素)。</param>
    /// <param name="height">HTML <c>height</c> 属性指定的高度(像素)。</param>
    /// <remarks>
    ///     统一尺寸规则:带 HTML 宽高属性的图片按属性固定尺寸(浏览器语义);
    ///     其余所有图片宽度受 <c>ImageMaxWidth</c> 统一上限、高度不限(按宽高比自由伸展)。
    /// </remarks>
    public static Picture CreatePicture(RenderContext context, string src, string alt,
        double? width = null, double? height = null)
    {
        var maxWidth = context.GetProperty<double?>(RenderPropertyKeys.ImageMaxWidth) ?? double.PositiveInfinity;
        var hideImage = context.GetProperty<bool?>(RenderPropertyKeys.HideImage) ?? false;

        var picture = new Picture
        {
            LoadImageCallback = SmartImageLoader.Default,
            Hide = hideImage, // 无图模式,与 UBB 的 Hide = Control.HideImage 对齐
            Src = src,
            MaxWidth = width is > 0 ? width.Value : maxWidth,
            MaxHeight = height is > 0 ? height.Value : double.PositiveInfinity,
            Stretch = Stretch.Uniform
        };

        // HTML 的 width/height 是"目标尺寸"而不是上限,这里按浏览器行为固定尺寸
        if (width is > 0) picture.Width = width.Value;
        if (height is > 0) picture.Height = height.Value;

        return picture;
    }

    /// <summary>
    ///     创建块级图片(独占段落时使用)。
    /// </summary>
    public static UIElement CreateBlockImage(RenderContext context, string src, string alt,
        double? width = null, double? height = null)
    {
        var picture = CreatePicture(context, src, alt, width, height);
        return CreateImageButton(context, picture, alt);
    }

    /// <summary>
    ///     创建行内图片。尺寸规则与块级图片一致(见 <see cref="CreatePicture" />):
    ///     未指定 HTML 宽高属性时宽度受统一上限、高度不限;行内与块级仅是排版位置不同。
    /// </summary>
    public static Inline CreateInlineImage(RenderContext context, string src, string alt,
        double? width = null, double? height = null)
    {
        var picture = CreatePicture(context, src, alt, width, height);
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
    private static HyperlinkButton CreateImageButton(RenderContext context, Picture picture, string alt)
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

        const double borderThickness = 1;
        const double paddingBottom = 1;
        // 等宽字体的下降部高度 ≈ 0.25 em(Consolas 实测;换其它等宽字体时这里可能要微调)
        const double monoDescentEm = 0.25;

        // ⚠️ `InlineUIContainer` 把子元素的**外边距盒底边**对齐到**文本基线**,并没有"按文字基线对齐"
        // 的开关(Inline / InlineUIContainer 上都不存在 BaselineAlignment 之类的属性,那是 WPF 才有的)。
        // 因此若不补偿,代码字的字底(基线下方还有一整个下降部)会整体落在基线的**上方**,
        // 看起来比周围文字高一截。这里把底边再下移"下边框 + 底部内边距 + 下降部",
        // 代码字的基线才会与同一行的普通文字齐平。
        var bottomOverflow = borderThickness + paddingBottom + fontSize * 0.92 * monoDescentEm;

        var border = new Border
        {
            Background = context.GetProperty<Brush>(RenderPropertyKeys.CodeBackground),
            BorderBrush = context.GetBrush(RenderPropertyKeys.BorderBrush,
                BrushHelper.CreateSubtleBrush(context.Control.Foreground, 0x33)),
            BorderThickness = new Thickness(borderThickness),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, paddingBottom),
            Margin = new Thickness(1, 0, 1, -bottomOverflow),
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
