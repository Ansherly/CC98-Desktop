// Copyright (c) Bili Copilot. All rights reserved.

using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.RenderGL;

/// <summary>For Mpv_RENDER_PARAM_DRM_DISPLAY_V2.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct MpvOpenGLDRMParamsV2
{
    /// <summary>DRM fd (int). Set to -1 if invalid.</summary>
    public int Fd;

    /// <summary>Currently used crtc id</summary>
    public int CrtcId;

    /// <summary>Currently used connector id</summary>
    public int ConnectorId;

    /// <summary>
    /// <para>Pointer to a drmModeAtomicReq pointer that is being used for the renderloop.</para>
    /// <para>This pointer should hold a pointer to the atomic request pointer</para>
    /// <para>The atomic request pointer is usually changed at every renderloop.</para>
    /// </summary>
    public IntPtr AtomicRequestPtr;

    /// <summary>
    /// <para>DRM render node. Used for VAAPI interop.</para>
    /// <para>Set to -1 if invalid.</para>
    /// </summary>
    public int RenderFd;
}