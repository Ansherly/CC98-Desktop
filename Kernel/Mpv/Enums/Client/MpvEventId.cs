// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Enums.Client;

/// <summary>
/// Events sent to the client.
/// </summary>
public enum MpvEventId
{
    /// <summary>Nothing happened. Happens on timeouts or sporadic wakeups.</summary>
    None = 0,

    /// <summary>
    /// <para>Happens when the player quits. The player enters a state where it tries</para>
    /// <para>to disconnect all clients. Most requests to the player will fail, and</para>
    /// <para>the client should react to this and quit with mpv_destroy() as soon as</para>
    /// <para>possible.</para>
    /// </summary>
    Shutdown = 1,

    /// <summary>See mpv_request_log_messages().</summary>
    LogMessage = 2,

    /// <summary>
    /// <para>Reply to a mpv_get_property_async() request.</para>
    /// <para>See also mpv_event and mpv_event_property.</para>
    /// </summary>
    GetPropertyReply = 3,

    /// <summary>
    /// <para>Reply to a mpv_set_property_async() request.</para>
    /// <para>(Unlike Mpv_EVENT_GET_PROPERTY, mpv_event_property is not used.)</para>
    /// </summary>
    SetPropertyReply = 4,

    /// <summary>
    /// <para>Reply to a mpv_command_async() or mpv_command_node_async() request.</para>
    /// <para>See also mpv_event and mpv_event_command.</para>
    /// </summary>
    CommandReply = 5,

    /// <summary>
    /// <para>Notification before playback start of a file (before the file is loaded).</para>
    /// <para>See also mpv_event and mpv_event_start_file.</para>
    /// </summary>
    StartFile = 6,

    /// <summary>
    /// <para>Notification after playback end (after the file was unloaded).</para>
    /// <para>See also mpv_event and mpv_event_end_file.</para>
    /// </summary>
    EndFile = 7,

    /// <summary>
    /// <para>Notification when the file has been loaded (headers were read etc.), and</para>
    /// <para>decoding starts.</para>
    /// </summary>
    FileLoaded = 8,

    /// <summary>
    /// <para>Idle mode was entered. In this mode, no file is played, and the playback</para>
    /// <para>core waits for new commands. (The command line player normally quits</para>
    /// <para>instead of entering idle mode, unless --idle was specified. If mpv</para>
    /// <para>was started with mpv_create(), idle mode is enabled by default.)</para>
    /// </summary>
    /// <remarks>
    /// <para>This is equivalent to using mpv_observe_property() on the</para>
    /// <para>&quot;idle-active&quot; property. The event is redundant, and might be</para>
    /// <para>removed in the far future. As a further warning, this event</para>
    /// <para>is not necessarily sent at the right point anymore (at the</para>
    /// <para>start of the program), while the property behaves correctly.</para>
    /// </remarks>
    Idle = 11,

    /// <summary>
    /// <para>Sent every time after a video frame is displayed. Note that currently,</para>
    /// <para>this will be sent in lower frequency if there is no video, or playback</para>
    /// <para>is paused - but that will be removed in the future, and it will be</para>
    /// <para>restricted to video frames only.</para>
    /// </summary>
    /// <remarks>
    /// <para>Use mpv_observe_property() with relevant properties instead</para>
    /// <para>(such as &quot;playback-time&quot;).</para>
    /// </remarks>
    Tick = 14,

    /// <summary>
    /// <para>Triggered by the script-message input command. The command uses the</para>
    /// <para>first argument of the command as client name (see mpv_client_name()) to</para>
    /// <para>dispatch the message, and passes along all arguments starting from the</para>
    /// <para>second argument as strings.</para>
    /// <para>See also mpv_event and mpv_event_client_message.</para>
    /// </summary>
    ClientMessage = 16,

    /// <summary>
    /// <para>Happens after video changed in some way. This can happen on resolution</para>
    /// <para>changes, pixel format changes, or video filter changes. The event is</para>
    /// <para>sent after the video filters and the VO are reconfigured. Applications</para>
    /// <para>embedding a mpv window should listen to this event in order to resize</para>
    /// <para>the window if needed.</para>
    /// <para>Note that this event can happen sporadically, and you should check</para>
    /// <para>yourself whether the video parameters really changed before doing</para>
    /// <para>something expensive.</para>
    /// </summary>
    VideoReconfig = 17,

    /// <summary>
    /// <para>Similar to Mpv_EVENT_VIDEO_RECONFIG. This is relatively uninteresting,</para>
    /// <para>because there is no such thing as audio output embedding.</para>
    /// </summary>
    AudioReconfig = 18,

    /// <summary>
    /// <para>Happens when a seek was initiated. Playback stops. Usually it will</para>
    /// <para>resume with Mpv_EVENT_PLAYBACK_RESTART as soon as the seek is finished.</para>
    /// </summary>
    Seek = 20,

    /// <summary>
    /// <para>There was a discontinuity of some sort (like a seek), and playback</para>
    /// <para>was reinitialized. Usually happens on start of playback and after</para>
    /// <para>seeking. The main purpose is allowing the client to detect when a seek</para>
    /// <para>request is finished.</para>
    /// </summary>
    PlaybackRestart = 21,

    /// <summary>
    /// <para>Event sent due to mpv_observe_property().</para>
    /// <para>See also mpv_event and mpv_event_property.</para>
    /// </summary>
    PropertyChange = 22,

    /// <summary>
    /// <para>Happens if the internal per-mpv_handle ringbuffer overflows, and at</para>
    /// <para>least 1 event had to be dropped. This can happen if the client doesn't</para>
    /// <para>read the event queue quickly enough with mpv_wait_event(), or if the</para>
    /// <para>client makes a very large number of asynchronous calls at once.</para>
    /// </summary>
    /// <remarks>
    /// <para>Event delivery will continue normally once this event was returned</para>
    /// <para>(this forces the client to empty the queue completely).</para>
    /// </remarks>
    QueueOverflow = 24,

    /// <summary>
    /// <para>Triggered if a hook handler was registered with mpv_hook_add(), and the</para>
    /// <para>hook is invoked. If you receive this, you must handle it, and continue</para>
    /// <para>the hook with mpv_hook_continue().</para>
    /// <para>See also mpv_event and mpv_event_hook.</para>
    /// </summary>
    Hook = 25,
}
