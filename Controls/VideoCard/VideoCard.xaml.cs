// VideoCard.xaml.cs
//
// UBB [video] 的卡片外壳:布局与 [bili] 卡片保持一致(卡片底 + 圆角 + 媒体区 + 信息栏),
// 媒体区按内容自身比例定尺寸,点击才创建 mpv 播放器。

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CC98.Kernel.Authorize;
using CC98.Services.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using CC98.Kernel;
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

    /// <summary>
    ///     初始化 <see cref="VideoCard" />。
    /// </summary>
    public VideoCard()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
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

        if (ShowPoster && !_isPosterLoaded) _ = LoadPosterAsync(_lifetimeCts.Token);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelInFlight();
        DestroyPlayer();

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
            Debug.WriteLine("VideoCard", $"封面加载失败:{ex.Message}");
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
            Debug.WriteLine("VideoCard", $"读取封面尺寸失败:{ex.Message}");
            return null;
        }
    }

    #endregion

    #region 播放

    private void PlayButton_Click(object sender, RoutedEventArgs e) => _ = StartPlaybackAsync();

    private async Task StartPlaybackAsync()
    {
        if (_player is not null || string.IsNullOrWhiteSpace(Source)) return;

        ShowBusy(true);

        var player = new VideoPlayer
        {
            Src = Source,
            AutoPlay = true,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // CC98 自己的媒体要带登录令牌,mpv 直接拉流拿不到 ForumClient 的处理器
        if (Uri.TryCreate(Source, UriKind.Absolute, out var uri)
            && uri.Host.EndsWith("cc98.org", StringComparison.OrdinalIgnoreCase))
        {
            var token = App.Current.GetService<ITokenService>()?.AccessToken;
            if (!string.IsNullOrEmpty(token)) player.HttpHeaders = $"Authorization: Bearer {token}";
        }

        player.VideoFailed += OnPlayerFailed;
        player.VideoLoaded += OnPlayerLoaded;
        player.VideoAspectRatioChanged += OnPlayerAspectRatioChanged;

        PlayerHost.Children.Add(player);
        PlayerHost.Visibility = Visibility.Visible;
        _player = player;

        var started = await player.LoadVideoAsync();
        if (!started && ReferenceEquals(_player, player))
        {
            DestroyPlayer();
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
    }

    private void OnPlayerAspectRatioChanged(object? sender, double aspect)
    {
        if (aspect <= 0) return;

        _videoAspect = aspect;
        if (_playerReady) UpdateMediaHeight();
    }

    private void OnPlayerFailed(object? sender, VideoPlayerFailedEventArgs e)
    {
        Debug.WriteLine("VideoCard", $"播放失败:{e.ErrorMessage}");
        DestroyPlayer();
        ShowBusy(false);
        ShowStatus($"播放失败:{e.ErrorMessage}");
    }

    private void DestroyPlayer()
    {
        var player = _player;
        if (player is null) return;

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
            Debug.WriteLine("VideoCard", $"播放器移除失败:{ex.Message}");
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
            Debug.WriteLine("VideoCard", $"播放器释放失败:{ex.Message}");
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
