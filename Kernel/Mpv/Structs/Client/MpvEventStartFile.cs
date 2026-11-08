// Copyright (c) Bili Copilot. All rights reserved.

using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Client;

/// <summary>Since API version 1.108.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public struct MpvEventStartFile
{
    /// <summary>Playlist entry ID of the file being loaded now.</summary>
    public long PlaylistEntryId;
}