using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Playback;
using CC98.Controls.Primitives;
using CC98.Controls.UbbTextBlock.Common.Events;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

// MusicPlayer.xaml.cs

namespace CC98.Controls.MusicPlayer;

/// <summary>
///     帖子内嵌的音频播放器：海报 + 歌名 + 作者 + 进度条 + 播放/下载/音量。
/// </summary>
public sealed partial class MusicPlayer : UserControl, IDisposable
{
    /// <summary>暂停态圆盘直径（封面 52 的 45.5%）。</summary>
    private const double PlayChipSize = 24;

    /// <summary>播放态圆盘直径（封面 52 的 30.3%）。</summary>
    private const double PlayChipSizePlaying = 16;

    /// <summary>播放态下圆盘的斜向位移（横纵相同即为 45°）。</summary>
    private const double PlayChipOffset = 15;

    /// <summary>暂停态图标字号。</summary>
    private const double PlayChipIconSize = 12;

    /// <summary>图标随圆盘等比例缩小（参考稿里图标始终约占圆盘 45%）。</summary>
    private const double PlayChipIconSizePlaying = PlayChipIconSize * PlayChipSizePlaying / PlayChipSize;

    private const int PlayChipAnimationMs = 240;

    private const double DefaultVolumePercent = 70;

    /// <summary>静音前记住的音量，取消静音时恢复。</summary>
    private double _lastVolumePercent = DefaultVolumePercent;

    /// <summary>正在把全局播放器的音量同步到滑块，此时不要回写全局播放器。</summary>
    private bool _isSyncingVolume = true;

    /// <summary>正在程序化地同步进度滑块，此时不要触发 seek。</summary>
    private bool _isSyncingProgress;

    /// <summary>用户正在拖拽进度条，此时定时器不要抢占滑块。</summary>
    private bool _isSeeking;

    private readonly Storyboard _equalizerStoryboard = new();

    private bool _disposed;
    private readonly GlobalMediaPlayer _globalPlayer;
    private bool _isInitialized;
    private bool _isPlaying;
    private DispatcherTimer _progressTimer;
    private DispatcherTimer? _errorTimer;

