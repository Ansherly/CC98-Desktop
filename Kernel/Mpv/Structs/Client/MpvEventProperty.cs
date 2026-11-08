// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Client;

[StructLayout(LayoutKind.Sequential)]
public struct MpvEventProperty
{
    public string Name;

    public MpvFormat Format;

    public IntPtr DataPtr; //Expand to all formats
}