// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Enums.Client;

/// <summary>
/// Since API version 1.9.
/// </summary>
public enum MpvEndFileReason
{
    /// <summary>
    /// <para>The end of file was reached. Sometimes this may also happen on</para>
    /// <para>incomplete or corrupted files, or if the network connection was</para>
    /// <para>interrupted when playing a remote file. It also happens if the</para>
    /// <para>playback range was restricted with --end or --frames or similar.</para>
    /// </summary>
    Eof = 0,

    /// <summary>Playback was stopped by an external action (e.g. playlist controls).</summary>
    Stop = 2,

    /// <summary>Playback was stopped by the quit command or player shutdown.</summary>
    Quit = 3,

    /// <summary>
    /// <para>Some kind of error happened that lead to playback abort. Does not</para>
    /// <para>necessarily happen on incomplete or broken files (in these cases, both</para>
    /// <para>Mpv_END_FILE_REASON_ERROR or Mpv_END_FILE_REASON_EOF are possible).</para>
    /// </summary>
    /// <remarks>mpv_event_end_file.error will be set.</remarks>
    Error = 4,

    /// <summary>
    /// <para>The file was a playlist or similar. When the playlist is read, its</para>
    /// <para>entries will be appended to the playlist after the entry of the current</para>
    /// <para>file, the entry of the current file is removed, and a Mpv_EVENT_END_FILE</para>
    /// <para>event is sent with reason set to Mpv_END_FILE_REASON_REDIRECT. Then</para>
    /// <para>playback continues with the playlist contents.</para>
    /// <para>Since API version 1.18.</para>
    /// </summary>
    Redirect = 5,
}