    public MusicPlayer()
    {
        //先拿到全局播放器再加载 XAML:滑块在 InitializeComponent 期间就会触发 ValueChanged,
        //此时 _globalPlayer 若还是 null 会直接抛异常
        _globalPlayer = GlobalMediaPlayer.Instance;
        InitializeComponent();

        SetupProgressTimer();
        SetupSeekGuards();
        SetupEqualizer();
        ApplyPlayChipState(false);

        //事件订阅改由 Loaded/Unloaded 管理:单例的事件委托是强引用,
        //控件脱离可视树后若不退订,会把整棵页面树(连同数据与已解码位图)钉在内存里
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #region 下载功能

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Src))
            OnDownloadStarted();
        else
            ShowErrorMessage("没有可下载的音频源");
    }

    #endregion

    #region 依赖属性

    public static readonly DependencyProperty SrcProperty =
        DependencyProperty.Register("Src", typeof(string), typeof(MusicPlayer),
            new(null, OnSrcChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register("Title", typeof(string), typeof(MusicPlayer),
            new("音频"));

    public static readonly DependencyProperty AuthorProperty =
        DependencyProperty.Register("Author", typeof(string), typeof(MusicPlayer),
            new(""));

    public static readonly DependencyProperty PosterProperty =
        DependencyProperty.Register("Poster", typeof(string), typeof(MusicPlayer),
            new("", OnPosterChanged));

    public static readonly DependencyProperty LoadMediaCallbackProperty =
        DependencyProperty.Register("LoadMediaCallback", typeof(IMediaLoader),
            typeof(MusicPlayer), new(new DefaultMediaLoader()));

    public string Src
    {
        get => (string)GetValue(SrcProperty);
        set => SetValue(SrcProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>作者 / 歌手名，对应 UBB 的 <c>[audio,title=…,author=…]</c>。</summary>
    public string Author
    {
        get => (string)GetValue(AuthorProperty);
        set => SetValue(AuthorProperty, value);
    }

    /// <summary>海报图片地址；留空或取不到图时显示内置默认海报。</summary>
    public string Poster
    {
        get => (string)GetValue(PosterProperty);
        set => SetValue(PosterProperty, value);
    }

    public IMediaLoader LoadMediaCallback
    {
        get => (IMediaLoader)GetValue(LoadMediaCallbackProperty);
        set => SetValue(LoadMediaCallbackProperty, value);
    }

    #endregion

    #region 事件

    public event EventHandler<MediaClickEventArgs> DownloadStarted;

    private void OnDownloadStarted()
    {
        DownloadStarted?.Invoke(this, new(Src, MediaType.Audio));
    }

    #endregion

    #region 初始化

    private bool _eventsSubscribed;

    private void SubscribeGlobalPlayerEvents()
    {
        if (_eventsSubscribed) return;
        _eventsSubscribed = true;

        _globalPlayer.MediaOpened += OnMediaOpened;
        _globalPlayer.MediaEnded += OnMediaEnded;
        _globalPlayer.MediaFailed += OnMediaFailed;
        _globalPlayer.CurrentStateChanged += OnCurrentStateChanged;
    }

    private void UnsubscribeGlobalPlayerEvents()
    {
        if (!_eventsSubscribed) return;
        _eventsSubscribed = false;

        _globalPlayer.MediaOpened -= OnMediaOpened;
        _globalPlayer.MediaEnded -= OnMediaEnded;
        _globalPlayer.MediaFailed -= OnMediaFailed;
        _globalPlayer.CurrentStateChanged -= OnCurrentStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeGlobalPlayerEvents();
        SyncVolumeFromPlayer();
        UpdatePoster();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        //脱离可视树:停止播放与进度定时器,断开单例事件,避免钉住整页
        if (_isPlaying)
        {
            _globalPlayer.Pause();
            _isPlaying = false;
        }
        _progressTimer.Stop();
        UnsubscribeGlobalPlayerEvents();
        _isInitialized = false;
        UpdatePlayPauseButton();
    }

    private void SetupProgressTimer()
    {
        _progressTimer = new();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(250);
        _progressTimer.Tick += UpdateProgress;
    }

    /// <summary>拖拽进度条期间让定时器让位，否则滑块每 250ms 会被弹回原位。</summary>
    private void SetupSeekGuards()
    {
        ProgressSlider.PointerPressed += (_, _) => _isSeeking = true;
        ProgressSlider.PointerReleased += (_, _) => _isSeeking = false;
        ProgressSlider.PointerCaptureLost += (_, _) => _isSeeking = false;
        ProgressSlider.PointerCanceled += (_, _) => _isSeeking = false;
    }

    /// <summary>
    ///     三格律动动画。**在代码里直接挂到 ScaleTransform 上**，
    ///     不走资源字典里的 Storyboard —— 资源字典有独立的命名域，
    ///     那里的 Storyboard.TargetName 解析不到可视树里的元素，Begin() 会直接抛异常
    ///     （曾表现为：音频正常播放，却跳出"初始化失败"，且进度条纹丝不动）。
    /// </summary>
    private void SetupEqualizer()
    {
        AddBarAnimation(EqScale1, 0);
        AddBarAnimation(EqScale2, 0.18);
        AddBarAnimation(EqScale3, 0.36);
    }

    private void AddBarAnimation(ScaleTransform target, double delaySeconds)
    {
        var animation = new DoubleAnimation
        {
            From = 0.35,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(450)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(delaySeconds)
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "ScaleY");
        _equalizerStoryboard.Children.Add(animation);
    }

    private static void OnSrcChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MusicPlayer player && !player._isPlaying)
        {
            player._isInitialized = false;
            player.ResetUi();
        }
    }

    /// <summary>内置默认海报。Poster 为空或取不到图时用它。</summary>
    private const string DefaultPosterUri = "ms-appx:///Assets/audio_cover.png";

    /// <summary>当前显示的是不是内置默认海报。防止默认海报自己也加载失败时反复回退。</summary>
    private bool _isDefaultPoster = true;

    private static void OnPosterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MusicPlayer player) player.UpdatePoster();
    }

    /// <summary>海报地址为空或非法时回退到内置默认海报。</summary>
    private void UpdatePoster()
    {
        var poster = Poster;

        if (string.IsNullOrWhiteSpace(poster))
        {
            ShowDefaultPoster();
            return;
        }

        try
        {
            //先标记成自定义海报，否则下面的 ImageFailed 会以为自己就是默认图而不回退
            _isDefaultPoster = false;
            PosterImage.Source = new BitmapImage(new Uri(poster, UriKind.Absolute));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("MusicPlayer", $"海报地址无效:{ex.Message}");
            ShowDefaultPoster();
        }
    }

    /// <summary>把海报换回内置默认图。</summary>
    private void ShowDefaultPoster()
    {
        _isDefaultPoster = true;
        PosterImage.Source = new BitmapImage(new Uri(DefaultPosterUri, UriKind.Absolute));
    }

    /// <summary>
    ///     海报加载失败（404、断网等）时退回内置图。
    /// </summary>
    /// <remarks>
    ///     <see cref="BitmapImage" /> 是异步取图的：地址本身不合法在上面的 <c>new Uri</c> 就抛掉了，
    ///     走到这里的都是「地址看着没问题、但图取不回来」的情况，此时 Image 会是一片空白，
    ///     所以必须显式换回默认图——这也是只用单个 Image 时唯一需要自己补的一步。
    /// </remarks>
    private void PosterImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (_isDefaultPoster) return;

        Debug.WriteLine("MusicPlayer", $"海报加载失败:{e.ErrorMessage}");
        ShowDefaultPoster();
    }

    #endregion

    #region 播放控制

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_disposed) return;

        if (!await EnsureInitializedAsync()) return;

        try
        {
            TogglePlayPause();
        }
        catch (Exception ex)
        {
            //UI 层的异常不能再被当成"初始化失败",否则状态自相矛盾（播着却报错、按不动）
            Debug.WriteLine("MusicPlayer", $"切换播放状态出错:{ex}");
            ShowErrorMessage("播放控制出错");
        }
    }

    /// <summary>把「加载媒体源」这一步单独隔离出来，只有它失败才算初始化失败。</summary>
    private async Task<bool> EnsureInitializedAsync()
    {
        if (_isInitialized) return true;

        if (string.IsNullOrEmpty(Src))
        {
            ShowErrorMessage("音频源为空");
            return false;
        }

        LoadingIndicator.Visibility = Visibility.Visible;
        try
        {
            var mediaSource = await LoadMediaCallback.LoadMedia(Src);

            if (mediaSource == null)
            {
                ShowErrorMessage($"无法加载媒体:{Src}");
                return false;
            }

            _globalPlayer.SetSource(mediaSource);
            _isInitialized = true;
            ProgressSlider.IsEnabled = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("MusicPlayer", $"初始化媒体出错:{ex}");
            ShowErrorMessage("初始化失败");
            _isInitialized = false;
            return false;
        }
        finally
        {
            LoadingIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void TogglePlayPause()
    {
        if (_isPlaying)
        {
            _globalPlayer.Pause();
            _progressTimer.Stop();
        }
        else
        {
            _globalPlayer.Play();
            _progressTimer.Start();
        }

        _isPlaying = !_isPlaying;
        UpdatePlayPauseButton();
    }

    private void UpdatePlayPauseButton()
    {
        PlayPauseIcon.Glyph = _isPlaying ? "\uE769" : "\uE768"; // 暂停/播放图标
        ToolTipService.SetToolTip(PlayPauseButton, _isPlaying ? "暂停" : "播放");
        AnimatePlayChip(_isPlaying);

        if (_isPlaying)
        {
            Equalizer.Visibility = Visibility.Visible;
            _equalizerStoryboard.Begin();
        }
        else
        {
            _equalizerStoryboard.Stop();
            Equalizer.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    ///     播放态：封面圆盘沿 45° 斜向右下位移并缩小到 0.72；暂停态回到正中并复原。
    /// </summary>
    private void AnimatePlayChip(bool playing)
    {
        var storyboard = new Storyboard();

        // 位移走合成动画（快）；直径与字号是布局属性，只能开 EnableDependentAnimation ——
        // 这样白环粗细恒定、图标随圆盘等比例缩小，跟参考稿一致
        AddChipAnimation(storyboard, PlayChipTransform, "X", playing ? PlayChipOffset : 0, false);
        AddChipAnimation(storyboard, PlayChipTransform, "Y", playing ? PlayChipOffset : 0, false);
        AddChipAnimation(storyboard, PlayChip, "Width", playing ? PlayChipSizePlaying : PlayChipSize, true);
        AddChipAnimation(storyboard, PlayChip, "Height", playing ? PlayChipSizePlaying : PlayChipSize, true);
        AddChipAnimation(storyboard, PlayPauseIcon, "FontSize",
            playing ? PlayChipIconSizePlaying : PlayChipIconSize, true);

        storyboard.Begin();
    }

    private static void AddChipAnimation(Storyboard storyboard, DependencyObject target, string property, double to,
        bool dependent)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(PlayChipAnimationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = dependent
        };

        //直接挂到目标对象上，不走 TargetName，避免资源字典命名域的问题
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    /// <summary>不带动画地把圆盘摆到指定状态（构造与换源复位时用）。</summary>
    private void ApplyPlayChipState(bool playing)
    {
        var offset = playing ? PlayChipOffset : 0;

        PlayChipTransform.X = offset;
        PlayChipTransform.Y = offset;
        PlayChip.Width = playing ? PlayChipSizePlaying : PlayChipSize;
        PlayChip.Height = playing ? PlayChipSizePlaying : PlayChipSize;
        PlayPauseIcon.FontSize = playing ? PlayChipIconSizePlaying : PlayChipIconSize;
    }

    #endregion

    #region 音量控制

    /// <summary>把全局播放器的当前音量同步到滑块（构造与每次加载时各做一次）。</summary>
    private void SyncVolumeFromPlayer()
    {
        _isSyncingVolume = true;
        var percent = Math.Clamp(_globalPlayer.Volume, 0, 1) * 100;
        VolumeSlider.Value = percent;
        if (percent > 0) _lastVolumePercent = percent;
        _isSyncingVolume = false;
        UpdateVolumeVisual(percent);
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        //构造期间 XAML 的 Value="70" 会触发一次,此时不能回写全局音量
        if (_isSyncingVolume) return;

        var percent = Math.Clamp(e.NewValue, 0, 100);
        _globalPlayer.Volume = percent / 100.0;
        if (percent > 0) _lastVolumePercent = percent;
        UpdateVolumeVisual(percent);
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        VolumeSlider.Value = VolumeSlider.Value > 0 ? 0 : _lastVolumePercent;
    }

    /// <summary>浮层里的喇叭按钮图标始终是「静音」，外面那颗跟着当前音量变。</summary>
    private void UpdateVolumeVisual(double percent)
    {
        var muted = percent <= 0;
        VolumeIcon.Glyph = muted ? "\uE74F" : "\uE767"; // 静音 / 音量
        ToolTipService.SetToolTip(VolumeButton, muted ? "静音中" : "音量");
        ToolTipService.SetToolTip(MuteButton, muted ? "取消静音" : "静音");
    }

    #endregion

    #region 进度条控制

    private void UpdateProgress(object sender, object e)
    {
        var session = _globalPlayer?.PlaybackSession;
        if (session == null || session.NaturalDuration.TotalSeconds <= 0) return;

        var current = session.Position.TotalSeconds;
        var total = session.NaturalDuration.TotalSeconds;

        //用户正在拖拽时不要抢占滑块，但时间文字照常不动
        if (!_isSeeking)
        {
            _isSyncingProgress = true;
            ProgressSlider.Value = current / total * 100;
            _isSyncingProgress = false;
        }

        CurrentTimeText.Text = FormatTime(current);
        TotalTimeText.Text = FormatTime(total);
    }

    private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        //定时器同步滑块时触发的回调不应反过来再 seek 一次
        if (_isSyncingProgress) return;

        var session = _globalPlayer?.PlaybackSession;
        if (session == null || session.NaturalDuration.TotalSeconds <= 0) return;

        session.Position = TimeSpan.FromSeconds(e.NewValue / 100.0 * session.NaturalDuration.TotalSeconds);
        CurrentTimeText.Text = FormatTime(session.Position.TotalSeconds);
    }

    private string FormatTime(double seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        return $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";
    }

    #endregion

    #region MediaPlayer 事件处理

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        // 媒体打开成功
        DispatcherQueue.TryEnqueue(() =>
        {
            TotalTimeText.Text = FormatTime(sender.PlaybackSession.NaturalDuration.TotalSeconds);
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _isPlaying = false;
            UpdatePlayPauseButton();
            _progressTimer.Stop();
            _isSyncingProgress = true;
            ProgressSlider.Value = 0;
            _isSyncingProgress = false;
            CurrentTimeText.Text = "0:00";
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ShowErrorMessage($"播放失败: {args.ErrorMessage}");
            _isPlaying = false;
            _isInitialized = false;
            UpdatePlayPauseButton();
            _progressTimer.Stop();
            ResetUi();
        });
    }

    private void OnCurrentStateChanged(MediaPlayer sender, object args)
    {
        // 可以根据需要处理状态变化
    }

    #endregion

    #region UI 辅助方法

    private void ResetUi()
    {
        _isSyncingProgress = true;
        ProgressSlider.Value = 0;
        _isSyncingProgress = false;
        ProgressSlider.IsEnabled = false;
        CurrentTimeText.Text = "0:00";
        TotalTimeText.Text = "0:00";
        PlayPauseIcon.Glyph = "\uE768";
        ToolTipService.SetToolTip(PlayPauseButton, "播放");
        ApplyPlayChipState(false);
        _equalizerStoryboard.Stop();
        Equalizer.Visibility = Visibility.Collapsed;
    }

    private void ShowErrorMessage(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;

        //复用同一个定时器:每次新建的 timer 在 3 秒窗口内会持续 tick 并钉住控件
        if (_errorTimer == null)
        {
            _errorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _errorTimer.Tick += (s, e) =>
            {
                _errorTimer.Stop();
                ErrorTextBlock.Visibility = Visibility.Collapsed;
            };
        }
        _errorTimer.Stop();
        _errorTimer.Start();
    }

    #endregion

    #region IDisposable 实现

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            _progressTimer?.Stop();
            _errorTimer?.Stop();
            _equalizerStoryboard.Stop();

            //只取消订阅事件，不释放全局播放器
            UnsubscribeGlobalPlayerEvents();
        }
    }

    #endregion
}
