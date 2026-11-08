using Microsoft.UI.Xaml.Media;
using Silk.NET.DXGI;
using OpenTK.Graphics.OpenGL;
using OpenTK.Graphics.Wgl;
using System;
using Silk.NET.Core.Native;
using OpenTK.Platform.Windows;
using Silk.NET.Direct3D11;
using System.Reflection;

namespace MpvWinUI.Common;

/// <summary>
///     交换链 + OpenGL 侧的可渲染表面。
/// </summary>
/// <remarks>
///     <para>
///         颜色附件不是普通的 GL renderbuffer,而是用 <c>WGL_NV_DX_interop</c> 把 DXGI 交换链的
///         后台缓冲"借"给 OpenGL。这个注册动作很贵:每注册一次,驱动都要为那张纹理准备一份共享表面。
///     </para>
///     <para>
///         所以注册只在创建 / 改变尺寸时做一次,每帧只 lock → 画 → unlock → Present。
///         早先的实现每帧 Gen/Delete renderbuffer 并重新注册,按 1080p 算每帧要漏一张 8MB 的
///         RGBA 表面,播放时能看到几百 MB/s 的内存增长。
///     </para>
/// </remarks>
public unsafe class FrameBuffer : FrameBufferBase
{
    private bool _isRegistered;

    public FrameBuffer(
        RenderContext context,
        int frameBufferWidth,
        int frameBufferHeight,
        double compositionScaleX,
        double compositionScaleY)
    {
        Context = context;
        BufferWidth = Convert.ToInt32(frameBufferWidth * compositionScaleX);
        BufferHeight = Convert.ToInt32(frameBufferHeight * compositionScaleY);

        IDXGISwapChain1* swapChain;

        // SwapChain
        {
            SwapChainDesc1 swapChainDesc = new()
            {
                Width = (uint)BufferWidth,
                Height = (uint)BufferHeight,
                Format = Format.FormatB8G8R8A8Unorm,
                Stereo = 0,
                SampleDesc = new SampleDesc()
                {
                    Count = 1,
                    Quality = 0
                },
                BufferUsage = DXGI.UsageRenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                Flags = 0,
                AlphaMode = AlphaMode.Ignore,
            };

            ((IDXGIFactory2*)Context.DxDeviceFactory)->CreateSwapChainForComposition(
                (IUnknown*)Context.DxDeviceHandle, &swapChainDesc, null, &swapChain);

            SwapChainHandle = (IntPtr)swapChain;
        }

        CreateGLResources();
    }

    public RenderContext Context { get; }

    public int GLColorRenderBufferHandle { get; private set; }

    public int GLDepthRenderBufferHandle { get; private set; }

    public int GLFrameBufferHandle { get; private set; }

    public IntPtr DxInteropColorHandle { get; private set; }

    public override int BufferWidth { get; protected set; }

    public override int BufferHeight { get; protected set; }

    public override nint SwapChainHandle { get; protected set; }

    /// <summary>绑定 FBO、设置视口并锁住互操作对象(每帧调用)。</summary>
    public void Begin()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, GLFrameBufferHandle);
        GL.Viewport(0, 0, BufferWidth, BufferHeight);

        if (_isRegistered)
        {
            Wgl.DXLockObjectsNV(Context.GlDeviceHandle, 1, new[] { DxInteropColorHandle });
        }
    }

    /// <summary>解锁互操作对象并提交这一帧(每帧调用)。</summary>
    public void End()
    {
        if (_isRegistered)
        {
            Wgl.DXUnlockObjectsNV(Context.GlDeviceHandle, 1, new[] { DxInteropColorHandle });
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        ((IDXGISwapChain1*)SwapChainHandle)->Present(0, 0);
    }

    public void UpdateSize(
        int framebufferWidth,
        int framebufferHeight,
        double compositionScaleX,
        double compositionScaleY)
    {
        var width = Convert.ToInt32(framebufferWidth * compositionScaleX);
        var height = Convert.ToInt32(framebufferHeight * compositionScaleY);

        // 尺寸没变就只补 DPI 矩阵:ResizeBuffers + 重新注册能省则省
        if (width == BufferWidth && height == BufferHeight)
        {
            ApplyScaleTransform(compositionScaleX, compositionScaleY);
            return;
        }

        BufferWidth = width;
        BufferHeight = height;

        // 后台缓冲会被 ResizeBuffers 换掉,旧的互操作注册随之失效:先拆后建
        ReleaseGLResources();

        ((IDXGISwapChain1*)SwapChainHandle)->ResizeBuffers(2, (uint)BufferWidth, (uint)BufferHeight, Format.FormatUnknown, 0);
        ApplyScaleTransform(compositionScaleX, compositionScaleY);

        CreateGLResources();
    }

    public override void Dispose()
    {
        ReleaseGLResources();
        GC.SuppressFinalize(this);
    }

    private void ApplyScaleTransform(double compositionScaleX, double compositionScaleY)
        => ((IDXGISwapChain2*)SwapChainHandle)->SetMatrixTransform(new Matrix3X2F
        {
            DXGI11 = 1.0f / (float)compositionScaleX,
            DXGI22 = 1.0f / (float)compositionScaleY,
        });

    /// <summary>建立 FBO / 深度缓冲,并把后台缓冲注册进 OpenGL(只在创建与改变尺寸时调用)。</summary>
    private void CreateGLResources()
    {
        GLFrameBufferHandle = GL.GenFramebuffer();
        GLColorRenderBufferHandle = GL.GenRenderbuffer();
        GLDepthRenderBufferHandle = GL.GenRenderbuffer();

        // 颜色附件:直接把交换链后台缓冲(第 0 张)注册成 GL renderbuffer
        {
            ID3D11Texture2D* colorBuffer;
            var guid = typeof(ID3D11Texture2D).GetTypeInfo().GUID;
            ((IDXGISwapChain1*)SwapChainHandle)->GetBuffer(0, &guid, (void**)&colorBuffer);

            DxInteropColorHandle = Wgl.DXRegisterObjectNV(
                Context.GlDeviceHandle,
                (nint)colorBuffer,
                (uint)GLColorRenderBufferHandle,
                (uint)RenderbufferTarget.Renderbuffer,
                WGL_NV_DX_interop.AccessReadWrite);

            colorBuffer->Release();
            _isRegistered = DxInteropColorHandle != IntPtr.Zero;
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, GLFrameBufferHandle);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, (uint)GLColorRenderBufferHandle);

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, GLDepthRenderBufferHandle);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, BufferWidth, BufferHeight);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, (uint)GLDepthRenderBufferHandle);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.StencilAttachment,
            RenderbufferTarget.Renderbuffer, (uint)GLDepthRenderBufferHandle);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
    }

    private void ReleaseGLResources()
    {
        if (_isRegistered)
        {
            Wgl.DXUnregisterObjectNV(Context.GlDeviceHandle, DxInteropColorHandle);
            _isRegistered = false;
        }

        DxInteropColorHandle = IntPtr.Zero;

        if (GLColorRenderBufferHandle != 0)
        {
            GL.DeleteRenderbuffer(GLColorRenderBufferHandle);
            GLColorRenderBufferHandle = 0;
        }

        if (GLDepthRenderBufferHandle != 0)
        {
            GL.DeleteRenderbuffer(GLDepthRenderBufferHandle);
            GLDepthRenderBufferHandle = 0;
        }

        if (GLFrameBufferHandle != 0)
        {
            GL.DeleteFramebuffer(GLFrameBufferHandle);
            GLFrameBufferHandle = 0;
        }
    }
}
