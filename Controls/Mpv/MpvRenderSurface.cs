using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT;
using MpvWinUI.Common;

namespace MpvWinUI.Common;

/// <summary>
///     把 mpv 的 OpenGL 画面接到 XAML 的 <see cref="SwapChainPanel" /> 上。
/// </summary>
/// <remarks>
///     <para>
///         mpv 用 OpenGL 渲染,而 XAML 只认 DirectX。这里的做法是:建一个 D3D11 设备 + 交换链,
///         再通过 <c>WGL_NV_DX_interop</c> 把交换链的后台缓冲注册成 OpenGL 的 renderbuffer,
///         mpv 直接画到这个 FBO 上,最后由交换链提交给 XAML 合成。
///     </para>
///     <para>
///         这个类刻意不继承 <see cref="ContentControl" />:WinUI 控件的托管子类在本项目里
///         赋值 <c>Content</c> 会抛 <c>E_NOINTERFACE</c>(CsWinRT 的 AOT 投影路径问题),
///         而 SwapChainPanel 由宿主 XAML 提供,这里只负责接管。
///     </para>
/// </remarks>
public sealed unsafe class MpvRenderSurface : IDisposable
{
    private readonly SwapChainPanel _panel;
    private readonly ContextSettings _settings;

    private RenderContext? _context;
    private FrameBuffer? _frameBuffer;
    private TimeSpan _lastRenderingTime = TimeSpan.FromSeconds(-1);
    private bool _isActive;
    private bool _isDisposed;

    /// <summary>已经提交的帧数(诊断用)。</summary>
    public long RenderCount { get; private set; }

    /// <summary>初始化 <see cref="MpvRenderSurface" />。</summary>
    ///     初始化 <see cref="MpvRenderSurface" />。
    /// </summary>
    /// <param name="panel">承载画面的 XAML 面板。</param>
    /// <param name="settings">OpenGL 上下文设置,必须与后续 mpv 渲染上下文一致。</param>
    public MpvRenderSurface(SwapChainPanel panel, ContextSettings settings)
    {
        _panel = panel;
        _settings = settings;

        _panel.SizeChanged += OnPanelSizeChanged;
        _panel.CompositionScaleChanged += OnCompositionScaleChanged;
        _panel.Unloaded += OnPanelUnloaded;
    }

    /// <summary>每帧回调;参数是距上一帧的时间。</summary>
    public event Action<TimeSpan>? Render;

    /// <summary>交换链与 GL 上下文是否已就绪。</summary>
    public bool IsInitialized => _context is not null;

    /// <summary>合成缩放(高 DPI)。</summary>
    public double ScaleX => _panel.CompositionScaleX;

    /// <summary>合成缩放(高 DPI)。</summary>
    public double ScaleY => _panel.CompositionScaleY;

    /// <summary>启动渲染循环并建立交换链。只有在面板已经有尺寸后才能调用。</summary>
    public void Initialize()
    {
        if (_isDisposed || _context is not null) return;

        _context = new RenderContext(_settings);

        if (!TryCreateFrameBuffer()) UpdateFrameBufferSize();

        _isActive = true;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>当前 FBO 句柄,交给 mpv 渲染用。</summary>
    public int GetBufferHandle() => _frameBuffer?.GLFrameBufferHandle ?? 0;

    /// <summary>停止渲染并释放交换链 / GL 资源,之后可以重新 <see cref="Initialize" />。</summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        _isActive = false;
        CompositionTarget.Rendering -= OnRendering;

        _panel.SizeChanged -= OnPanelSizeChanged;
        _panel.CompositionScaleChanged -= OnCompositionScaleChanged;
        _panel.Unloaded -= OnPanelUnloaded;

        try
        {
            _frameBuffer?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("MpvRenderSurface", $"释放交换链失败:{ex.Message}");
        }

        _frameBuffer = null;
        _context = null;
    }

    private void OnRendering(object? sender, object e)
    {
        if (!_isActive || e is not RenderingEventArgs args) return;
        if (_lastRenderingTime == args.RenderingTime) return;

        _lastRenderingTime = args.RenderingTime;

        var frameBuffer = _frameBuffer;
        if (frameBuffer is null) return;

        try
        {
            frameBuffer.Begin();
            Render?.Invoke(TimeSpan.Zero);
            frameBuffer.End();
            RenderCount++;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("MpvRenderSurface", $"渲染失败:{ex.Message}");
        }
    }

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_context is null || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;

        if (!TryCreateFrameBuffer()) UpdateFrameBufferSize();
    }

    private void OnCompositionScaleChanged(SwapChainPanel sender, object args) => UpdateFrameBufferSize();

    private void OnPanelUnloaded(object sender, RoutedEventArgs e)
    {
        // 面板离开可视树后不再往它上面提交帧
        _isActive = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void UpdateFrameBufferSize()
        => _frameBuffer?.UpdateSize((int)_panel.ActualWidth, (int)_panel.ActualHeight, ScaleX, ScaleY);

    private bool TryCreateFrameBuffer()
    {
        if (_context is null || _frameBuffer is not null) return false;

        if (_panel.ActualWidth <= 0 || _panel.ActualHeight <= 0) return false;

        var frameBuffer = new FrameBuffer(_context, (int)_panel.ActualWidth, (int)_panel.ActualHeight, ScaleX, ScaleY);
        _panel.As<ISwapChainPanelNative>().SetSwapChain(frameBuffer.SwapChainHandle);
        _frameBuffer = frameBuffer;

        // 首次建链后按 DPI 缩放补一次矩阵,否则高 DPI 下画面会糊
        frameBuffer.UpdateSize((int)_panel.ActualWidth, (int)_panel.ActualHeight, ScaleX, ScaleY);
        return true;
    }
}
