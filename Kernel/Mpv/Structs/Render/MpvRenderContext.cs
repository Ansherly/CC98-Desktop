// Copyright (c) Bili Copilot. All rights reserved.

using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Render;

[StructLayout(LayoutKind.Sequential)]
public struct MpvRenderContextHandle
{
    public IntPtr Handle;
}