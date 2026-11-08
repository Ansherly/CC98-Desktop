// Copyright (c) Bili Copilot. All rights reserved.

using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.RenderGL;

/// <summary>For initializing the mpv OpenGL state via Mpv_RENDER_PARAM_OPENGL_INIT_PARAMS.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct MpvOpenGLInitParams
{
    /// <summary>
    /// <para>This retrieves OpenGL function pointers, and will use them in subsequent</para>
    /// <para>operation.</para>
    /// <para>Usually, you can simply call the GL context APIs from this callback (e.g.</para>
    /// <para>glXGetProcAddressARB or wglGetProcAddress), but some APIs do not always</para>
    /// <para>return pointers for all standard functions (even if present); in this</para>
    /// <para>case you have to compensate by looking up these functions yourself when</para>
    /// <para>libmpv wants to resolve them through this callback.</para>
    /// <para>libmpv will not normally attempt to resolve GL functions on its own, nor</para>
    /// <para>does it link to GL libraries directly.</para>
    /// </summary>
    public GetProcAddr GetProcAddrFn;

    /// <summary>Value passed as ctx parameter to get_proc_address().</summary>
    public IntPtr GetProcAddressCtx;

    public delegate nint GetProcAddr(IntPtr ctx, string name);
}