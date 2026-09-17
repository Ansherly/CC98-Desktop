using CC98.Controls.MarkdownTextBlock.Common;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CC98.Controls.MarkdownTextBlock.Rendering;

/// <summary>
///     画刷辅助:由前景色推导出主题自适应的浅色 / 分隔线画刷,避免硬编码颜色。
/// </summary>
public static class BrushHelper
{
    private static readonly Color Fallback = Color.FromArgb(0xFF, 0x88, 0x88, 0x88);

    public static Color ResolveColor(Brush? brush)
    {
        return brush is SolidColorBrush solid ? solid.Color : Fallback;
    }

    /// <summary>分隔线 / 边框用的浅色画刷。</summary>
    public static Brush CreateSubtleBrush(Brush? foreground, byte alpha = 0x33)
    {
        var color = ResolveColor(foreground);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    /// <summary>表头等填充用的更浅画刷。</summary>
    public static Brush CreateFillBrush(Brush? foreground, byte alpha = 0x14)
    {
        var color = ResolveColor(foreground);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public static Brush Transparent => new SolidColorBrush(Colors.Transparent);
}
