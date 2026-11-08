// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Render;
using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Render;

/// <summary>
/// <para>Information about the next video frame that will be rendered. Can be</para>
/// <para>retrieved with Mpv_RENDER_PARAM_NEXT_FRAME_INFO.</para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct MpvRenderFrameInfo
{
    /// <summary>
    /// <para>A bitset of mpv_render_frame_info_flag values (i.e. multiple flags are</para>
    /// <para>combined with bitwise or).</para>
    /// </summary>
    public MpvRenderFrameInfoFlag Flags;

    /// <summary>
    /// <para>Absolute time at which the frame is supposed to be displayed. This is in</para>
    /// <para>the same unit and base as the time returned by mpv_get_time_us(). For</para>
    /// <para>frames that are redrawn, or if vsync locked video timing is used (see</para>
    /// <para>&quot;video-sync&quot; option), then this can be 0. The &quot;video-timing-offset&quot;</para>
    /// <para>option determines how much &quot;headroom&quot; the render thread gets (but a high</para>
    /// <para>enough frame rate can reduce it anyway). mpv_render_context_render() will</para>
    /// <para>normally block until the time is elapsed, unless you pass it</para>
    /// <para>Mpv_RENDER_PARAM_BLOCK_FOR_TARGET_TIME = 0.</para>
    /// </summary>
    public long TargetTime;
}