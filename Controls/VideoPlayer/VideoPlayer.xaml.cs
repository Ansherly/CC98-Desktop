// VideoPlayer.xaml.cs

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using CC98.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CC98.Controls.Extensions;

namespace CC98.Controls.VideoPlayer;

public sealed partial class VideoPlayer : UserControl, IDisposable
{
    private bool _isDisposed;
    private bool _isInitialized;


    public VideoPlayer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #region 加载

    // 当前媒体源:MediaSource 实现了 IDisposable,显式关闭可立即释放其持有的内存流
    private MediaSource? _currentSource;

    /// <summary>
    ///     暂停播放并释放当前媒体源(不清空 _isDisposed,控件可再次加载)。
    /// </summary>
    private void ReleaseMedia()
    {
        try
        {
            if (MediaPlayer.MediaPlayer != null)
            {
                MediaPlayer.MediaPlayer.Pause();
                MediaPlayer.MediaPlayer.Source = null;
            }
            MediaPlayer.Source = null;
        }
        catch
        {
            // 卸载过程中内部播放器可能已失效,忽略
        }
        _currentSource?.Dispose();
        _currentSource = null;
        _isInitialized = false;
    }

    // 手动请求加载视频
    public async Task<bool> LoadVideoAsync()
    {
        return await InitializeVideoAsync();
    }

    #endregion

    #region 视频加载

