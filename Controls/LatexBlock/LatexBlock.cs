using System;
using System.Diagnostics;
using Windows.Foundation;
using CSharpMath.Atom;
using CSharpMath.SkiaSharp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace CC98.Controls.LatexBlock;

/// <summary>
/// 使用 CSharpMath + SkiaSharp 渲染 LaTeX 公式的控件。
/// </summary>
public sealed partial class LatexBlock : UserControl
{
    private readonly SKXamlCanvas _canvas = new();
    private readonly MathPainter _painter = new();
    private float _contentHeight;
    private float _contentWidth;
    private string _latex = "";

    /// <summary>是否按独立公式(Display)排版,详见 <see cref="DisplayStyle" />。</summary>
    private bool _displayStyle = true;

    /// <summary>行间距(相对行高的比例)。</summary>
    private float _lineSpacing = 0.2f;

    /// <summary>每行公式额外预留的底部空白比例。</summary>
    private const float BottomSlackRatio = 0.2f;

    public LatexBlock()
    {
        // 必须设置 IgnorePixelScaling 为 true,以避免 DPI 缩放问题
        // 关闭将导致实际高度和宽度与测量值不一致
        _canvas.IgnorePixelScaling = true;

        var grid = new Grid();
        grid.Children.Add(_canvas);
        Content = grid;

        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;

        // 复用基类 DP,并通过回调统一触发重新测量(字体/内边距/内容对齐变化时)
        RegisterPropertyChangedCallback(Control.FontSizeProperty, (_, _) => OnFontSizeChanged());
        RegisterPropertyChangedCallback(Control.PaddingProperty, (_, _) => InvalidateMeasure());
        RegisterPropertyChangedCallback(Control.HorizontalContentAlignmentProperty, (_, _) => InvalidateMeasure());

        // 默认字号(与旧行为一致)
        FontSize = 20;

        _canvas.PaintSurface += OnPaintSurface;
        OnFontSizeChanged(); // 同步 _painter 字号
        // 显式同步一次排版样式,不依赖 MathPainter 自身的 LineStyle 默认值
        _painter.LineStyle = _displayStyle ? LineStyle.Display : LineStyle.Text;

        // 公式颜色的主题自适应。
        // MathPainter.TextColor 的默认值是纯黑 (#FF000000),深色背景下完全看不见,
        // 因此这里跟随「显式 Foreground > 控件实际主题」解析,并在两种变化时同步:
        //   · Foreground 被重新赋值(例如 XAML 里的 {ThemeResource ...} 随主题重算);
        //   · 实际主题变化(应用通过 RootGrid.RequestedTheme 做元素级主题切换)。
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => UpdateTextColor());
        ActualThemeChanged += (_, _) => UpdateTextColor();
        UpdateTextColor();
    }

    private void OnFontSizeChanged()
    {
        _painter.FontSize = (float)FontSize;
        InvalidateMeasure();
    }

    /// <summary>
    ///     解析公式应当使用的颜色。
    ///     优先使用本地设置的 Foreground;未设置时按控件的【实际主题】取默认前景色。
    ///     这里刻意不用 Application.Current.Resources["TextFillColorPrimaryBrush"] 查表:
    ///     其索引器只反映【应用级】主题,而本应用是用 RootGrid.RequestedTheme 做元素级
    ///     主题切换的,查表结果会与实际生效的主题不一致;ActualTheme 才是元素级生效值。
    /// </summary>
    private SKColor ResolveTextColor()
    {
        // 只采纳【本地设置】的 Foreground(XAML 属性或代码直接赋值),它一定是使用者的真实意图;
        // 用 ReadLocalValue 而不是直接读 Foreground,是为了避免被框架/样式提供的默认值"劫持"
        // (那种值往往是固定色,反而会把深色主题的判断盖掉)。
        if (ReadLocalValue(ForegroundProperty) is SolidColorBrush solid)
            return new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, solid.Color.A);

        return ActualTheme == ElementTheme.Light
            ? new SKColor(0x00, 0x00, 0x00)    // 浅色主题:黑
            : new SKColor(0xFF, 0xFF, 0xFF);   // 深色主题:白
    }

    /// <summary>
    ///     把解析出的颜色同步给 MathPainter 并重绘。
    ///     颜色不参与测量,因此只 Invalidate 画布,不触发 InvalidateMeasure。
    /// </summary>
    private void UpdateTextColor()
    {
        var color = ResolveTextColor();
        if (_painter.TextColor == color) return;

        _painter.TextColor = color;
        _canvas.Invalidate();
    }

    /// <summary>行间距。</summary>
    public double LineSpacing
    {
        get => _lineSpacing;
        set
        {
            _lineSpacing = (float)value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     是否按独立公式(Display)排版。
    ///     行内公式应设为 false —— 对应 LaTeX 的「文本样式」:分式更紧凑、
    ///     求和/积分的上下限移到右侧,不会把所在行的行高撑开。
    ///     MathPainter.LineStyle 默认为 Display,故这里默认 true 与之一致。
    /// </summary>
    public bool DisplayStyle
    {
        get => _displayStyle;
        set
        {
            if (_displayStyle == value) return;

            _displayStyle = value;
            _painter.LineStyle = value ? LineStyle.Display : LineStyle.Text;
            InvalidateMeasure();
        }
    }

    /// <summary>LaTeX 公式(可含换行,每行独立公式)。</summary>
    public string LaTeX
    {
        get => _latex;
        set
        {
            _latex = value;
            InvalidateMeasure();
        }
    }

    /// <summary>是否显示调试边框/空状态提示。</summary>
    public bool ShowDebugBounds { get; set; } = false;

    /// <summary>调试信息。</summary>
    public string DebugInfo
    {
        get
        {
            var lineCount = _latex?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
            return $"内容: {_contentWidth:F1}x{_contentHeight:F1}, " +
                   $"控件: {ActualWidth:F1}x{ActualHeight:F1}, " +
                   $"画布: {_canvas.ActualWidth:F1}x{_canvas.ActualHeight:F1}, " +
                   $"行数: {lineCount}";
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(_latex))
        {
            _contentWidth = 0;
            _contentHeight = 0;
            var emptySize = new Size(Padding.Left + Padding.Right, Padding.Top + Padding.Bottom);
            if (Content is FrameworkElement c) c.Measure(emptySize);
            return emptySize;
        }

        try
        {
            // 公式不需要换行,用足够大的宽度测量
            const float measureWidth = 2000f;
            var lines = _latex.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            _contentWidth = 0;
            _contentHeight = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                _painter.LaTeX = lines[i].Trim();
                var rect = _painter.Measure(measureWidth);

                _contentWidth = Math.Max(_contentWidth, rect.Width);
                // 行块 = 公式高 + 底部余量;行间距只在行间额外加,避免单行虚高
                var lineStep = rect.Height * (1f + BottomSlackRatio);
                _contentHeight += lineStep;
                if (i < lines.Length - 1) _contentHeight += rect.Height * _lineSpacing;
            }

        if (_contentWidth <= 0) _contentWidth = 20;
            if (_contentHeight <= 0) _contentHeight = 20;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"测量失败: {ex.Message}");
            _contentWidth = 100;
            _contentHeight = 30;
        }

        var totalWidth = _contentWidth + Padding.Left + Padding.Right;
        var totalHeight = _contentHeight + Padding.Top + Padding.Bottom;
        var desiredSize = new Size(totalWidth, totalHeight);

        if (Content is FrameworkElement content) content.Measure(desiredSize);

        return desiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Content is FrameworkElement content) content.Arrange(new(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    /// <summary>计算单行公式的占位高度(公式高度 + 底部余量 + 行间距)。</summary>
    private float MeasureLineStep(float lineHeight) =>
        lineHeight * (1f + BottomSlackRatio) + lineHeight * _lineSpacing;

    /// <summary>计算单行公式相对行块顶部的绘制偏移。</summary>
    private float DrawBaselineOffset(float lineHeight) => lineHeight * (1f + BottomSlackRatio);

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var canvasWidth = (float)_canvas.ActualWidth;
        var canvasHeight = (float)_canvas.ActualHeight;

        if (string.IsNullOrEmpty(_latex) || _contentWidth <= 0 || _contentHeight <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
        {
            if (ShowDebugBounds && string.IsNullOrEmpty(_latex))
            {
                canvas.DrawText(SKTextBlob.Create("无公式", new SKFont()), 10f, 30f, new SKPaint { Color = SKColors.Gray, IsAntialias = true });
            }
            return;
        }

        try
        {
            var lines = _latex.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var currentY = (float)Padding.Top;

            for (var i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                _painter.LaTeX = lines[i].Trim();
                var rect = _painter.Measure(_contentWidth);
                // 水平对齐(基于控件实际宽度,而非仅内容宽度,保证控件被拉伸时居中正确)
                var x = (float)Padding.Left;
                switch (HorizontalContentAlignment)
                {
                    case HorizontalAlignment.Right:
                        x = canvasWidth - rect.Width - (float)Padding.Right;
                        break;
                    case HorizontalAlignment.Center:
                        x = (float)Padding.Left + (canvasWidth - (float)Padding.Left - (float)Padding.Right - rect.Width) / 2f;
                        break;
                }
                // ★ 关键发现(经对比各版本调试确定):
                // CSharpMath 的 MathPainter.Draw(canvas, position) 中 position 是【公式的基线】,而非左上角。
                // 公式体实际绘制范围为: [基线 - |rect.Y|, 基线 + (rect.Height - |rect.Y|)]
                //   - rect.Y 为负值,表示基线以上的高度(基线上方部分);
                //   - rect.Height - |rect.Y| 为基线以下的部分。
                // 若把 position 当作左上角(currentY)或行底(currentY + rect.Height)直接绘制,会分别出现
                // 「公式顶出画布被裁」或「公式沉底被裁」两种错误——都必须以基线语义来定位。
                // 正确基线 = 行顶 + 行块内居中留白 + 基线上方高度,使公式体在行块内垂直居中。
                var lineStep = rect.Height * (1f + BottomSlackRatio);   // 行块高 = 公式高 + 底部余量
                var topPad = (lineStep - rect.Height) / 2f;             // 行块内顶部留白,用于垂直居中
                var baseline = currentY + topPad + Math.Abs(rect.Y);    // 基线 = 行顶 + 居中止白 + 基线上方高度
                _painter.Draw(canvas, new(x, baseline));

                currentY += rect.Height * (1f + BottomSlackRatio);
                if (i < lines.Length - 1) currentY += rect.Height * _lineSpacing;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"绘制失败: {ex.Message}");
        }
    }
}
