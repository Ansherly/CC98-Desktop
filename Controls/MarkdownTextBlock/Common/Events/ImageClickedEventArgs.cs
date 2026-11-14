using System;

namespace CC98.Controls.MarkdownTextBlock.Common.Events;

/// <summary>
///     图片点击事件参数。
/// </summary>
public sealed class ImageClickedEventArgs : EventArgs
{
    public ImageClickedEventArgs(string source, string alt)
    {
        Source = source;
        Alt = alt;
    }

    /// <summary>
    ///     图片地址(可能是 http/https、ms-appx:/ms-appdata: 或本地路径,已做 ~/ 展开)。
    /// </summary>
    public string Source { get; }

    /// <summary>
    ///     替代文本(alt):Markdown 中为 ![...] 内的文本,HTML 中为 alt 属性。
    /// </summary>
    public string Alt { get; }

    /// <summary>
    ///     置为 true 表示宿主已处理该点击。
    /// </summary>
    public bool Handled { get; set; }
}
