// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Player;

namespace Mpv.Core.Args;

/// <summary>
/// 播放状态改变事件参数.
/// </summary>
public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackStateChangedEventArgs"/> class.
    /// </summary>
    public PlaybackStateChangedEventArgs(PlaybackState oldState, PlaybackState newState)
    {
        OldState = oldState;
        NewState = newState;
    }

    /// <summary>
    /// The old state of the player.
    /// </summary>
    public PlaybackState OldState { get; }

    /// <summary>
    /// The new state of the player.
    /// </summary>
    public PlaybackState NewState { get; }
}
