using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CC98.Kernel.Bilibili;
using System.Runtime.InteropServices.WindowsRuntime;
using CC98.Services.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using CC98.Controls.Primitives;

namespace CC98.Controls;

/// <summary>
///     UBB <c>[bili]</c> 的内嵌播放卡片:未登录可取流,且全程懒加载。
/// </summary>
/// <remarks>
///     <para>
///         分三级懒加载,滚动浏览帖子时不会因为帖子里挂了视频就产生请求:
///         <list type="number">
///             <item>渲染期:只搭一张空卡片(16:9 占位 + 播放按钮),不碰网络;</item>
///             <item>可见期:卡片进入<b>有效视口</b>(<see cref="FrameworkElement.EffectiveViewportChanged" />)
///                 才请求 <c>view</c> 接口拿标题 / 封面 / 时长 —— 被回收进缓冲区、尚未真正露出的卡片不会加载;</item>
///             <item>点击期:用户点播放才请求 <c>playurl</c> 并下载媒体(带进度、可取消),播放器本身到这一步才创建。</item>
///         </list>
///     </para>
///     <para>
///         全程匿名:B 站接口只走未登录可用的公开参数,不携带任何账号凭证,失败时给出"在 B 站打开"的出口。
///     </para>
/// </remarks>
public sealed partial class BilibiliPlayer : UserControl
{
    /// <summary>
    ///     卡片所处的阶段,决定播放按钮、进度环、播放器各层的显隐。
    /// </summary>
    private enum Stage
    {
        Idle,
        LoadingInfo,
        Ready,
        Resolving,
        Playing,
        Failed
    }

    private BilibiliVideoInfo? _info;
    private VideoPlayer? _player;

    // 首帧到手之前继续显示封面:否则播放器初始化那两秒会闪一下黑底
    private bool _playerReady;

    // 命中缓存的直链可能已过期:播放失败时作废它并自动重试一次,不必让用户反复点
    private bool _playbackRetried;

    // 封面 / 视频的真实宽高比(宽÷高):媒体区高度跟着它走,而不是固定 16:9
    private double? _coverAspect;
    private double? _videoAspect;
    private CancellationTokenSource? _lifetimeCts;
    private Stage _stage = Stage.Idle;

    // 代次:控件被回收 / 参数变更后,在途请求的结果不得再回填到 UI
    private int _generation;

    /// <summary>上限:一张卡片被反复点也不至于把内存撑爆。到顶后只追加一行"已停止记录"。</summary>
    private const int MaxTraceLines = 400;

    /// <summary>本卡片的调试轨迹:每打一条调试日志就同步 append 一份,右键卡片即复制到剪贴板。</summary>
    private readonly StringBuilder _trace = new();

    private int _traceLines;
    private bool _traceCapped;

    /// <summary>
    ///     初始化 <see cref="BilibiliPlayer" />。
    /// </summary>
    public BilibiliPlayer()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        // handledEventsToo: true —— 播放键、视频面板都可能把右键吞掉,这里照样收得到
        AddHandler(RightTappedEvent, new RightTappedEventHandler(OnRightTapped), true);

