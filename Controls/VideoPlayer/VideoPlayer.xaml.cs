// VideoPlayer.xaml.cs
//
// 基于 mpv 的播放控件:
//   libmpv 用 OpenGL 解码渲染 → WGL_NV_DX_interop 共享到 D3D11 纹理 → SwapChainPanel 交给 XAML 合成。
//   之所以不用 MediaPlayerElement:它在控件卸载/拆树过程中被 Dispose 时会以 E_ABORT(0x80004004)
//   触发原生 failfast(0xc000027b),托管层的 try/catch 拦不住。

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CC98.Controls.Primitives;
using CC98.Kernel.Mpv;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mpv.Core.Args;
using Mpv.Core.Enums.Player;
using MpvWinUI.Common;
using OpenTK.Windowing.Common;
using MpvContextSettings = MpvWinUI.Common.ContextSettings;
using MpvPlayerCore = Mpv.Core.Player;
using MpvRenderContext = MpvWinUI.Common.RenderContext;

namespace CC98.Controls;

/// <summary>
///     视频播放控件(mpv)。
/// </summary>
public sealed partial class VideoPlayer : UserControl, IDisposable
{
    private MpvPlayerCore? _player;
    private bool _isDisposed;
    private bool _isInitialized;
    private bool _isMediaLoaded;
    private bool _reportedInitialized;
    private bool _isUpdatingFromPlayer;

    // 静音在 mpv 里就是音量 0:不单独用 mute 属性,否则两边状态会打架
    private double _lastVolumePercent = 100;
    private Stretch _stretch = Stretch.Uniform;
    private string? _httpHeaders;
    private string? _referer;
    private string? _userAgent;
    private MpvRenderSurface? _surface;

    // mpv 读出来的视频宽高比(宽÷高),宿主可以据此把容器调成同一个比例
    private double? _videoAspect;

    // 防止 Loaded 自动加载与宿主显式调用撞车:重入会重复 loadfile
    private bool _isLoading;

    // mpv 的最后一条错误日志:失败时拼进提示,否则用户只能看到干巴巴的“播放失败”
    private string? _lastMpvError;

    /// <summary>
    ///     初始化 <see cref="VideoPlayer" />。
    /// </summary>
    public VideoPlayer()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #region 依赖属性

    /// <summary>视频源:本地路径或网络地址。</summary>
    public static readonly DependencyProperty SrcProperty =
        DependencyProperty.Register("Src", typeof(string), typeof(VideoPlayer),
            new(null, OnSrcChanged));

    /// <summary>旧版媒体加载器;mpv 直接吃地址,这里只保留属性以兼容既有调用方。</summary>
    public static readonly DependencyProperty LoadVideoCallbackProperty =
        DependencyProperty.Register("LoadVideoCallback", typeof(IMediaLoader),
            typeof(VideoPlayer), new(new DefaultMediaLoader()));

    /// <summary>视频源:本地路径或网络地址。</summary>
    public string Src
    {
        get => (string)GetValue(SrcProperty);
        set => SetValue(SrcProperty, value);
    }

    /// <summary>旧版媒体加载器;mpv 版本不使用它。</summary>
    public IMediaLoader LoadVideoCallback
    {
        get => (IMediaLoader)GetValue(LoadVideoCallbackProperty);
        set => SetValue(LoadVideoCallbackProperty, value);
    }

    /// <summary>加载完成后是否自动开始播放。</summary>
    public bool AutoPlay { get; set; }

    /// <summary>画面填充方式(仅保存,绘制时统一按等比铺满处理)。</summary>
    public Stretch Stretch
    {
        get => _stretch;
        set => _stretch = value;
    }

    /// <summary>额外的 HTTP 头,格式为 <c>Name: Value</c>,多条用逗号分隔;需要鉴权的源用它。</summary>
    public string? HttpHeaders
    {
        get => _httpHeaders;
        set => _httpHeaders = value;
    }

    /// <summary>HTTP Referer;B 站直链必须带它,否则 CDN 会返回 403。</summary>
    public string? Referer
    {
        get => _referer;
        set => _referer = value;
    }

    /// <summary>HTTP User-Agent;部分站点会校验,B 站直链建议用浏览器 UA。</summary>
    public string? UserAgent
    {
        get => _userAgent;
        set => _userAgent = value;
    }

