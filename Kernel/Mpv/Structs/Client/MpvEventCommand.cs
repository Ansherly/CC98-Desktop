// Copyright (c) Bili Copilot. All rights reserved.

using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Client;

[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct MpvEventCommand
{
    internal IntPtr _resultPtr;

    /// <summary>
    /// <para>Result data of the command. Note that success/failure is signaled</para>
    /// <para>separately via mpv_event.error. This field is only for result data</para>
    /// <para>in case of success. Most commands leave it at Mpv_FORMAT_NONE. Set</para>
    /// <para>to Mpv_FORMAT_NONE on failure.</para>
    /// </summary>
    public MpvNode Result => Marshal.PtrToStructure<MpvNode>(_resultPtr);
}