// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Args;

/// <summary>
/// 播放停止事件参数.
/// </summary>
public sealed class PlaybackStoppedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackStoppedEventArgs"/> class.
    /// </summary>
    public PlaybackStoppedEventArgs(bool isInterrupted, string? message = default)
    {
        IsInterrupted = isInterrupted;
        ErrorMessage = message;
    }

    /// <summary>
    /// Gets a value indicating whether the playback was interrupted.
    /// </summary>
    public bool IsInterrupted { get; }

    /// <summary>
    /// Gets error message if the playback was interrupted.
    /// </summary>
    public string? ErrorMessage { get; }
}
