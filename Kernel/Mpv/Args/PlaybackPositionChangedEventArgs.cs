// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Args;

/// <summary>
/// 播放位置改变事件参数.
/// </summary>
public sealed class PlaybackPositionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackPositionChangedEventArgs"/> class.
    /// </summary>
    public PlaybackPositionChangedEventArgs(long duration, long position)
    {
        Duration = duration;
        Position = position;
    }

    /// <summary>
    /// Gets or sets the duration in milliseconds of the media item.
    /// </summary>
    public long Duration { get; set; }

    /// <summary>
    /// Gets or sets the position in milliseconds of the media item.
    /// </summary>
    public long Position { get; set; }
}
