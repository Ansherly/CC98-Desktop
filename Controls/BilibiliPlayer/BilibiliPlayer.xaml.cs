using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CC98.Kernel.Bilibili;
using System.Runtime.InteropServices.WindowsRuntime;
using CC98.Services.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    // 首帧到手之前继续显示封面:否则 mpv 初始化那两秒会闪一下黑底
    private bool _playerReady;

    // 封面 / 视频的真实宽高比(宽÷高):媒体区高度跟着它走,而不是固定 16:9
    private double? _coverAspect;
    private double? _videoAspect;
    private CancellationTokenSource? _lifetimeCts;
    private Stage _stage = Stage.Idle;

    // 代次:控件被回收 / 参数变更后,在途请求的结果不得再回填到 UI
    private int _generation;

    /// <summary>
    ///     初始化 <see cref="BilibiliPlayer" />。
    /// </summary>
    public BilibiliPlayer()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        UpdateVisuals();
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
        DestroyPlayer();

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
            SetStage(Stage.Failed, "视频信息获取失败,可点右上角「在 B 站打开」");
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
        // Idle:视口事件没来(极少数宿主),点击就是"立刻加载";Failed:允许点一次重试
        if (_stage is Stage.Idle or Stage.Failed)
        {
            await LoadInfoAsync();
            if (_stage != Stage.Ready) return;
        }

        if (_stage == Stage.Ready) await StartPlaybackAsync();
    }

    private async Task StartPlaybackAsync()
    {
        if (_info is null || _stage is Stage.Resolving or Stage.Playing) return;

        var part = _info.GetPart(Page);
        if (part is null)
        {
            SetStage(Stage.Failed, "该视频没有可播放的分 P");
            return;
        }

        var generation = ++_generation;
        var token = LifetimeToken;

        SetStage(Stage.Resolving, "正在获取播放地址…");

        var play = await Api.GetPlayUrlAsync(_info.Bvid, part.Cid, token);
        if (generation != _generation) return;

        if (play is null)
        {
            SetStage(Stage.Failed, "未取到播放地址(可能被风控),可点右上角「在 B 站打开」");
            return;
        }

        DurationText.Text = $"{FormatDuration(_info.Duration)} · {play.QualityText}";

        // 拿到直链后直接交给 mpv:参考头由播放器带上,不再走"下载到缓存再播"
        SetStage(Stage.Playing, play.QualityText);

        PlayUrl(play.Url);
    }

    // B 站 CDN 会校验 UA,用浏览器 UA 最稳
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>
    ///     直接播放直链:mpv 自己发 HTTP 请求,带上 Referer / UA 就能过 CDN 的防盗链,
    ///     所以不需要"先下载完整文件再播"。
    /// </summary>
    private void PlayUrl(string url)
    {
        DestroyPlayer();

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

        PlayerHost.Children.Add(player);
        _player = player;

        SetStage(Stage.Playing);
        _ = player.LoadVideoAsync();
    }

    private void OnPlayerLoaded(object? sender, VideoPlayerEventArgs e)
    {
        _playerReady = true;
        UpdateVisuals();
    }

    private void OnPlayerFailed(object? sender, VideoPlayerFailedEventArgs e)
    {
        Debug.WriteLine("Bilibili", $"播放失败:{e.ErrorMessage}");
        DestroyPlayer();
        SetStage(Stage.Failed, $"播放失败:{e.ErrorMessage}");
    }


    #endregion

    #region 状态与清理

    private void SetStage(Stage stage, string? status = null)
    {
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
        // 隐藏会让播放区域量到 0,mpv 那边会当“区域没准备好”。
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

    private void DestroyPlayer()
    {
        var player = _player;
        if (player is null) return;

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
        DestroyPlayer();

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
