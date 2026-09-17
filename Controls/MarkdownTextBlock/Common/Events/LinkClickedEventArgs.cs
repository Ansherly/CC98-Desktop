using System;

namespace CC98.Controls.MarkdownTextBlock.Common.Events;

/// <summary>
///     链接点击事件参数。同时用于 Markdown 链接、自动链接以及 HTML &lt;a&gt; 链接。
/// </summary>
public sealed class LinkClickedEventArgs : EventArgs
{
    public LinkClickedEventArgs(string url, string text, bool isAutolink = false)
    {
        Url = url;
        Text = text;
        IsAutolink = isAutolink;
    }

    /// <summary>
    ///     链接地址(未经改写,可能为相对地址或 mailto:)。
    /// </summary>
    public string Url { get; }

    /// <summary>
    ///     链接显示文本。
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     是否由裸 URL/邮箱的自动链接产生。
    /// </summary>
    public bool IsAutolink { get; }

    /// <summary>
    ///     置为 true 表示宿主已处理该点击(控件本身不做任何导航,此标记仅用于宿主间协作)。
    /// </summary>
    public bool Handled { get; set; }
}
