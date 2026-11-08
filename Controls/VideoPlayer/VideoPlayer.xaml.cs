using System;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CC98.Controls.Interop;
using Microsoft.UI.Dispatching;
using CC98.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using WinRT;
using WinRTSurface = Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface;

namespace CC98.Controls;

/// <summary>
///     视频播放控件(Media Foundation)。
/// </summary>
/// <remarks>
///     <para>MediaPlayer 走"帧服务器"模式:只负责解码,不发显示。每一帧通过
///     <see cref="MediaPlayer.CopyFrameToVideoSurface(WinRTSurface, Rect)" /> 拷进我们自己的 D3D11
///     纹理,再 <c>Present</c> 到 SwapChainPanel 的合成交换链上。</para>
///     <para>之所以不用 MediaPlayerElement:它在控件卸载/拆树过程中被 Dispose 时会以
///     E_ABORT(0x80004004) 触发原生 failfast(0xc000027b),托管层的 try/catch 拦不住。</para>
/// </remarks>
public sealed partial class VideoPlayer : UserControl, IDisposable
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>渲染资源的锁:帧回调在 MF 线程,换尺寸在 UI 线程。</summary>
    private readonly object _renderLock = new();

    /// <summary>已经有一帧排队到 UI 线程了:帧率高于渲染时直接丢,避免队列堆积。</summary>
    private bool _framePending;

    // 渲染:全部用 IntPtr 存句柄,避免把整个类标成 unsafe(XAML 生成了另一半 partial)
    private IntPtr _device;
    private IntPtr _context;
    private IntPtr _swapChain;
    private IntPtr _backBuffer;
    private IntPtr _renderTargetView;
    private WinRTSurface? _frameSurface;
    private int _pixelWidth;
    private int _pixelHeight;
    private long _renderedFrames;

    private MediaPlayer? _player;
    private DispatcherQueueTimer? _positionTimer;
    private LocalMediaProxy? _proxy;

    private bool _isDisposed;
    private bool _isInitialized;
    private bool _isMediaLoaded;
    private bool _reportedInitialized;
    private bool _isUpdatingFromPlayer;

    // 静音就是音量 0:不单独用 mute,否则两边状态会打架
    private double _lastVolumePercent = 100;
    private Stretch _stretch = Stretch.Uniform;
    private string? _httpHeaders;
    private string? _referer;
    private string? _userAgent;

    private double? _videoAspect;

    // 防止 Loaded 自动加载与宿主显式调用撞车:重入会重复换源
    private bool _isLoading;

    /// <summary>最近一条播放错误:失败时拼进提示,否则用户只能看到干巴巴的"播放失败"。</summary>
    private string? _lastError;

    /// <summary>
    ///     初始化 <see cref="VideoPlayer" />。
    /// </summary>
    public VideoPlayer()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Surface.SizeChanged += OnSurfaceSizeChanged;
        Surface.CompositionScaleChanged += OnSurfaceCompositionScaleChanged;
    }


    #region 依赖属性

    /// <summary>视频源:本地路径或网络地址。</summary>
    public static readonly DependencyProperty SrcProperty =
        DependencyProperty.Register("Src", typeof(string), typeof(VideoPlayer),
            new(null, OnSrcChanged));

    /// <summary>旧版媒体加载器;保留属性只为兼容既有调用方。</summary>
    public static readonly DependencyProperty LoadVideoCallbackProperty =
        DependencyProperty.Register("LoadVideoCallback", typeof(IMediaLoader),
            typeof(VideoPlayer), new(new DefaultMediaLoader()));

    /// <summary>视频源:本地路径或网络地址。</summary>
    public string Src
    {
        get => (string)GetValue(SrcProperty);
        set => SetValue(SrcProperty, value);
    }

    /// <summary>旧版媒体加载器;当前实现不使用它。</summary>
    public IMediaLoader LoadVideoCallback
    {
        get => (IMediaLoader)GetValue(LoadVideoCallbackProperty);
        set => SetValue(LoadVideoCallbackProperty, value);
    }

    /// <summary>加载完成后是否自动开始播放。</summary>
    public bool AutoPlay { get; set; }

    /// <summary>画面填充方式:Uniform 等比留黑,UniformToFill 等比裁切,Fill 拉伸铺满。</summary>
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
    public long RenderedFrames => Interlocked.Read(ref _renderedFrames);

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

        // 已经有一次加载在跑:Loaded 会自动触发一次,宿主往往又显式调一次,不要重复换源
        if (_isLoading) return false;

        _isLoading = true;

        try
        {
            _lastError = null;
            ErrorText.Visibility = Visibility.Collapsed;
            ShowBusy(true);

            if (!await EnsureInitializedAsync()) return false;

            var player = _player!;
            _isMediaLoaded = false;
            _reportedInitialized = false;
            _videoAspect = null;

            var source = await CreateSourceAsync(src);
            if (source is null)
            {
                ReportError("无法解析视频地址");
                VideoFailed?.Invoke(this, new(Src, "无法解析视频地址"));
                return false;
            }

            player.Source = source;

            if (AutoPlay) player.Play();
            else player.Pause();

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
    public void Pause() => SetPaused(true);

    /// <summary>跳转到指定位置。</summary>
    public void Seek(TimeSpan position)
    {
        try
        {
            var session = _player?.PlaybackSession;
            if (session is null || !session.CanSeek) return;

            var duration = session.NaturalDuration;
            if (duration > TimeSpan.Zero && position > duration) position = duration;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;

            session.Position = position;
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
            if (_player is null) return;

            if (paused) _player.Pause();
            else _player.Play();
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

        var playing = player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        SetPaused(playing);
    }

    #region 初始化与渲染

    private async Task<bool> EnsureInitializedAsync()
    {
        if (_isInitialized) return true;

        await WaitForLayoutAsync();

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            ShowBusy(false);
            ReportError("播放区域还没准备好,请再试一次");
            return false;
        }

        // 面板默认是 Collapsed,先亮出来它才会有实际尺寸(否则交换链只能是 1x1)
        Surface.Visibility = Visibility.Visible;
        Surface.PointerPressed -= OnRenderPointerPressed;
        Surface.PointerPressed += OnRenderPointerPressed;

        for (var i = 0; i < 20 && Surface.ActualWidth <= 0; i++)
        {
            await Task.Delay(25);
        }
        try
        {
            CreateDevice();
            var panelWidth = Surface.ActualWidth > 0 ? Surface.ActualWidth : ActualWidth;
            var panelHeight = Surface.ActualHeight > 0 ? Surface.ActualHeight : ActualHeight;
            var width = Math.Max(1, (int)Math.Round(panelWidth * Surface.CompositionScaleX));
            var height = Math.Max(1, (int)Math.Round(panelHeight * Surface.CompositionScaleY));
            CreateSwapChain(width, height);
        }
        catch (Exception ex)
        {
            ShowBusy(false);
            ReportError($"渲染初始化失败:{ex.Message}");
            ReleaseRenderResources();
            return false;
        }

        var player = new MediaPlayer
        {
            IsVideoFrameServerEnabled = true,
            AutoPlay = false,
            Volume = Math.Clamp(VolumeSlider.Value, 0, 100) / 100.0,
        };

        try { player.CommandManager.IsEnabled = false; } catch { }

        player.MediaOpened += OnMediaOpened;
        player.MediaEnded += OnMediaEnded;
        player.MediaFailed += OnMediaFailed;
        player.VideoFrameAvailable += OnVideoFrameAvailable;
        player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;

        _player = player;
        _isInitialized = true;

        ApplyVolume(VolumeSlider.Value);
        ResizeSwapChain();
        return true;
    }

    /// <summary>
    ///     等一次布局:宿主往往把控件加进可视树后立刻调用 <see cref="LoadVideoAsync" />,
    ///     此时宽高还是 0,交换链没有尺寸可用。
    /// </summary>
    private async Task WaitForLayoutAsync()
    {
        if (Surface.ActualWidth > 0 && Surface.ActualHeight > 0) return;

        var completion = new TaskCompletionSource();

        void OnLayoutUpdated(object? sender, object e)
        {
            if (Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0) return;

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

    private unsafe void CreateDevice()
    {
        if (_device != IntPtr.Zero) return;

        var flags = CreateDeviceFlag.BgraSupport | CreateDeviceFlag.VideoSupport;
        ID3D11Device* device;
        ID3D11DeviceContext* context;
        var hr = D3D11.GetApi(null).CreateDevice(
            null, D3DDriverType.Hardware, 0, (uint)flags, null, 0, D3D11.SdkVersion,
            &device, null, &context);

        if (hr < 0) throw new InvalidOperationException($"D3D11 设备创建失败 0x{hr:X8}");

        _device = (IntPtr)device;
        _context = (IntPtr)context;
    }

    private unsafe void CreateSwapChain(int pixelWidth, int pixelHeight)
    {
        var device = (ID3D11Device*)_device;
        if (device == null) throw new InvalidOperationException("D3D11 设备尚未创建");

        Guid factoryId = typeof(IDXGIFactory2).GetTypeInfo().GUID;
        IDXGIFactory2* factory;
        var hr = DXGI.GetApi(null).CreateDXGIFactory2(0, &factoryId, (void**)&factory);
        if (hr < 0) throw new InvalidOperationException($"DXGI 工厂创建失败 0x{hr:X8}");

        IDXGISwapChain1* chain = null;
        try
        {
            var description = new SwapChainDesc1
            {
                Width = (uint)pixelWidth,
                Height = (uint)pixelHeight,
                Format = Format.FormatB8G8R8A8Unorm,
                Stereo = 0,
                SampleDesc = new SampleDesc(1, 0),
                BufferUsage = DXGI.UsageRenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
                Flags = 0,
            };

            hr = factory->CreateSwapChainForComposition((IUnknown*)device, &description, null, &chain);
            if (hr < 0) throw new InvalidOperationException($"交换链创建失败 0x{hr:X8}");

            // SwapChainPanel 要按合成缩放把像素尺寸映射回 DIP,否则高 DPI 下画面/位置都不对
            var scaleX = Math.Max(0.01, Surface.CompositionScaleX);
            var scaleY = Math.Max(0.01, Surface.CompositionScaleY);
            ((IDXGISwapChain2*)chain)->SetMatrixTransform(new Matrix3X2F
            {
                DXGI11 = 1.0f / (float)scaleX,
                DXGI22 = 1.0f / (float)scaleY,
            });
        }
        finally
        {
            factory->Release();
        }

        ID3D11Texture2D* backBuffer = null;
        ID3D11RenderTargetView* renderTargetView = null;
        WinRTSurface? frameSurface = null;

        try
        {
            Guid textureId = typeof(ID3D11Texture2D).GetTypeInfo().GUID;
            hr = chain->GetBuffer(0, &textureId, (void**)&backBuffer);
            if (hr < 0) throw new InvalidOperationException($"取后台缓冲失败 0x{hr:X8}");

            hr = device->CreateRenderTargetView((ID3D11Resource*)backBuffer, null, &renderTargetView);
            if (hr < 0) throw new InvalidOperationException($"创建渲染目标失败 0x{hr:X8}");

            frameSurface = CreateFrameSurface(backBuffer);
        }
        catch
        {
            if (renderTargetView != null) renderTargetView->Release();
            if (backBuffer != null) backBuffer->Release();
            chain->Release();
            throw;
        }

        // 先挂新链,再释放旧资源,避免面板指向已释放的交换链
        var previousChain = _swapChain;
        var previousBackBuffer = _backBuffer;
        var previousRtv = _renderTargetView;
        var previousSurface = _frameSurface;

        _swapChain = (IntPtr)chain;
        _backBuffer = (IntPtr)backBuffer;
        _renderTargetView = (IntPtr)renderTargetView;
        _frameSurface = frameSurface;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;

        Surface.As<ISwapChainPanelNative>().SetSwapChain((IntPtr)chain);

        if (previousSurface is not null) previousSurface.Dispose();
        if (previousRtv != IntPtr.Zero) ((ID3D11RenderTargetView*)previousRtv)->Release();
        if (previousBackBuffer != IntPtr.Zero) ((ID3D11Texture2D*)previousBackBuffer)->Release();
        if (previousChain != IntPtr.Zero) ((IDXGISwapChain1*)previousChain)->Release();
    }

    private static unsafe WinRTSurface CreateFrameSurface(ID3D11Texture2D* texture)
    {
        Guid surfaceId = typeof(IDXGISurface).GetTypeInfo().GUID;
        IDXGISurface* dxgiSurface;
        var hr = texture->QueryInterface(&surfaceId, (void**)&dxgiSurface);
        if (hr < 0) throw new InvalidOperationException($"取 DXGI 表面失败 0x{hr:X8}");

        try
        {
            hr = CreateDirect3D11SurfaceFromDXGISurface((IntPtr)dxgiSurface, out var abi);
            if (hr < 0 || abi == IntPtr.Zero) throw new InvalidOperationException($"包装 IDirect3DSurface 失败 0x{hr:X8}");

            try
            {
                return WinRT.MarshalInspectable<WinRTSurface>.FromAbi(abi);
            }
            finally
            {
                Marshal.Release(abi);
            }
        }
        finally
        {
            dxgiSurface->Release();
        }
    }

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr d3dSurface);

    /// <summary>每来一帧:清黑边 → 把画面拷进后台缓冲的对应矩形 → 提交。</summary>
    /// <summary>
    ///     VideoFrameAvailable 在 MF 的工作线程上触发,而 MediaPlayer 本身是 UI 线程创建的
    ///     STA 对象:直接在那里调 CopyFrameToVideoSurface 会抛 RPC_E_WRONG_THREAD(0x8001010E)。
    ///     所以只在这里置位,真正拷贝+提交放到 UI 线程做,来不及就丢帧。
    /// </summary>
    private void OnVideoFrameAvailable(MediaPlayer sender, object args)
    {
        if (_framePending || _isDisposed) return;
        _framePending = true;

        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _framePending = false;
                RenderFrame(sender);
            }))
        {
            _framePending = false;
        }
    }

    private void RenderFrame(MediaPlayer sender)
    {
        lock (_renderLock)
        {
            if (_isDisposed || _frameSurface is null || _swapChain == IntPtr.Zero) return;

            try
            {
                unsafe
                {
                    var context = (ID3D11DeviceContext*)_context;
                    var renderTarget = (ID3D11RenderTargetView*)_renderTargetView;
                    var chain = (IDXGISwapChain1*)_swapChain;

                    if (context != null && renderTarget != null)
                    {
                        var black = stackalloc float[4];
                        context->ClearRenderTargetView(renderTarget, black);
                    }

                    sender.CopyFrameToVideoSurface(_frameSurface, GetVideoRect());
                    chain->Present(1, 0);
                }

                Interlocked.Increment(ref _renderedFrames);
            }
            catch (Exception ex)
            {
                if (_renderedFrames < 3) Debug.WriteLine("VideoPlayer", $"渲染失败:{ex.Message}");
            }
        }
    }

    /// <summary>按视频宽高比和 <see cref="Stretch" /> 算出画面在交换链里占的矩形(像素)。</summary>
    private Rect GetVideoRect()
    {
        var width = _pixelWidth;
        var height = _pixelHeight;

        var session = _player?.PlaybackSession;
        var videoWidth = session?.NaturalVideoWidth ?? 0;
        var videoHeight = session?.NaturalVideoHeight ?? 0;

        if (videoWidth <= 0 || videoHeight <= 0 || width <= 0 || height <= 0)
        {
            return new Rect(0, 0, Math.Max(1, width), Math.Max(1, height));
        }

        var scaleX = (double)width / videoWidth;
        var scaleY = (double)height / videoHeight;

        var scale = _stretch switch
        {
            Stretch.Fill => double.NaN,
            Stretch.UniformToFill => Math.Max(scaleX, scaleY),
            _ => Math.Min(scaleX, scaleY),
        };

        if (double.IsNaN(scale))
        {
            return new Rect(0, 0, width, height);
        }

        var drawWidth = videoWidth * scale;
        var drawHeight = videoHeight * scale;
        return new Rect((width - drawWidth) / 2, (height - drawHeight) / 2, drawWidth, drawHeight);
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => ResizeSwapChain();

    private void OnSurfaceCompositionScaleChanged(SwapChainPanel sender, object args) => ResizeSwapChain();

    private void ResizeSwapChain()
    {
        if (_isDisposed || _swapChain == IntPtr.Zero || !_isInitialized) return;

        var width = Math.Max(1, (int)Math.Round(Surface.ActualWidth * Surface.CompositionScaleX));
        var height = Math.Max(1, (int)Math.Round(Surface.ActualHeight * Surface.CompositionScaleY));

        if (width == _pixelWidth && height == _pixelHeight) return;

        lock (_renderLock)
        {
            try
            {
                CreateSwapChain(width, height);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("VideoPlayer", $"调整交换链尺寸失败:{ex.Message}");
            }
        }
    }

    private void ReleaseRenderResources()
    {
        lock (_renderLock)
        {
            try
            {
                if (Surface is not null)
                {
                    Surface.As<ISwapChainPanelNative>().SetSwapChain(IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("VideoPlayer", $"解绑交换链失败:{ex.Message}");
            }

            _frameSurface?.Dispose();
            _frameSurface = null;

            unsafe
            {
                if (_renderTargetView != IntPtr.Zero) ((ID3D11RenderTargetView*)_renderTargetView)->Release();
                if (_backBuffer != IntPtr.Zero) ((ID3D11Texture2D*)_backBuffer)->Release();
                if (_swapChain != IntPtr.Zero) ((IDXGISwapChain1*)_swapChain)->Release();
                if (_context != IntPtr.Zero) ((ID3D11DeviceContext*)_context)->Release();
                if (_device != IntPtr.Zero) ((ID3D11Device*)_device)->Release();
            }

            _renderTargetView = IntPtr.Zero;
            _backBuffer = IntPtr.Zero;
            _swapChain = IntPtr.Zero;
            _context = IntPtr.Zero;
            _device = IntPtr.Zero;
            _pixelWidth = 0;
            _pixelHeight = 0;
        }
    }

    #endregion

    #region 媒体源

    private async Task<MediaSource?> CreateSourceAsync(string src)
    {
        var headers = BuildHeaders();

        if (Uri.TryCreate(src, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
            {
                // 统一走本机回环代理:能带请求头、能走系统代理,也不受 MF 自己那套网络栈影响;
                // 且不需要在托管代码里实现 WinRT 接口(AOT + full trim 下 CCW 会创建失败)。
                _proxy?.Dispose();
                _proxy = LocalMediaProxy.Start(uri, headers);
                return MediaSource.CreateFromUri(_proxy.Url);
            }

            if (uri.Scheme is "ms-appx" or "ms-appdata") return MediaSource.CreateFromUri(uri);
        }

        var file = await StorageFile.GetFileFromPathAsync(src);
        return MediaSource.CreateFromStorageFile(file);
    }

    private Dictionary<string, string> BuildHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 只有显式给了 Referer / 额外头才走自建流:纯直链交给 MF 自己发请求(它会按 Range 取)
        if (!string.IsNullOrWhiteSpace(_referer)) headers["Referer"] = _referer!;

        if (!string.IsNullOrWhiteSpace(_httpHeaders))
        {
            foreach (var pair in _httpHeaders!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var index = pair.IndexOf(':');
                if (index <= 0) continue;

                var name = pair[..index].Trim();
                var value = pair[(index + 1)..].Trim();
                if (name.Length > 0) headers[name] = value;
            }
        }

        if (headers.Count > 0)
        {
            headers["User-Agent"] = string.IsNullOrWhiteSpace(_userAgent) ? DefaultUserAgent : _userAgent!;
        }

        return headers;
    }

    private static string GuessContentType(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return extension switch
        {
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".ts" => "video/mp2t",
            _ => "video/mp4",
        };
    }

    #endregion

    #region 播放器事件

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        var session = sender.PlaybackSession;
        var width = session.NaturalVideoWidth;
        var height = session.NaturalVideoHeight;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed) return;

            _isMediaLoaded = true;
            ShowBusy(false);
            ShowSurface();
            SetPlayPauseGlyph(true);
            UpdateVideoAspect(width, height);
            StartPositionTimer();

            if (!_reportedInitialized)
            {
                _reportedInitialized = true;
                VideoInitialized?.Invoke(this, new(Src));
                VideoLoaded?.Invoke(this, new(Src));
            }
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed) return;

            _isMediaLoaded = false;
            SetPlayPauseGlyph(false);
            StopPositionTimer();
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _lastError = string.IsNullOrWhiteSpace(args.ErrorMessage)
            ? args.Error.ToString()
            : $"{args.Error}: {args.ErrorMessage}";

        var message = _lastError;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed) return;

            _isMediaLoaded = false;
            StopPositionTimer();
            ReportError("视频播放失败");
            VideoFailed?.Invoke(this, new(Src, message));
        });
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        var state = sender.PlaybackState;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed) return;

            switch (state)
            {
                case MediaPlaybackState.Opening:
                case MediaPlaybackState.Buffering:
                    ShowBusy(true);
                    break;

                case MediaPlaybackState.Playing:
                    ShowBusy(false);
                    ShowSurface();
                    SetPlayPauseGlyph(true);
                    StartPositionTimer();
                    break;

                case MediaPlaybackState.Paused:
                    ShowBusy(false);
                    SetPlayPauseGlyph(false);
                    UpdatePositionUi();
                    break;

                case MediaPlaybackState.None:
                    ShowBusy(false);
                    SetPlayPauseGlyph(false);
                    StopPositionTimer();
                    break;
            }
        });
    }

    /// <summary>
    ///     从播放会话读视频真实尺寸,算出宽高比通知宿主。
    /// </summary>
    private void UpdateVideoAspect(uint width, uint height)
    {
        if (width <= 0 || height <= 0) return;

        var aspect = (double)width / height;
        if (_videoAspect is not null && Math.Abs(_videoAspect.Value - aspect) < 0.001) return;

        _videoAspect = aspect;
        VideoAspectRatioChanged?.Invoke(this, aspect);
    }

    #endregion

    #region 进度

    private void StartPositionTimer()
    {
        if (_positionTimer is not null) return;
        if (DispatcherQueue is null) return;

        // 必须存字段:否则被 GC,定时器停掉
        _positionTimer = DispatcherQueue.CreateTimer();
        _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
        _positionTimer.IsRepeating = true;
        _positionTimer.Tick += (_, _) => UpdatePositionUi();
        _positionTimer.Start();
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = null;
    }

    private void UpdatePositionUi()
    {
        var session = _player?.PlaybackSession;
        if (session is null || _isDisposed) return;

        var duration = session.NaturalDuration.TotalSeconds;
        var position = session.Position.TotalSeconds;

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

        ErrorText.Text = string.IsNullOrEmpty(_lastError) ? message : $"{message}\n{_lastError}";
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SetPlayPauseGlyph(bool playing)
    {
        // E768 = 播放,E769 = 暂停
        PlayPauseIcon.Glyph = playing ? "\uE769" : "\uE768";
    }

    /// <summary>浮层里的喇叭固定是"静音";外面那颗跟着当前音量变(与 MusicPlayer 一致)。</summary>
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
            if (_player is not null) _player.Volume = percent / 100.0;
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
        // 卸载回调处在 XAML 拆树过程中:这里只做"停",不动 D3D/交换链。
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
    ///     释放渲染资源与播放器,可重复调用。宿主在移除控件后、消息循环的下一拍调用最安全。
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        _isMediaLoaded = false;
        _isInitialized = false;
        _reportedInitialized = false;

        StopPositionTimer();

        var player = _player;
        _player = null;

        if (player is not null)
        {
            try
            {
                player.MediaOpened -= OnMediaOpened;
                player.MediaEnded -= OnMediaEnded;
                player.MediaFailed -= OnMediaFailed;
                player.VideoFrameAvailable -= OnVideoFrameAvailable;
                player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;

                player.Pause();
                player.Source = null;
                player.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("VideoPlayer", $"释放播放器失败:{ex.Message}");
            }
        }

        try
        {
            Surface.PointerPressed -= OnRenderPointerPressed;
            Surface.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"释放渲染面失败:{ex.Message}");
        }

        ReleaseRenderResources();

        _proxy?.Dispose();
        _proxy = null;
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

        try
        {
            if (_player is not null)
            {
                await Task.Yield();
                _player.Source = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("VideoPlayer", $"停止旧媒体失败:{ex.Message}");
        }

        if (_isDisposed) return;

        StopPositionTimer();
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
