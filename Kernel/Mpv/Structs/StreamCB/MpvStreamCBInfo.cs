// Copyright (c) Bili Copilot. All rights reserved.

using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.StreamCB;

/// <summary>See mpv_stream_cb_open_ro_fn callback.</summary>
[StructLayout(LayoutKind.Sequential, Size = 48)]
public struct MpvStreamCBInfo
{
    /// <summary>
    /// <para>Opaque user-provided value, which will be passed to the other callbacks.</para>
    /// <para>The close callback will be called to release the cookie. It is not</para>
    /// <para>interpreted by mpv. It doesn't even need to be a valid pointer.</para>
    /// </summary>
    /// <remarks>The user sets this in the mpv_stream_cb_open_ro_fn callback.</remarks>
    public IntPtr Cookie;

    /// <summary>
    /// <para>Callbacks set by the user in the mpv_stream_cb_open_ro_fn callback. Some</para>
    /// <para>of them are optional, and can be left unset.</para>
    /// </summary>
    /// <remarks>The following callbacks are mandatory: read_fn, close_fn</remarks>
    public IntPtr ReadFn;

    public IntPtr SeekFn;

    public IntPtr SizeFn;

    public IntPtr CloseFn;

    public IntPtr CancelFn;
}