using System;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace CC98.Controls.Primitives;

// 全局单例封装 MediaPlayer，负责媒体源、播放控制和事件转发
public sealed partial class GlobalMediaPlayer : IDisposable
{
    private static readonly Lazy<GlobalMediaPlayer> Lazy = new(() => new());
    private object? _owner; // 记录当前拥有者，避免多个控件同时使用同一个播放器
    public object? CurrentOwner => _owner;
    public bool IsCurrentOwner(object owner) => ReferenceEquals(_owner, owner);
    private readonly MediaPlayer _player;

    private GlobalMediaPlayer()
    {
        _player = new()
        {
            AutoPlay = false,
            IsLoopingEnabled = false
        };

        //直接把底层事件转发出去，订阅者可以像之前那样使用相同的处理函数签名
        _player.MediaOpened += (s, e) => MediaOpened?.Invoke(s, e);
        _player.MediaEnded += (s, e) => MediaEnded?.Invoke(s, e);
        _player.MediaFailed += (s, e) => MediaFailed?.Invoke(s, e);
        _player.CurrentStateChanged += (s, e) => CurrentStateChanged?.Invoke(s, e);

        //也可以转发播放会话位置变化（如果需要）
        _player.PlaybackSession?.PositionChanged += (s, e) => PlaybackSessionPositionChanged?.Invoke(s, e);
    }

    public static GlobalMediaPlayer Instance => Lazy.Value;

    public MediaPlaybackSession PlaybackSession => _player.PlaybackSession;

    public bool IsPlaying { get; private set; }

    /// <summary>音量，0.0 ~ 1.0。所有播放器实例共享同一个底层 MediaPlayer，因此也是一个全局音量。</summary>
    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    public void Dispose()
    {
        // 单例通常不需要被释放，除非程序退出或明确需要清理
        try
        {
            _player?.Pause();
            _player?.Dispose();
        }
        catch
        {
        }
    }

    // 转发事件，保持与 MediaPlayer事件签名兼容
    public event TypedEventHandler<MediaPlayer, object> MediaOpened;
    public event TypedEventHandler<MediaPlayer, object> MediaEnded;
    public event TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs> MediaFailed;
    public event TypedEventHandler<MediaPlayer, object> CurrentStateChanged;

    // 如果 UI需要监听 PlaybackSession 的 PositionChanged
    public event TypedEventHandler<MediaPlaybackSession, object> PlaybackSessionPositionChanged;

    public event EventHandler<object> OwnerChanged;

    // 当前媒体源:MediaSource 实现了 IDisposable,显式关闭可立即释放其持有的内存流
    private MediaSource? _currentSource;

    public void SetSource(MediaSource source,object owner)
    {
        var ownerChanged=!IsCurrentOwner(owner);
        // 先换源再释放旧 MediaSource:旧源持有整个文件的内存流,仅靠 GC 回收时机不可控
        var old = _currentSource;
        _currentSource = source;
        _player.Source = source;
        _owner = owner;
        old?.Dispose();
        if(ownerChanged)
        {
            OwnerChanged?.Invoke(this, owner);
        }
    }

    public void Play()
    {
        _player.Play();
        IsPlaying = true;
    }

    public void Pause()
    {
        _player.Pause();
        IsPlaying = false;
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Seek(TimeSpan position)
    {
        _player.PlaybackSession?.Position = position;
    }

    /// <summary>owner 卸载或释放时归还播放器。非当前 owner 调用则不生效。</summary>
    /// 此方法解决_owner持有控件引用的内存泄漏问题
    public void ReleaseOwnership(object owner)
    {
        if (!IsCurrentOwner(owner)) return;

        _owner = null;
        // 源一并丢弃：owner 侧 _isInitialized 已复位，下次播放必然重新 LoadMedia，
        // 留着旧 MediaSource 只是白占一份内存流
        //保留资源的引用
        var old = _currentSource;
        //释放变量引用，断开连接
        _currentSource = null;
        _player.Source = null;
        //最后再释放旧资源
        old?.Dispose();
    }
}