        UpdateVisuals();
    }

    /// <summary>
    ///     打一条调试日志:同时进 Debug 输出和本卡片的 <see cref="_trace" />。
    /// </summary>
    private void Log(string message)
    {
        MediaDebugLog.Write("Bilibili", message);

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
            ? $"(这张卡片还没有调试信息:videoId={VideoId})"
            : _trace.ToString();

        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);

            // 不 Flush 的话应用一退出剪贴板就空了
            Clipboard.Flush();
            Log($"右键:调试轨迹已复制({text.Length} 字符)");
        }
        catch (Exception ex)
        {
            Log($"右键:复制调试轨迹失败 {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    ///     用户要求"在 B 站打开"时触发,参数是视频页地址;由渲染策略转成宿主的链接点击。
    /// </summary>
    public event Action<string>? OpenInBrowserRequested;

    #region 依赖属性

    /// <summary>视频标识(BV 号 / av 号 / 视频页地址)。</summary>
    public static readonly DependencyProperty VideoIdProperty =
        DependencyProperty.Register(
            nameof(VideoId),
            typeof(string),
            typeof(BilibiliPlayer),
            new(string.Empty, OnVideoIdChanged));

    /// <summary>要播放的分 P 序号(从 1 开始)。</summary>
    public static readonly DependencyProperty PageProperty =
        DependencyProperty.Register(
            nameof(Page),
            typeof(int),
            typeof(BilibiliPlayer),
            new(1));

    /// <summary>是否加载封面(与宿主的无图模式联动;关掉后只显示占位底色)。</summary>
    public static readonly DependencyProperty ShowCoverProperty =
        DependencyProperty.Register(
            nameof(ShowCover),
            typeof(bool),
            typeof(BilibiliPlayer),
            new(true, OnShowCoverChanged));

    /// <summary>视频标识(BV 号 / av 号 / 视频页地址)。</summary>
    public string VideoId
    {
        get => (string)GetValue(VideoIdProperty);
        set => SetValue(VideoIdProperty, value);
    }

    /// <summary>要播放的分 P 序号(从 1 开始)。</summary>
    public int Page
    {
        get => (int)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>是否加载封面。</summary>
    public bool ShowCover
    {
        get => (bool)GetValue(ShowCoverProperty);
        set => SetValue(ShowCoverProperty, value);
    }

    #endregion

    private static BilibiliApi Api => App.Current.GetService<BilibiliApi>();


    private CancellationToken LifetimeToken => _lifetimeCts?.Token ?? CancellationToken.None;

    #region 生命周期

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _lifetimeCts ??= new CancellationTokenSource();
        UpdateMediaHeight();

        // 防重复订阅:控件被回收后再次显示时会重新走到这里
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        EffectiveViewportChanged += OnEffectiveViewportChanged;

        UpdateToolTip();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        EffectiveViewportChanged -= OnEffectiveViewportChanged;

        // 让在途的接口请求 / 下载回填全部失效:控件已经被回收,不能再改 UI
        _generation++;
        CancelInFlight();
        Log("离树(被回收/切换页面)→ 停播并拆播放器");
        DestroyPlayer("卡片离树");

        // 元数据保留(再滚回来不必重新请求),但播放器必须销毁,阶段回到"已就绪"
        SetStage(_info is null ? Stage.Idle : Stage.Ready);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMediaHeight();

    /// <summary>
    ///     媒体区始终按"当前内容"的宽高比定尺寸:封面期按封面比例,播放期按视频比例。
    ///     具体尺寸约束与 <see cref="VideoCard" /> 共用 <see cref="MediaCardMetrics" />。
    /// </summary>
    private void UpdateMediaHeight()
    {
        var (width, height) = MediaCardMetrics.Fit(ActualWidth, CurrentAspect());
        if (width <= 0 || height <= 0) return;

        MediaHost.Width = width;
        MediaHost.Height = height;
    }

    /// <summary>当前应该采用的宽高比(宽÷高)。</summary>
    private double CurrentAspect()
    {
        if (_stage == Stage.Playing && _videoAspect is > 0) return _videoAspect.Value;
        if (_coverAspect is > 0) return _coverAspect.Value;

        return 16d / 9d;
    }

    /// <summary>播放器读出视频真实尺寸后,高度按视频比例重算。</summary>
    private void OnPlayerAspectRatioChanged(object? sender, double aspect)
    {
        if (aspect <= 0) return;

        _videoAspect = aspect;
        if (_stage == Stage.Playing) UpdateMediaHeight();
    }

    /// <summary>
    ///     读图片像素尺寸以得到宽高比:接口只给封面 URL,比例得从解码器里拿。
    /// </summary>
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
            Debug.WriteLine("Bilibili", $"读取封面尺寸失败:{ex.Message}");
            return null;
        }
    }

    #endregion

    #region 懒加载:可见才取元数据

    private void OnEffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        // 与视口没有交集时宽高为 0:此时卡片还没真正露出来,不该发请求
        if (args.EffectiveViewport.Width <= 0 || args.EffectiveViewport.Height <= 0) return;

        if (_stage != Stage.Idle) return;

        _ = LoadInfoAsync();
    }

    /// <summary>
    ///     取视频基础信息(走进程内缓存,同一条视频被多次引用时只请求一次)。
    /// </summary>
    private async Task LoadInfoAsync()
    {
        if (_stage is not (Stage.Idle or Stage.Failed)) return;

        // 上次只是被回收:元数据还在,最多补一张封面
        if (_info is not null)
        {
            SetStage(Stage.Ready);
            if (ShowCover && CoverImage.Source is null) await LoadCoverAsync(_info, ++_generation);
            return;
        }

        var generation = ++_generation;
        SetStage(Stage.LoadingInfo, "正在获取视频信息…");

        var info = await Api.GetVideoAsync(VideoId, LifetimeToken);
        if (generation != _generation) return;

        if (info is null)
        {
            SetStage(Stage.Failed, "视频信息获取失败,可使用浏览器打开");
            return;
        }

        _info = info;
        ApplyInfo(info);
        SetStage(Stage.Ready);

        await LoadCoverAsync(info, generation);
    }

    /// <summary>
    ///     把元数据填进卡片。封面单独走 <see cref="LoadCoverAsync" />,失败不影响其它信息。
    /// </summary>
    private void ApplyInfo(BilibiliVideoInfo info)
    {
        TitleText.Text = info.Title;

        var partText = info.Parts.Count > 1 ? $" · 共 {info.Parts.Count}P" : string.Empty;
        var duration = FormatDuration(info.Duration);
        SubtitleText.Text = $"{info.Owner}{partText}";
        DurationText.Text = duration;
        DurationBadge.Visibility = Visibility.Visible;

        UpdateToolTip();
    }

    /// <summary>
    ///     取封面并解码:先试缩略图(体积小、解码后占用小),取不到再退回原图。
    /// </summary>
    private async Task LoadCoverAsync(BilibiliVideoInfo info, int generation)
    {
        if (!ShowCover || string.IsNullOrEmpty(info.Cover)) return;

        try
        {
            var bytes = await Api.GetImageBytesAsync(BilibiliApi.AppendCoverThumb(info.Cover), LifetimeToken);
            if (generation != _generation) return;

            bytes ??= await Api.GetImageBytesAsync(info.Cover, LifetimeToken);
            if (bytes is null || bytes.Length == 0 || generation != _generation) return;

            var source = await ImageHelper.LoadFromBytesAsync(bytes, false, LifetimeToken);
            if (generation != _generation) return;

            CoverImage.Source = source;
            CoverImage.Opacity = 1;

            // 封面比例决定媒体区高度:没拿到就沿用旧值 / 默认 16:9
            _coverAspect = await ReadImageAspectAsync(bytes) ?? _coverAspect;
            if (_stage != Stage.Playing) UpdateMediaHeight();
        }
        catch (Exception ex)
        {
            // 封面只是锦上添花:失败时保留渐变底色即可
            Debug.WriteLine("Bilibili", $"封面加载失败:{ex.Message}");
        }
    }

    #endregion

    #region 懒加载:点击才取直链并播放

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        Log($"用户点击播放:阶段={_stage} bvid={_info?.Bvid} 分P={Page} " +
            $"卡片={ActualWidth:F0}x{ActualHeight:F0} MediaHost={MediaHost.ActualWidth:F0}x{MediaHost.ActualHeight:F0}");

        // Idle:视口事件没来(极少数宿主),点击就是"立刻加载";Failed:允许点一次重试
        if (_stage is Stage.Idle or Stage.Failed)
        {
            await LoadInfoAsync();
            if (_stage != Stage.Ready)
            {
                Log($"取信息后没到 Ready(阶段={_stage}),不播放");
                return;
            }
        }

        if (_stage == Stage.Ready) await StartPlaybackAsync();
        else Log($"当前阶段={_stage},点击被忽略");
    }

    private async Task StartPlaybackAsync(bool retryAfterStaleUrl = false)
    {
        Log($"开始播放(retryAfterStaleUrl={retryAfterStaleUrl}) 阶段={_stage} bvid={_info?.Bvid} 分P={Page}");

        // 用户主动点播放时重新给一次重试机会;自动重试不能把那一次机会刷掉
        if (!retryAfterStaleUrl) _playbackRetried = false;

        if (_info is null || _stage is Stage.Resolving or Stage.Playing)
        {
            Log($"放弃:信息未就绪或正在解析/播放(阶段={_stage})");
            return;
        }

        var part = _info.GetPart(Page);
        if (part is null)
        {
            Log($"分P {Page} 不存在");
            SetStage(Stage.Failed, "该视频没有可播放的分 P");
            return;
        }

        var generation = ++_generation;
        var token = LifetimeToken;

        SetStage(Stage.Resolving, "正在获取播放地址…");

        Log($"请求直链:bvid={_info.Bvid} cid={part.Cid} 分P={Page}");
        var stopwatch = Stopwatch.StartNew();
        var play = await Api.GetPlayUrlAsync(_info.Bvid, part.Cid, token);
        Log($"直链返回 {stopwatch.ElapsedMilliseconds} ms:" +
            (play is null ? "null" : $"{play.QualityText} url={MediaDebugLog.Url(play.Url)}"));

        if (generation != _generation)
        {
            Log($"直链回来时已经换了一次播放(generation 变化),丢弃");
            return;
        }

        if (play is null)
        {
            SetStage(Stage.Failed, "未取到播放地址,可使用浏览器打开");
            return;
        }

        DurationText.Text = $"{FormatDuration(_info.Duration)} · {play.QualityText}";

        // 拿到直链后直接交给播放器:参考头由播放器带上,不再走"下载到缓存再播"
        SetStage(Stage.Playing, play.QualityText);

        PlayUrl(play.Url);
    }

    // B 站 CDN 会校验 UA,用浏览器 UA 最稳
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>
    ///     直接播放直链:播放器自己发 HTTP 请求(经本机回环代理),带上 Referer / UA
    ///     就能过 CDN 的防盗链,所以不需要"先下载完整文件再播"。
    /// </summary>
    private void PlayUrl(string url)
    {
        Log($"交给播放器:url={MediaDebugLog.Url(url)}");
        DestroyPlayer("换新的直链");

        var player = new VideoPlayer
        {
            Src = url,
            Referer = "https://www.bilibili.com/",
            UserAgent = BrowserUserAgent,
            AutoPlay = true,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        player.VideoFailed += OnPlayerFailed;
        player.VideoLoaded += OnPlayerLoaded;
        player.VideoAspectRatioChanged += OnPlayerAspectRatioChanged;

        // 播放器内部的进展/失败原因也收进本卡片的轨迹:右键复制时才看得到"为什么没出画"
        player.DebugMessage += message => Log("  · " + message);

        PlayerHost.Children.Add(player);
        _player = player;

        SetStage(Stage.Playing);

        Log($"播放器已进树:PlayerHost={PlayerHost.ActualWidth:F0}x{PlayerHost.ActualHeight:F0} " +
            $"播放器={player.ActualWidth:F0}x{player.ActualHeight:F0} " +
            $"HttpHeaders={MediaDebugLog.HeaderNames(player.HttpHeaders)} Referer={player.Referer}");

        _ = LoadAndLogAsync(player);
    }

    /// <summary>B 站卡片只管发车、不看结果,这里把结果与耗时打进 Debug 输出。</summary>
    private async Task LoadAndLogAsync(VideoPlayer player)
    {
        var stopwatch = Stopwatch.StartNew();
        var started = await player.LoadVideoAsync();
        Log($"LoadVideoAsync 返回 {started}(耗时 {stopwatch.ElapsedMilliseconds} ms," +
            $"播放器 {player.ActualWidth:F0}x{player.ActualHeight:F0})");
    }

    private void OnPlayerLoaded(object? sender, VideoPlayerEventArgs e)
    {
        _playerReady = true;
        UpdateVisuals();

        Log($"VideoLoaded:url={MediaDebugLog.Url(e.Source)} 比例={_videoAspect:F3} " +
            $"帧数={_player?.RenderedFrames} 播放器={_player?.ActualWidth:F0}x{_player?.ActualHeight:F0}");
    }

    private void OnPlayerFailed(object? sender, VideoPlayerFailedEventArgs e)
    {
        Log($"VideoFailed:{e.ErrorMessage} (url={MediaDebugLog.Url(e.Source)} " +
            $"帧数={_player?.RenderedFrames} 已加载={_player?.IsMediaLoaded} " +
            $"已重试={_playbackRetried})");

        var info = _info;
        var part = info?.GetPart(Page);

        // 直链可能已经过期(尤其是命中缓存的那条):作废缓存并重试一次
        if (info is not null && part is not null && !_playbackRetried)
        {
            _playbackRetried = true;
            BilibiliApi.InvalidatePlayUrl(info.Bvid, part.Cid);

            DestroyPlayer("直链失效,重试");
            SetStage(Stage.Ready);
            _ = StartPlaybackAsync(true);
            return;
        }

        DestroyPlayer("播放失败");
        SetStage(Stage.Failed, $"播放失败:{e.ErrorMessage}");
    }


    #endregion

    #region 状态与清理

    private void SetStage(Stage stage, string? status = null)
    {
        if (_stage != stage || !string.IsNullOrEmpty(status))
        {
            Log($"阶段 {_stage} → {stage}" + (string.IsNullOrEmpty(status) ? string.Empty : $",提示「{status}」"));
        }

        _stage = stage;
        StatusText.Text = status ?? string.Empty;
        UpdateVisuals();
        UpdateMediaHeight();
    }

    private void UpdateVisuals()
    {
        var busy = _stage is Stage.LoadingInfo or Stage.Resolving;
        var playing = _stage == Stage.Playing;

        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Visibility = string.IsNullOrEmpty(StatusText.Text) ? Visibility.Collapsed : Visibility.Visible;

        // 播放器必须保持可见(只是透明):它要参与布局才有实际宽高,
        // 隐藏会让播放区域量到 0,交换链那边会当“区域没准备好”。
        // 首帧到手前保持封面可见,避免黑底闪一下。
        CoverOverlay.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PlayerHost.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        PlayerHost.Opacity = _playerReady ? 1 : 0;

        // 忙碌 / 已在播放时藏掉播放按钮,避免重复点击
        PlayButton.Visibility = busy || playing ? Visibility.Collapsed : Visibility.Visible;
    }

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

    private void DestroyPlayer(string reason)
    {
        var player = _player;
        if (player is null)
        {
            Log($"拆播放器({reason}):本来就没有");
            return;
        }

        Log($"拆播放器({reason}):已加载={_playerReady} 帧数={player.RenderedFrames} 比例={_videoAspect:F3}");

        _player = null;
        player.VideoFailed -= OnPlayerFailed;
        player.VideoLoaded -= OnPlayerLoaded;
        _playerReady = false;
        player.VideoAspectRatioChanged -= OnPlayerAspectRatioChanged;

        // 回到封面期:高度改回封面比例
        _videoAspect = null;
        UpdateMediaHeight();

        try
        {
            // 先把控件从容器里摘下来(纯 XAML 操作),再推到下一拍做媒体收尾。
            // 反过来做——在 XAML 拆树过程中同步关播放器——原生侧会 failfast 崩进程(0xc000027b)。
            PlayerHost.Children.Remove(player);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Bilibili", $"播放器移除失败:{ex.Message}");
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
            Debug.WriteLine("Bilibili", $"播放器释放失败:{ex.Message}");
        }
    }

    /// <summary>
    ///     参数变更时清空整张卡片(渲染策略通常重建控件,这里只是防御性处理)。
    /// </summary>
    private void ResetContent()
    {
        _generation++;
        CancelInFlight();
        Log("参数变更 → 清空卡片");
        DestroyPlayer("参数变更");

        _info = null;
        _coverAspect = null;
        _videoAspect = null;
        CoverImage.Source = null;
        CoverImage.Opacity = 0;
        TitleText.Text = string.Empty;
        SubtitleText.Text = string.Empty;
        DurationBadge.Visibility = Visibility.Collapsed;

        SetStage(Stage.Idle);
        UpdateToolTip();
    }

    #endregion

    #region 杂项

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (!BilibiliVideoId.TryParse(VideoId, out var id, out _)) id = VideoId;
        OpenInBrowserRequested?.Invoke(BilibiliVideoId.ToPageUrl(id, Page));
    }

    private void UpdateToolTip()
    {
        if (!BilibiliVideoId.TryParse(VideoId, out var id, out _)) id = VideoId;
        ToolTipService.SetToolTip(this, BilibiliVideoId.ToPageUrl(id, Page));
    }

    private static string FormatDuration(long seconds)
    {
        if (seconds <= 0) return "--:--";

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    private static void OnVideoIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BilibiliPlayer player) player.ResetContent();
    }

    private static void OnShowCoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BilibiliPlayer player) return;

        if (!player.ShowCover)
        {
            // 关掉封面时立即释放已解码的位图
            player.CoverImage.Source = null;
            player.CoverImage.Opacity = 0;
            return;
        }

        // 重新打开时补一张封面:元数据已在,不必再等一次视口事件
        if (player._info is not null && player.CoverImage.Source is null)
            _ = player.LoadCoverAsync(player._info, ++player._generation);
    }

    #endregion
}
