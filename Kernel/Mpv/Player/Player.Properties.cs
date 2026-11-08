// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Args;
using Mpv.Core.Enums.Player;
using Mpv.Core.Interop;
using Mpv.Core.Structs.Player;
using System.Threading;

namespace Mpv.Core;

public sealed partial class Player
{
    private bool _isDisposed;
    private bool _isLoaded;
    private long _currentDuration;
    private long _currentPosition;
    private Task? _eventLoopTask;
    private CancellationTokenSource? _eventLoopCancellationTokenSource;

    public event EventHandler<LogMessageReceivedEventArgs>? LogMessageReceived;

    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;

    public event EventHandler<PlaybackStoppedEventArgs>? PlaybackStopped;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PlaybackPositionChanged;

    public event EventHandler? Destroyed;

    public MpvClientNative Client { get; }

    public MpvRenderContextNative? RenderContext { get; private set; }

    public LibMpvDependencies Dependencies { get; private set; }

    public PlaybackState PlaybackState { get; private set; }

    public bool AutoPlay { get; set; }

    public bool IsLoggingEnabled { get; set; } = true;

    /// <summary>
    /// 该播放器是否已被释放.
    /// </summary>
    public bool IsDisposed => _isDisposed;

    public TimeSpan? Duration => _currentDuration > 0 ? TimeSpan.FromSeconds(_currentDuration) : default;

    public TimeSpan? Position => _currentPosition > 0 ? TimeSpan.FromSeconds(_currentPosition) : default;
}