    /// <summary>播放器当前是否已经接管了媒体(可以暂停/拖动)。</summary>
    public bool IsMediaLoaded => _isMediaLoaded;

    /// <summary>已经提交到交换链的帧数(诊断用)。</summary>
    public long RenderedFrames => _surface?.RenderCount ?? 0;
    #endregion

    #region 事件

    /// <summary>媒体开始播放。</summary>
    public event EventHandler<VideoPlayerEventArgs>? VideoLoaded;

    /// <summary>播放失败,参数里带错误原因。</summary>
    public event EventHandler<VideoPlayerFailedEventArgs>? VideoFailed;
    /// <summary>视频真实宽高比确定后触发,参数是 宽÷高。</summary>
    public event EventHandler<double>? VideoAspectRatioChanged;

    /// <summary>视频真实宽高比(宽÷高);还没读到时为 <c>null</c>。</summary>
    public double? VideoAspectRatio => _videoAspect;
    /// <summary>播放器初始化完成并成功解出第一帧。</summary>
    public event EventHandler<VideoPlayerEventArgs>? VideoInitialized;

    #endregion

    #region 加载与播放

    /// <summary>
    ///     初始化播放器并载入 <see cref="Src" />。
    /// </summary>
    /// <returns>是否成功接管媒体。</returns>
    public async Task<bool> LoadVideoAsync()
    {
        var src = Src;
        if (string.IsNullOrWhiteSpace(src))
        {
            ReportError("视频地址为空");
            return false;
        }

        // 已经有一次加载在跑:Loaded 会自动触发一次,宿主往往又显式调一次,不要重复 loadfile
        if (_isLoading) return false;

        _isLoading = true;

        try
        {
            _lastMpvError = null;
            ErrorText.Visibility = Visibility.Collapsed;
            ShowBusy(true);

            if (!await EnsureInitializedAsync()) return false;

            var player = _player!;
            _isMediaLoaded = false;
            _reportedInitialized = false;
            _videoAspect = null;

            try
            {
                player.Client.SetProperty("pause", !AutoPlay);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("VideoPlayer", $"设置播放状态失败:{ex.Message}");
            }

            await player.Client.ExecuteWithResultAsync(["loadfile", src, "replace"]);
            return true;
        }
        catch (Exception ex)
        {
            ShowBusy(false);
            ReportError($"视频加载失败:{ex.Message}");
            VideoFailed?.Invoke(this, new(Src, ex.Message));
            return false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>开始/继续播放。</summary>
    public void Play() => SetPaused(false);

    /// <summary>暂停。</summary>
    public void Pause()
    {
        try
        {
            _player?.Pause();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"暂停失败:{ex.Message}");
        }
    }

    /// <summary>跳转到指定位置。</summary>
    public void Seek(TimeSpan position)
    {
        try
        {
            _player?.Seek(position);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"跳转失败:{ex.Message}");
        }
    }

