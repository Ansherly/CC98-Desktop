using CC98.Controls.MarkdownTextBlock.Common;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Collections.Concurrent;

namespace CC98.Controls.MarkdownTextBlock.Rendering;

/// <summary>
///     画刷辅助:由前景色推导出主题自适应的浅色 / 分隔线画刷,避免硬编码颜色。
/// </summary>
public static class BrushHelper
{
    private static readonly Color Fallback = Color.FromArgb(0xFF, 0x88, 0x88, 0x88);

    // 画刷缓存:键为(颜色,α)。同一前景色推导出的画刷在每次渲染中都被重复创建,
    // 这里复用同一实例。纯色画刷不可变,跨元素共享安全;键空间受主题颜色数量约束。
    private static readonly ConcurrentDictionary<(Color Color, byte Alpha), SolidColorBrush> DerivedBrushCache = new();

    public static Color ResolveColor(Brush? brush)
    {
        return brush is SolidColorBrush solid ? solid.Color : Fallback;
    }

    private static Brush GetDerivedBrush(Brush? foreground, byte alpha)
    {
        var color = ResolveColor(foreground);
        return DerivedBrushCache.GetOrAdd((color, alpha),
            static key => new SolidColorBrush(Color.FromArgb(key.Alpha, key.Color.R, key.Color.G, key.Color.B)));
    }

    /// <summary>分隔线 / 边框用的浅色画刷。</summary>
    public static Brush CreateSubtleBrush(Brush? foreground, byte alpha = 0x33)
    {
        return GetDerivedBrush(foreground, alpha);
    }

    /// <summary>表头等填充用的更浅画刷。</summary>
    public static Brush CreateFillBrush(Brush? foreground, byte alpha = 0x14)
    {
        return GetDerivedBrush(foreground, alpha);
    }

    // 全透明画刷是不变量,缓存为单例
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public static Brush Transparent => TransparentBrush;
}
