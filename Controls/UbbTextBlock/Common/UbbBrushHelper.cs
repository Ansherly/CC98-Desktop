using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using System.Collections.Concurrent;

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

    // 画刷缓存:键为(颜色,α)。同一前景色推导出的画刷在每次渲染中都被重复创建,
    // 这里复用同一实例。纯色画刷不可变,跨元素共享安全(与主题资源画刷同理);
    // 键空间受主题颜色数量约束,不会无界增长。
    private static readonly ConcurrentDictionary<(Color Color, byte Alpha), SolidColorBrush> DerivedBrushCache = new();

    /// <summary>取出画刷的实际颜色;非纯色画刷时返回兜底灰。</summary>
    public static Color ResolveColor(Brush? brush)
    {
        return brush is SolidColorBrush solid ? solid.Color : Fallback;
    }

    /// <summary>分隔线 / 边框用的半透明画刷(默认 α=0x33)。</summary>
    public static Brush CreateSubtleBrush(Brush? foreground, byte alpha = 0x33)
    {
        return GetDerivedBrush(foreground, alpha);
    }

    /// <summary>背景填充用的极浅画刷(默认 α=0x14)。</summary>
    public static Brush CreateFillBrush(Brush? foreground, byte alpha = 0x14)
    {
        return GetDerivedBrush(foreground, alpha);
    }

    private static Brush GetDerivedBrush(Brush? foreground, byte alpha)
    {
        var color = ResolveColor(foreground);
        return DerivedBrushCache.GetOrAdd((color, alpha),
            static key => new SolidColorBrush(Color.FromArgb(key.Alpha, key.Color.R, key.Color.G, key.Color.B)));
    }

    // 全透明画刷是不变量,缓存为单例(属性形式每次访问都新建画刷)
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    /// <summary>全透明画刷。</summary>
    public static Brush Transparent => TransparentBrush;
}
