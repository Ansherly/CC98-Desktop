// Copyright (c) Bili Copilot. All rights reserved.

using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.RenderGL;

/// <summary>Deprecated. For Mpv_RENDER_PARAM_DRM_DISPLAY.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct MpvOpenGLDRMParams
{
    public int Fd;

    public int CrtcId;

    public int ConnectorId;

    public IntPtr AtomicRequestPtr; //WAT

    public int RenderFd;
}