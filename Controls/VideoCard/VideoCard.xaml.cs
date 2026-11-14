// VideoCard.xaml.cs
//
// UBB [video] 的卡片外壳:布局与 [bili] 卡片保持一致(卡片底 + 圆角 + 媒体区 + 信息栏),
// 媒体区按内容自身比例定尺寸,点击才创建播放器。

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CC98.Kernel.Authorize;
using CC98.Services.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using CC98.Kernel;
using CC98.Controls.Primitives;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CC98.Controls;

/// <summary>
///     UBB <c>[video]</c> 的视频卡片。
/// </summary>
public sealed partial class VideoCard : UserControl
{
    private VideoPlayer? _player;
    private CancellationTokenSource? _lifetimeCts;
    private double? _posterAspect;
    private double? _videoAspect;
    private bool _playerReady;
    private bool _isPosterLoaded;

    /// <summary>上限:一张卡片被反复点也不至于把内存撑爆。到顶后只追加一行"已停止记录"。</summary>
    private const int MaxTraceLines = 400;

    /// <summary>本卡片的调试轨迹:每打一条调试日志就同步 append 一份,右键卡片即复制到剪贴板。</summary>
    private readonly StringBuilder _trace = new();

    private int _traceLines;
    private bool _traceCapped;

    /// <summary>
    ///     初始化 <see cref="VideoCard" />。
    /// </summary>
    public VideoCard()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        // handledEventsToo: true —— 播放键、视频面板都可能把右键吞掉,这里照样收得到
        AddHandler(RightTappedEvent, new RightTappedEventHandler(OnRightTapped), true);
    }

    /// <summary>
    ///     打一条调试日志:同时进 Debug 输出和本卡片的 <see cref="_trace" />。
    /// </summary>
    private void Log(string message)
    {
        MediaDebugLog.Write("VideoCard", message);

        if (_traceCapped) return;

        if (_traceLines >= MaxTraceLines)
        {
            _traceCapped = true;
            _trace.Append("……(超过 ").Append(MaxTraceLines).AppendLine(" 行,后续不再记录)");
            return;
        }

        _trace.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("] ").AppendLine(message);
        _traceLines++;
    }

    /// <summary>右键卡片:把这张卡片的调试轨迹放进剪贴板。</summary>
    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;

        var text = _trace.Length == 0
            ? $"(这张卡片还没有调试信息:src={Source})"
            : _trace.ToString();

        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);

            // 不 Flush 的话应用一退出剪贴板就空了,贴到别处会是一场空
            Clipboard.Flush();
            Log($"右键:调试轨迹已复制({text.Length} 字符)");
        }
        catch (Exception ex)
        {
            Log($"右键:复制调试轨迹失败 {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>视频地址(直链或本地路径)。</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>标题;没给就用地址里的主机名。</summary>
    public string? Title { get; set; }

    /// <summary>封面图地址(可选)。</summary>
    public string? Poster { get; set; }

    /// <summary>是否加载封面(与宿主的无图模式联动)。</summary>
    public bool ShowPoster { get; set; } = true;

    /// <summary>用户要求打开原始地址时触发,参数是该地址。</summary>
    public event Action<string>? OpenRequested;

    #region 生命周期

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lifetimeCts ??= new CancellationTokenSource();

        ApplyMetadata();
        UpdateMediaHeight();

        Log($"进树:src={MediaDebugLog.Url(Source)} 卡片={ActualWidth:F0}x{ActualHeight:F0} " +
            $"MediaHost={MediaHost.ActualWidth:F0}x{MediaHost.ActualHeight:F0} 有封面={ShowPoster}");

        if (ShowPoster && !_isPosterLoaded) _ = LoadPosterAsync(_lifetimeCts.Token);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Log("离树(被回收/切换页面)→ 停播并拆播放器");
        CancelInFlight();
        DestroyPlayer("卡片离树");

        // 回收后重新进树时封面不用再请求一次
        SetStageToCover();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMediaHeight();

    private void CancelInFlight()
    {
        try
        {
            _lifetimeCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经释放,忽略
        }

        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
    }

    #endregion

    #region 尺寸与元数据

    private void ApplyMetadata()
    {
        var title = Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = Uri.TryCreate(Source, UriKind.Absolute, out var uri) ? uri.Host : "视频";
        }

        TitleText.Text = title;

        var subtitle = string.Empty;
        if (Uri.TryCreate(Source, UriKind.Absolute, out var sourceUri))
        {
            var fileName = Path.GetFileName(sourceUri.LocalPath);
            subtitle = string.IsNullOrEmpty(fileName) ? sourceUri.Host : fileName;
        }
        else if (!string.IsNullOrEmpty(Source))
        {
            subtitle = Path.GetFileName(Source);
        }

        SubtitleText.Text = subtitle;
        ToolTipService.SetToolTip(this, Source);
    }

    private void UpdateMediaHeight()
    {
        var aspect = CurrentAspect();
        var (width, height) = MediaCardMetrics.Fit(ActualWidth, aspect);

        if (width <= 0 || height <= 0) return;

        MediaHost.Width = width;
        MediaHost.Height = height;
    }

    private double CurrentAspect()
    {
        if (_playerReady && _videoAspect is > 0) return _videoAspect.Value;
        if (_posterAspect is > 0) return _posterAspect.Value;

        return MediaCardMetrics.DefaultAspect;
    }

    private async Task LoadPosterAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Poster)) return;

        try
        {
            var api = App.Current.GetService<ApiService>();
            var bytes = await api.GetBytesAsync(Poster!, token);
            if (bytes is null || bytes.Length == 0 || token.IsCancellationRequested) return;

            var source = await ImageHelper.LoadFromBytesAsync(bytes, false, token);
            if (token.IsCancellationRequested) return;

            _isPosterLoaded = true;
            PosterImage.Source = source;
            PosterImage.Opacity = 1;

            _posterAspect = await ReadImageAspectAsync(bytes) ?? _posterAspect;
            if (!_playerReady) UpdateMediaHeight();
        }
        catch (OperationCanceledException)
        {
            // 卡片已被回收
        }
        catch (Exception ex)
        {
            Log($"封面加载失败:{ex.Message}");
        }
    }

    private static async Task<double?> ReadImageAspectAsync(byte[] bytes)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var output = stream.GetOutputStreamAt(0))
            {
                await output.WriteAsync(bytes.AsBuffer());
                await output.FlushAsync();
            }

            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) return null;

            return (double)decoder.PixelWidth / decoder.PixelHeight;
        }
        catch (Exception ex)
        {
            // 静态方法里拿不到实例轨迹,只进 Debug 输出
            MediaDebugLog.Write("VideoCard", $"读取封面尺寸失败:{ex.Message}");
            return null;
        }
    }

    #endregion

    #region 播放

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        Log("用户点击播放");
        _ = StartPlaybackAsync();
    }

    private async Task StartPlaybackAsync()
    {
        Log($"开始播放:src={MediaDebugLog.Url(Source)} 卡片={ActualWidth:F0}x{ActualHeight:F0} " +
            $"MediaHost={MediaHost.ActualWidth:F0}x{MediaHost.ActualHeight:F0} " +
            $"PlayerHost={PlayerHost.ActualWidth:F0}x{PlayerHost.ActualHeight:F0} " +
            $"已有播放器={_player is not null} 已加载={_playerReady} 封面比例={_posterAspect:F3}");

        if (_player is not null || string.IsNullOrWhiteSpace(Source))
        {
            Log("放弃:已有播放器在跑,或地址为空");
            return;
        }

        ShowBusy(true);

        var player = new VideoPlayer
        {
            Src = Source,
            AutoPlay = true,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // 播放器内部的进展/失败原因也收进本卡片的轨迹:右键复制时才看得到"为什么返回 false"
        player.DebugMessage += message => Log("  · " + message);

        // CC98 自己的媒体要带登录令牌,直链拉流拿不到 ForumClient 的处理器
        if (Uri.TryCreate(Source, UriKind.Absolute, out var uri)
            && uri.Host.EndsWith("cc98.org", StringComparison.OrdinalIgnoreCase))
        {
            var token = App.Current.GetService<ITokenService>()?.AccessToken;
            Log(string.IsNullOrEmpty(token)
                ? "内链媒体:当前没有登录令牌,不带 Authorization"
                : $"内链媒体:带 Authorization(令牌 {token.Length} 字符)");
            if (!string.IsNullOrEmpty(token)) player.HttpHeaders = $"Authorization: Bearer {token}";
        }
        else
        {
            Log("非 cc98.org 宿主,不带鉴权头");
        }

        player.VideoFailed += OnPlayerFailed;
        player.VideoLoaded += OnPlayerLoaded;
        player.VideoAspectRatioChanged += OnPlayerAspectRatioChanged;

        PlayerHost.Children.Add(player);
        PlayerHost.Visibility = Visibility.Visible;
        _player = player;

        Log($"播放器已进树:PlayerHost={PlayerHost.ActualWidth:F0}x{PlayerHost.ActualHeight:F0} " +
            $"播放器={player.ActualWidth:F0}x{player.ActualHeight:F0} " +
            $"HttpHeaders={MediaDebugLog.HeaderNames(player.HttpHeaders)}");

        var stopwatch = Stopwatch.StartNew();
        var started = await player.LoadVideoAsync();
        Log($"LoadVideoAsync 返回 {started}(耗时 {stopwatch.ElapsedMilliseconds} ms," +
            $"播放器 {player.ActualWidth:F0}x{player.ActualHeight:F0})");

        if (!started && ReferenceEquals(_player, player))
        {
            Log("加载失败 → 拆播放器,提示「视频加载失败」");
            DestroyPlayer("加载返回 false");
            ShowBusy(false);
            ShowStatus("视频加载失败");
        }
    }

    private void OnPlayerLoaded(object? sender, VideoPlayerEventArgs e)
    {
        _playerReady = true;
        PlayerHost.Opacity = 1;
        CoverOverlay.Visibility = Visibility.Collapsed;
        ShowBusy(false);

        Log($"VideoLoaded:src={MediaDebugLog.Url(e.Source)} 比例={_videoAspect:F3} " +
            $"帧数={_player?.RenderedFrames} 播放器={_player?.ActualWidth:F0}x{_player?.ActualHeight:F0} " +
            $"MediaHost={MediaHost.ActualWidth:F0}x{MediaHost.ActualHeight:F0}");
    }

    private void OnPlayerAspectRatioChanged(object? sender, double aspect)
    {
        Log($"比例变化:{aspect:F3}");
        if (aspect <= 0) return;

        _videoAspect = aspect;
        if (_playerReady) UpdateMediaHeight();
    }

    private void OnPlayerFailed(object? sender, VideoPlayerFailedEventArgs e)
    {
        var player = _player;
        Log($"VideoFailed:{e.ErrorMessage} (src={MediaDebugLog.Url(e.Source)} " +
            $"播放器={player?.ActualWidth:F0}x{player?.ActualHeight:F0} " +
            $"帧数={player?.RenderedFrames} 已加载={player?.IsMediaLoaded})");
        DestroyPlayer("播放失败事件");
        ShowBusy(false);
        ShowStatus($"播放失败:{e.ErrorMessage}");
    }

    private void DestroyPlayer(string reason)
    {
        var player = _player;
        if (player is null)
        {
            Log($"拆播放器({reason}):本来就没有");
            return;
        }

        Log($"拆播放器({reason}):已加载={_playerReady} 帧数={player.RenderedFrames} " +
            $"比例={_videoAspect:F3}");

        _player = null;
        _playerReady = false;
        _videoAspect = null;

        player.VideoFailed -= OnPlayerFailed;
        player.VideoLoaded -= OnPlayerLoaded;
        player.VideoAspectRatioChanged -= OnPlayerAspectRatioChanged;

        try
        {
            // 先摘树再延后释放:在 XAML 拆树过程中同步关播放器会原生 failfast 崩进程
            PlayerHost.Children.Remove(player);
        }
        catch (Exception ex)
        {
            Log($"播放器移除失败:{ex.Message}");
        }

        if (!DispatcherQueue.TryEnqueue(() => ReleasePlayer(player))) ReleasePlayer(player);
    }

    private static void ReleasePlayer(VideoPlayer player)
    {
        try
        {
            player.Dispose();
        }
        catch (Exception ex)
        {
            // 静态方法里拿不到实例轨迹,只进 Debug 输出
            MediaDebugLog.Write("VideoCard", $"播放器释放失败:{ex.Message}");
        }
    }

    #endregion

    #region 界面

    private void SetStageToCover()
    {
        PlayerHost.Visibility = Visibility.Collapsed;
        PlayerHost.Opacity = 0;
        CoverOverlay.Visibility = Visibility.Visible;
        ShowBusy(false);
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void ShowBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        CoverOverlay.Visibility = Visibility.Visible;
    }

    private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Source)) OpenRequested?.Invoke(Source);
    }

    #endregion
}
