// Copyright (c) Bili Copilot. All rights reserved.

using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Client;

/// <summary>(see mpv_node).</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct MpvByteArray
{
    /// <summary>
    /// <para>Pointer to the data. In what format the data is stored is up to whatever</para>
    /// <para>uses Mpv_FORMAT_BYTE_ARRAY.</para>
    /// </summary>
    public IntPtr Data;

    /// <summary>
    /// Size of the data pointed to by ptr.
    /// </summary>
    public ulong Size;
}