    private void SetPaused(bool paused)
    {
        try
        {
            _player?.Client.SetProperty("pause", paused);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"切换播放状态失败:{ex.Message}");
        }
    }

    private void TogglePlayPause()
    {
        var player = _player;
        if (player is null || !_isMediaLoaded)
        {
            // 还没接管媒体:按钮当成"开始播放"
            _ = LoadVideoAsync();
            return;
        }

        try
        {
            SetPaused(!player.IsPaused());
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"切换播放状态失败:{ex.Message}");
        }
    }

    private async Task<bool> EnsureInitializedAsync()
    {
        if (_isInitialized) return true;

        if (!MpvEnvironment.Initialize())
        {
            ShowBusy(false);
            ReportError("缺少 libmpv-2.dll,无法播放视频");
            return false;
        }

        await WaitForLayoutAsync();

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            ShowBusy(false);
            ReportError("播放区域还没准备好,请再试一次");
            return false;
        }

        // 1) 渲染面:GL 上下文 + D3D11 交换链(面板由 XAML 提供)
        Surface.Visibility = Visibility.Visible;

        if (_surface is null)
        {
            _surface = new MpvRenderSurface(Surface, new MpvContextSettings
            {
                MajorVersion = 4,
                MinorVersion = 6,
                GraphicsProfile = ContextProfile.Compatability,
            });
            _surface.Render += OnRender;
        }

        Surface.PointerPressed -= OnRenderPointerPressed;
        Surface.PointerPressed += OnRenderPointerPressed;
        _surface.Initialize();

        // 2) mpv 核心:全部选项在 mpv_initialize 之前设定
        var player = new MpvPlayerCore();
        var client = player.Client;

        client.SetOption("vo", "libmpv");
        client.SetOption("gpu-api", "opengl");
        client.SetOption("ao", "wasapi");
        client.SetOption("idle", "yes");
        client.SetOption("keep-open", "no");
        client.SetOption("osc", "no");
        client.SetOption("border", "no");
        client.SetOption("input-default-bindings", "no");
        client.SetOption("input-vo-keyboard", "no");
        client.SetOption("terminal", "no");
        client.SetOption("msg-level", "all=error");
        client.SetOption("demuxer-max-bytes", "64MiB");
        client.SetOption("demuxer-readahead-secs", "20");

        if (!string.IsNullOrWhiteSpace(_httpHeaders)) client.SetOption("http-header-fields", _httpHeaders!);
        if (!string.IsNullOrWhiteSpace(_referer)) client.SetOption("referrer", _referer!);
        if (!string.IsNullOrWhiteSpace(_userAgent)) client.SetOption("user-agent", _userAgent!);

        player.PlaybackStateChanged += OnMpvStateChanged;
        player.PlaybackPositionChanged += OnMpvPositionChanged;
        player.PlaybackStopped += OnMpvStopped;
        player.LogMessageReceived += OnMpvLog;

        try
        {
            await player.InitializeAsync(new InitializeArgument(null!, true, MpvRenderContext.GetProcAddress));
        }
        catch (Exception ex)
        {
            ShowBusy(false);
            ReportError($"播放器初始化失败:{ex.Message}");
            _ = player.DisposeAsync();
            return false;
        }

        _player = player;
        _isInitialized = true;

        ApplyVolume(VolumeSlider.Value);
        return true;
    }

    /// <summary>
    ///     等一次布局:宿主往往把控件加进可视树后立刻调用 <see cref="LoadVideoAsync" />,
    ///     此时宽高还是 0,交换链没有尺寸可用。
    /// </summary>
    private async Task WaitForLayoutAsync()
    {
        if (ActualWidth > 0 && ActualHeight > 0) return;

        var completion = new TaskCompletionSource();

        void OnLayoutUpdated(object? sender, object e)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            LayoutUpdated -= OnLayoutUpdated;
            completion.TrySetResult();
        }

        LayoutUpdated += OnLayoutUpdated;

        try
        {
            await Task.WhenAny(completion.Task, Task.Delay(1500));
        }
        finally
        {
            LayoutUpdated -= OnLayoutUpdated;
        }
    }

    #endregion

    #region mpv 事件

    private void OnRender(TimeSpan elapsed)
    {
        var player = _player;
        if (player?.RenderContext is null) return;

        try
        {
            var surface = _surface;
            if (surface is null) return;

            var width = Math.Max(1, (int)Math.Round(Surface.ActualWidth * surface.ScaleX));
            var height = Math.Max(1, (int)Math.Round(Surface.ActualHeight * surface.ScaleY));

            player.RenderGL(width, height, surface.GetBufferHandle());
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"渲染失败:{ex.Message}");
        }
    }

    private void OnMpvStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        var state = e.NewState;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed) return;

            switch (state)
            {
                case PlaybackState.Opening:
                case PlaybackState.Decoding:
                case PlaybackState.Buffering:
                    ShowBusy(true);
                    break;

                case PlaybackState.Playing:
                    _isMediaLoaded = true;
                    ShowBusy(false);
                    ShowSurface();
                    SetPlayPauseGlyph(true);
                    UpdateVideoAspect();
                    if (!_reportedInitialized)
                    {
                        _reportedInitialized = true;
                        VideoInitialized?.Invoke(this, new(Src));
                        VideoLoaded?.Invoke(this, new(Src));
                    }

                    break;

                case PlaybackState.Paused:
                    _isMediaLoaded = true;
                    ShowBusy(false);
                    ShowSurface();
                    SetPlayPauseGlyph(false);
                    break;

                case PlaybackState.None:
                    SetPlayPauseGlyph(false);
                    break;

                case PlaybackState.Failed:
                    _isMediaLoaded = false;
                    ShowBusy(false);
                    ReportError("视频播放失败");
                    break;
            }
        });
    }

    private void OnMpvPositionChanged(object? sender, PlaybackPositionChangedEventArgs e)
    {
        // 位置/时长单位是秒(mpv 侧按整秒读取)
        var duration = e.Duration;
        var position = e.Position;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed || _isUpdatingFromPlayer) return;

            _isUpdatingFromPlayer = true;
            try
            {
                SeekSlider.Maximum = Math.Max(1, duration);
                SeekSlider.Value = Math.Clamp(position, 0, SeekSlider.Maximum);
            }
            finally
            {
                _isUpdatingFromPlayer = false;
            }

            PositionText.Text = FormatTime(position);
            DurationText.Text = FormatTime(duration);
        });
    }

    private void OnMpvStopped(object? sender, PlaybackStoppedEventArgs e)
    {
        var interrupted = e.IsInterrupted;
        var message = e.ErrorMessage;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed) return;

            _isMediaLoaded = false;
            ShowBusy(false);
            SetPlayPauseGlyph(false);

            if (!interrupted) return;

            var text = string.IsNullOrWhiteSpace(message) ? "播放中断" : message!;
            ReportError(text);
            VideoFailed?.Invoke(this, new(Src, text));
        });
    }

    /// <summary>
    ///     从 mpv 读视频真实尺寸,算出宽高比通知宿主。
    /// </summary>
    /// <remarks>
    ///     用 dwidth/dheight(已按像素宽高比校正过的显示尺寸),拿不到时退回 width/height。
    /// </remarks>
    private void UpdateVideoAspect()
    {
        var player = _player;
        if (player is null) return;

        // 走字符串读法:属性尚未就绪时 mpv 返回 PropertyUnavailable,
        // 用 GetPropertyToLong 会直接抛异常(每加载一条视频都刷一串首帧异常),这里安静地降级。
        var width = ReadIntProperty(player, "dwidth");
        var height = ReadIntProperty(player, "dheight");

        if (width <= 0 || height <= 0)
        {
            width = ReadIntProperty(player, "width");
            height = ReadIntProperty(player, "height");
        }

        if (width <= 0 || height <= 0) return;

        var aspect = (double)width / height;
        if (_videoAspect is not null && Math.Abs(_videoAspect.Value - aspect) < 0.001) return;

        _videoAspect = aspect;
        VideoAspectRatioChanged?.Invoke(this, aspect);
    }

    private static int ReadIntProperty(MpvPlayerCore player, string name)
    {
        try
        {
            var text = player.Client.GetPropertyToString(name);
            return int.TryParse(text, out var value) ? value : 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"读取属性 {name} 失败:{ex.Message}");
            return 0;
        }
    }

    private void OnMpvLog(object? sender, LogMessageReceivedEventArgs e)
    {
        if (e.Level is Mpv.Core.Enums.Client.MpvLogLevel.Error or Mpv.Core.Enums.Client.MpvLogLevel.Fatal)
        {
            _lastMpvError = $"{e.Prefix}: {e.Message}";
        }

        Debug.WriteLine("VideoPlayer", $"[{e.Level}] {e.Prefix}: {e.Message}");
    }

    #endregion

    #region 界面

    private void ShowSurface()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        Surface.Visibility = Visibility.Visible;
        ControlBar.Visibility = Visibility.Visible;
    }

    private void ShowBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReportError(string message)
    {
        ControlBar.Visibility = Visibility.Collapsed;
        Surface.Visibility = Visibility.Collapsed;
        ShowBusy(false);

        ErrorText.Text = string.IsNullOrEmpty(_lastMpvError) ? message : $"{message}\n{_lastMpvError}";
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SetPlayPauseGlyph(bool playing)
    {
        // E768 = 播放,E769 = 暂停
        PlayPauseIcon.Glyph = playing ? "\uE769" : "\uE768";
    }

    /// <summary>浮层里的喇叭固定是“静音”;外面那颗跟着当前音量变(与 MusicPlayer 一致)。</summary>
    private void UpdateVolumeVisual(double percent)
    {
        var muted = percent <= 0;

        // E767 = 音量,E74F = 静音
        VolumeIcon.Glyph = muted ? "\uE74F" : "\uE767";
        ToolTipService.SetToolTip(VolumeButton, muted ? "静音中" : "音量");
        ToolTipService.SetToolTip(MuteButton, muted ? "取消静音" : "静音");
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "00:00";

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void MuteButton_Click(object sender, RoutedEventArgs e)
        // 静音 = 拉到 0;再点一次回到上一次的音量
        => VolumeSlider.Value = VolumeSlider.Value > 0 ? 0 : _lastVolumePercent;

    private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingFromPlayer || _player is null) return;

        Seek(TimeSpan.FromSeconds(e.NewValue));
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingFromPlayer) return;

        ApplyVolume(e.NewValue);
    }

    private void ApplyVolume(double percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent > 0) _lastVolumePercent = percent;

        UpdateVolumeVisual(percent);

        try
        {
            _player?.Client.SetProperty("volume", (long)Math.Round(percent));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"设置音量失败:{ex.Message}");
        }
    }

    private void OnRenderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 还没接管媒体:点一下就开始加载
        if (!_isMediaLoaded)
        {
            _ = LoadVideoAsync();
            return;
        }

        TogglePlayPause();
    }

    #endregion

    #region 生命周期与清理

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 控件可能被列表回收后重新挂回可视树
        _isDisposed = false;

        // 宿主没有显式调 LoadVideoAsync 的场合(UBB [video]),进可视树就自己开始载入
        if (!_isInitialized && !_isLoading && !string.IsNullOrWhiteSpace(Src)) _ = LoadVideoAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 卸载回调处在 XAML 拆树过程中:这里只做"停",不动 GL/D3D/mpv 的原生资源。
        // 在拆树过程中释放原生资源,失败会变成 RoFailFastWithErrorContext(0xc000027b),崩整个进程。
        Pause();

        if (!DispatcherQueue.TryEnqueue(() =>
            {
                // 下一拍还挂在树上说明只是移动位置,不要释放
                if (!IsLoaded) Dispose();
            }))
        {
            Dispose();
        }
    }

    /// <summary>
    ///     释放渲染面与 mpv 实例,可重复调用。宿主在移除控件后、消息循环的下一拍调用最安全。
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        _isMediaLoaded = false;
        _isInitialized = false;
        _reportedInitialized = false;

        try
        {
            Surface.PointerPressed -= OnRenderPointerPressed;

            var surface = _surface;
            _surface = null;
            if (surface is not null)
            {
                surface.Render -= OnRender;
                surface.Dispose();
            }

            Surface.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"释放渲染面失败:{ex.Message}");
        }

        var player = _player;
        _player = null;
        if (player is null) return;

        player.PlaybackStateChanged -= OnMpvStateChanged;
        player.PlaybackPositionChanged -= OnMpvPositionChanged;
        player.PlaybackStopped -= OnMpvStopped;
        player.LogMessageReceived -= OnMpvLog;

        // 内部在 Task.Run 里 terminate_destroy,不阻塞 UI
        _ = player.DisposeAsync();
    }

    private static void OnSrcChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VideoPlayer player) _ = player.ResetPlaybackAsync();
    }

    private async Task ResetPlaybackAsync()
    {
        _isMediaLoaded = false;
        _reportedInitialized = false;
        _videoAspect = null;
        var player = _player;
        if (player is not null)
        {
            try
            {
                await player.Client.ExecuteAsync("stop");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("VideoPlayer", $"停止旧媒体失败:{ex.Message}");
            }
        }

        if (_isDisposed) return;

        ShowBusy(false);
        SetPlayPauseGlyph(false);

        ErrorText.Visibility = Visibility.Collapsed;
        ControlBar.Visibility = Visibility.Collapsed;
        Surface.Visibility = Visibility.Collapsed;
    }

    #endregion
}

/// <summary>播放事件参数。</summary>
public class VideoPlayerEventArgs : EventArgs
{
    /// <summary>
    ///     初始化 <see cref="VideoPlayerEventArgs" />。
    /// </summary>
    public VideoPlayerEventArgs(string source) => Source = source;

    /// <summary>视频源。</summary>
    public string Source { get; }
}

/// <summary>播放失败事件参数。</summary>
public class VideoPlayerFailedEventArgs : VideoPlayerEventArgs
{
    /// <summary>
    ///     初始化 <see cref="VideoPlayerFailedEventArgs" />。
    /// </summary>
    public VideoPlayerFailedEventArgs(string source, string errorMessage)
        : base(source)
        => ErrorMessage = errorMessage;

    /// <summary>错误信息。</summary>
    public string ErrorMessage { get; }
}
