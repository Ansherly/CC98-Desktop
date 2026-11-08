// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Client;

[StructLayout(LayoutKind.Sequential)]
public partial struct MpvEvent
{
    /// <summary>
    /// <para>One of mpv_event. Keep in mind that later ABI compatible releases might</para>
    /// <para>add new event types. These should be ignored by the API user.</para>
    /// </summary>
    public MpvEventId EventId;

    /// <summary>
    /// <para>This is mainly used for events that are replies to (asynchronous)</para>
    /// <para>requests. It contains a status code, which is &gt;= 0 on success, or&lt;0</para>
    /// <para>on error (a mpv_error value). Usually, this will be set if an</para>
    /// <para>asynchronous request fails.</para>
    /// <para>Used for:</para>
    /// <para>Mpv_EVENT_GET_PROPERTY_REPLY</para>
    /// <para>Mpv_EVENT_SET_PROPERTY_REPLY</para>
    /// <para>Mpv_EVENT_COMMAND_REPLY</para>
    /// </summary>
    public MpvError Error;

    /// <summary>
    /// <para>If the event is in reply to a request (made with this API and this</para>
    /// <para>API handle), this is set to the reply_userdata parameter of the request</para>
    /// <para>call. Otherwise, this field is 0.</para>
    /// <para>Used for:</para>
    /// <para>Mpv_EVENT_GET_PROPERTY_REPLY</para>
    /// <para>Mpv_EVENT_SET_PROPERTY_REPLY</para>
    /// <para>Mpv_EVENT_COMMAND_REPLY</para>
    /// <para>Mpv_EVENT_PROPERTY_CHANGE</para>
    /// <para>Mpv_EVENT_HOOK</para>
    /// </summary>
    public ulong UserData;

    /// <summary>
    /// <para>The meaning and contents of the data member depend on the event_id:</para>
    /// <para>Mpv_EVENT_GET_PROPERTY_REPLY:     mpv_event_property*</para>
    /// <para>Mpv_EVENT_PROPERTY_CHANGE:        mpv_event_property*</para>
    /// <para>Mpv_EVENT_LOG_MESSAGE:            mpv_event_log_message*</para>
    /// <para>Mpv_EVENT_CLIENT_MESSAGE:         mpv_event_client_message*</para>
    /// <para>Mpv_EVENT_START_FILE:             mpv_event_start_file* (since v1.108)</para>
    /// <para>Mpv_EVENT_END_FILE:               mpv_event_end_file*</para>
    /// <para>Mpv_EVENT_HOOK:                   mpv_event_hook*</para>
    /// <para>Mpv_EVENT_COMMAND_REPLY*          mpv_event_command*</para>
    /// <para>other: NULL</para>
    /// </summary>
    /// <remarks>
    /// <para>Note: future enhancements might add new event structs for existing or new</para>
    /// <para>event types.</para>
    /// </remarks>
    public IntPtr DataPtr; // EventArgs

    /// <summary>
    /// <para>The meaning and contents of the data member depend on the event_id:</para>
    /// <para>Mpv_EVENT_GET_PROPERTY_REPLY:     mpv_event_property*</para>
    /// <para>Mpv_EVENT_PROPERTY_CHANGE:        mpv_event_property*</para>
    /// <para>Mpv_EVENT_LOG_MESSAGE:            mpv_event_log_message*</para>
    /// <para>Mpv_EVENT_CLIENT_MESSAGE:         mpv_event_client_message*</para>
    /// <para>Mpv_EVENT_START_FILE:             mpv_event_start_file* (since v1.108)</para>
    /// <para>Mpv_EVENT_END_FILE:               mpv_event_end_file*</para>
    /// <para>Mpv_EVENT_HOOK:                   mpv_event_hook*</para>
    /// <para>Mpv_EVENT_COMMAND_REPLY*          mpv_event_command*</para>
    /// <para>other: NULL</para>
    /// </summary>
    /// <remarks>
    /// <para>Note: future enhancements might add new event structs for existing or new</para>
    /// <para>event types.</para>
    /// </remarks>
    public T GetData<T>() where T : struct => Marshal.PtrToStructure<T>(DataPtr);
}