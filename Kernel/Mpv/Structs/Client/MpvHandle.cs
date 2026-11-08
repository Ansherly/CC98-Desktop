// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Structs.Client;

public struct MpvHandle
{
    /// <summary>
    /// Pointer to this struct. This is used as a unique identifier.
    /// </summary>
    public IntPtr Handle;

    public static MpvHandle None => new MpvHandle { Handle = IntPtr.Zero };

    public static implicit operator bool(MpvHandle handle) => handle.Handle != IntPtr.Zero;
}