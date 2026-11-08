using System;

namespace CC98.Controls;

/// <summary>
///     UBB 媒体卡片(<c>[bili]</c> / <c>[video]</c>)共用的尺寸约束。
/// </summary>
/// <remarks>
///     两个卡片要保持同一套观感:媒体区永远按内容自身比例定尺寸,卡片宽度封顶,
///     高度封顶后再用高度反推宽度,这样封面不会被裁、视频也不会留黑边。
/// </remarks>
internal static class MediaCardMetrics
{
    /// <summary>卡片最大宽度。</summary>
    public const double MaxCardWidth = 480d;

    /// <summary>媒体区最大高度。</summary>
    public const double MaxMediaHeight = 270d;

    /// <summary>媒体区最小高度(极窄栏不至于只剩一条)。</summary>
    public const double MinMediaHeight = 132d;

    /// <summary>拿不到内容比例时的默认值。</summary>
    public const double DefaultAspect = 16d / 9d;

    /// <summary>
    ///     按可用宽度与内容宽高比算出媒体区尺寸。
    /// </summary>
    /// <param name="availableWidth">宿主给的可用宽度。</param>
    /// <param name="aspect">内容宽高比(宽÷高)。</param>
    public static (double Width, double Height) Fit(double availableWidth, double aspect)
    {
        if (availableWidth <= 0) return (0, 0);

        if (aspect <= 0 || double.IsNaN(aspect) || double.IsInfinity(aspect)) aspect = DefaultAspect;

        var cardWidth = Math.Min(availableWidth, MaxCardWidth);
        var width = cardWidth;
        var height = cardWidth / aspect;

        if (height > MaxMediaHeight)
        {
            height = MaxMediaHeight;
            width = height * aspect;
        }
        else if (height < MinMediaHeight)
        {
            height = MinMediaHeight;
            width = Math.Min(cardWidth, height * aspect);
        }

        return (width, height);
    }
}
