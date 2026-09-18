using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CC98.Controls.UbbTextBlock.Common;

/// <summary>
///     UBB 控件自用的画刷辅助:由前景色推导主题自适应的半透明画刷,避免在策略里硬编码颜色。
/// </summary>
/// <remarks>
///     按项目约定,本类与 MarkdownTextBlock 的同类工具各自维护,不做共享。
/// </remarks>
internal static class UbbBrushHelper
{
    private static readonly Color Fallback = Color.FromArgb(0xFF, 0x88, 0x88, 0x88);

    /// <summary>取出画刷的实际颜色;非纯色画刷时返回兜底灰。</summary>
    public static Color ResolveColor(Brush? brush)
    {
        return brush is SolidColorBrush solid ? solid.Color : Fallback;
    }

    /// <summary>分隔线 / 边框用的半透明画刷(默认 α=0x33)。</summary>
    public static Brush CreateSubtleBrush(Brush? foreground, byte alpha = 0x33)
    {
        var color = ResolveColor(foreground);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    /// <summary>背景填充用的极浅画刷(默认 α=0x14)。</summary>
    public static Brush CreateFillBrush(Brush? foreground, byte alpha = 0x14)
    {
        var color = ResolveColor(foreground);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    /// <summary>全透明画刷。</summary>
    public static Brush Transparent => new SolidColorBrush(Colors.Transparent);
}
