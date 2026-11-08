// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Client;

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct MpvEventEndFile
{
    /// <summary>Corresponds to the values in enum mpv_end_file_reason.</summary>
    /// <remarks>Unknown values should be treated as unknown.</remarks>
    public MpvEndFileReason Reason;

    /// <summary>
    /// <para>If reason==Mpv_END_FILE_REASON_ERROR, this contains a mpv error code</para>
    /// <para>(one of Mpv_ERROR_...) giving an approximate reason why playback</para>
    /// <para>failed. In other cases, this field is 0 (no error).</para>
    /// <para>Since API version 1.9.</para>
    /// </summary>
    public MpvError Error;

    /// <summary>
    /// <para>Playlist entry ID of the file that was being played or attempted to be</para>
    /// <para>played. This has the same value as the playlist_entry_id field in the</para>
    /// <para>corresponding mpv_event_start_file event.</para>
    /// <para>Since API version 1.108.</para>
    /// </summary>
    public long PlaylistEntryId;

    /// <summary>
    /// <para>If loading ended, because the playlist entry to be played was for example</para>
    /// <para>a playlist, and the current playlist entry is replaced with a number of</para>
    /// <para>other entries. This may happen at least with Mpv_END_FILE_REASON_REDIRECT</para>
    /// <para>(other event types may use this for similar but different purposes in the</para>
    /// <para>future). In this case, playlist_insert_id will be set to the playlist</para>
    /// <para>entry ID of the first inserted entry, and playlist_insert_num_entries to</para>
    /// <para>the total number of inserted playlist entries. Note this in this specific</para>
    /// <para>case, the ID of the last inserted entry is playlist_insert_id+num-1.</para>
    /// <para>Beware that depending on circumstances, you may observe the new playlist</para>
    /// <para>entries before seeing the event (e.g. reading the &quot;playlist&quot; property or</para>
    /// <para>getting a property change notification before receiving the event).</para>
    /// <para>Since API version 1.108.</para>
    /// </summary>
    public long PlaylistInsertId;

    /// <summary>
    /// <para>See playlist_insert_id. Only non-0 if playlist_insert_id is valid. Never</para>
    /// <para>negative.</para>
    /// <para>Since API version 1.108.</para>
    /// </summary>
    public int PlaylistInsertNumEntries;
}