    private async Task<bool> InitializeVideoAsync()
    {
        if (_isInitialized || _isDisposed || string.IsNullOrEmpty(Src)) return false;

        try
        {
            ShowLoadingIndicator();

            var src = Src; // 在 UI 线程读取依赖属性
            var callback = LoadVideoCallback; // 在 UI 线程读取依赖属性

            // 在后台线程执行加载操作
            var mediaSource = await Task.Run(() =>
            {
                try
                {
                    return callback?.LoadMedia(src);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"后台线程加载失败: {ex.Message}");
                    return null;
                }
            });

            if (mediaSource == null) throw new("无法加载视频源");


            // 切换到 UI 线程设置源
            await DispatcherQueue.EnqueueAsync(() =>
            {
                ReleaseMedia();
                MediaPlayer.Source = mediaSource;
                _currentSource = mediaSource;
                _isInitialized = true;

                HideLoadingIndicator();
                HidePlaceholder();

                VideoInitialized?.Invoke(this, new(Src));

                Debug.WriteLine($"视频初始化成功: {Src}");
            });

            return true;
        }
        catch (Exception ex)
        {
            await DispatcherQueue.EnqueueAsync(() =>
            {
                HideLoadingIndicator();
                ShowErrorMessage($"视频加载失败: {ex.Message}");

                VideoFailed?.Invoke(this, new(Src, ex.Message));

                Debug.WriteLine($"视频初始化失败: {ex.Message}");
            });

            return false;
        }
    }

    #endregion

    #region 依赖属性

    public static readonly DependencyProperty SrcProperty =
        DependencyProperty.Register("Src", typeof(string), typeof(VideoPlayer),
            new(null, OnSrcChanged));

    public static readonly DependencyProperty LoadVideoCallbackProperty =
        DependencyProperty.Register("LoadVideoCallback", typeof(IMediaLoader),
            typeof(VideoPlayer), new(new DefaultMediaLoader()));

    public string Src
    {
        get => (string)GetValue(SrcProperty);
        set => SetValue(SrcProperty, value);
    }

    public IMediaLoader LoadVideoCallback
    {
        get => (IMediaLoader)GetValue(LoadVideoCallbackProperty);
        set => SetValue(LoadVideoCallbackProperty, value);
    }

    // 暴露 MediaPlayerElement 的原始属性
    public MediaPlayerElement MediaPlayerElement => MediaPlayer;

    public bool AreTransportControlsEnabled
    {
        get => MediaPlayer.AreTransportControlsEnabled;
        set => MediaPlayer.AreTransportControlsEnabled = value;
    }

    public MediaTransportControls TransportControls => (MediaTransportControls)MediaPlayer.TransportControls;

    public Stretch Stretch
    {
        get => MediaPlayer.Stretch;
        set => MediaPlayer.Stretch = value;
    }

    public bool AutoPlay
    {
        get => MediaPlayer.AutoPlay;
        set => MediaPlayer.AutoPlay = value;
    }

    #endregion

    #region 事件

    public event EventHandler<VideoPlayerEventArgs> VideoLoaded;
    public event EventHandler<VideoPlayerFailedEventArgs> VideoFailed;
    public event EventHandler<VideoPlayerEventArgs> VideoInitialized;

    #endregion

    #region 初始化与生命周期

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 注册焦点事件


        // 注册 MediaPlayer 事件
        if (MediaPlayer.MediaPlayer != null)
        {
            MediaPlayer.MediaPlayer.MediaOpened += OnMediaOpened;
            MediaPlayer.MediaPlayer.MediaFailed += OnMediaFailed;
            MediaPlayer.MediaPlayer.MediaEnded += OnMediaEnded;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (MediaPlayer.MediaPlayer != null)
        {
            MediaPlayer.MediaPlayer.MediaOpened -= OnMediaOpened;
            MediaPlayer.MediaPlayer.MediaFailed -= OnMediaFailed;
            MediaPlayer.MediaPlayer.MediaEnded -= OnMediaEnded;
        }
        //脱离可视树:暂停并释放媒体源。MediaSource 持有整个文件的内存流,
        //不显式关闭会一直滞留到 GC(视频场景下可达几十 MB)
        ReleaseMedia();
        ShowPlaceholder();
    }

    private static void OnSrcChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VideoPlayer player)
        {
            player.ReleaseMedia();
            player.ShowPlaceholder();
        }
    }

    #endregion

    #region MediaPlayer 事件处理

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            HideLoadingIndicator();
            HidePlaceholder();

            VideoLoaded?.Invoke(this, new(Src));

            Debug.WriteLine($"视频开始播放: {Src}");
        });
    }

    private void OnMediaFailed(MediaPlayer sender,
        MediaPlayerFailedEventArgs args)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            HideLoadingIndicator();
            ShowErrorMessage($"播放失败: {args.ErrorMessage}");

            VideoFailed?.Invoke(this, new(Src, args.ErrorMessage));

            Debug.WriteLine($"视频播放失败: {args.ErrorMessage}");
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            Debug.WriteLine($"视频播放结束: {Src}");
            // 可以在这里添加播放结束后的逻辑
        });
    }

    #endregion

    #region UI 状态管理

    private void LoadingButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadVideoAsync();
    }

    private void ShowPlaceholder()
    {
        PlaceholderGrid.Visibility = Visibility.Visible;
        VideoPlayerGrid.Visibility = Visibility.Collapsed;
    }

    private void HidePlaceholder()
    {
        PlaceholderGrid.Visibility = Visibility.Collapsed;
        VideoPlayerGrid.Visibility = Visibility.Visible;
    }

    private void ShowLoadingIndicator()
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
    }

    private void HideLoadingIndicator()
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
    }

    private void ShowErrorMessage(string message)
    {
        VideoPlayerGrid.Visibility = Visibility.Collapsed;
        PlaceholderGrid.Visibility = Visibility.Collapsed;
    }


    // 重试加载
    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _isInitialized = false;
        ShowPlaceholder();


        _ = InitializeVideoAsync();
    }

    #endregion

    #region IDisposable 实现

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;

            // 清理 MediaPlayer 资源
            // 忽略对已经dispose的对象进行操作
            try
            {
                if (MediaPlayer.MediaPlayer != null)
                {
                    MediaPlayer.MediaPlayer.Pause();
                    MediaPlayer.MediaPlayer.Source = null;
                    MediaPlayer.MediaPlayer.Dispose();
                }

                MediaPlayer.Source = null;
            }
            catch
            {
            }

            _currentSource?.Dispose();
            _currentSource = null;
        }
    }

    #endregion
}

#region 事件参数类

public class VideoPlayerEventArgs : EventArgs
{
    public VideoPlayerEventArgs(string source)
    {
        Source = source;
    }

    public string Source { get; }
}

public class VideoPlayerFailedEventArgs : VideoPlayerEventArgs
{
    public VideoPlayerFailedEventArgs(string source, string errorMessage)
        : base(source)
    {
        ErrorMessage = errorMessage;
    }

    public string ErrorMessage { get; }
}

#endregion