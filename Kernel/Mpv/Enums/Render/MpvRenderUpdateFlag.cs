// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Enums.Render;

/// <summary>
/// <para>Flags returned by mpv_render_context_update(). Each value represents a bit.</para>
/// <para>in the function's return value.</para>
/// </summary>
public enum MpvRenderUpdateFlag
{
    /// <summary>
    /// <para>A new video frame must be rendered. mpv_render_context_render() must be</para>
    /// <para>called.</para>
    /// </summary>
    Frame = 1,
}
