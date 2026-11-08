// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Enums.Client;

/// <summary>
/// <para>List of error codes than can be returned by API functions. 0 and positive.</para>
/// <para>return values always mean success, negative values are always errors.</para>
/// </summary>
public enum MpvError
{
    /// <summary>
    /// <para>No error happened (used to signal successful operation).</para>
    /// <para>Keep in mind that many API functions returning error codes can also</para>
    /// <para>return positive values, which also indicate success. API users can</para>
    /// <para>hardcode the fact that &quot;&gt;= 0&quot; means success.</para>
    /// </summary>
    Success = 0,

    /// <summary>
    /// <para>The event ringbuffer is full. This means the client is choked, and can't</para>
    /// <para>receive any events. This can happen when too many asynchronous requests</para>
    /// <para>have been made, but not answered. Probably never happens in practice,</para>
    /// <para>unless the mpv core is frozen for some reason, and the client keeps</para>
    /// <para>making asynchronous requests. (Bugs in the client API implementation</para>
    /// <para>could also trigger this, e.g. if events become &quot;lost&quot;.)</para>
    /// </summary>
    EventQueueFull = -1,

    /// <summary>
    /// Memory allocation failed.
    /// </summary>
    Nomem = -2,

    /// <summary>
    /// <para>The mpv core wasn't configured and initialized yet. See the notes in</para>
    /// <para>mpv_create().</para>
    /// </summary>
    Uninitialized = -3,

    /// <summary>
    /// <para>Generic catch-all error if a parameter is set to an invalid or</para>
    /// <para>unsupported value. This is used if there is no better error code.</para>
    /// </summary>
    InvalidParameter = -4,

    /// <summary>
    /// Trying to set an option that doesn't exist.
    /// </summary>
    OptionNotFound = -5,

    /// <summary>
    /// Trying to set an option using an unsupported Mpv_FORMAT.
    /// </summary>
    OptionFormat = -6,

    /// <summary>
    /// <para>Setting the option failed. Typically this happens if the provided option</para>
    /// <para>value could not be parsed.</para>
    /// </summary>
    OptionError = -7,

    /// <summary>
    /// The accessed property doesn't exist.
    /// </summary>
    PropertyNotFound = -8,

    /// <summary>
    /// Trying to set or get a property using an unsupported Mpv_FORMAT.
    /// </summary>
    PropertyFormat = -9,

    /// <summary>
    /// <para>The property exists, but is not available. This usually happens when the</para>
    /// <para>associated subsystem is not active, e.g. querying audio parameters while</para>
    /// <para>audio is disabled.</para>
    /// </summary>
    PropertyUnavailable = -10,

    /// <summary>
    /// Error setting or getting a property.
    /// </summary>
    PropertyError = -11,

    /// <summary>
    /// General error when running a command with mpv_command and similar.
    /// </summary>
    Command = -12,

    /// <summary>
    /// Generic error on loading (usually used with mpv_event_end_file.error).
    /// </summary>
    LoadingFailed = -13,

    /// <summary>
    /// Initializing the audio output failed.
    /// </summary>
    AoInitFailed = -14,

    /// <summary>
    /// Initializing the video output failed.
    /// </summary>
    VoInitFailed = -15,

    /// <summary>
    /// <para>There was no audio or video data to play. This also happens if the</para>
    /// <para>file was recognized, but did not contain any audio or video streams,</para>
    /// <para>or no streams were selected.</para>
    /// </summary>
    NothingToPlay = -16,

    /// <summary>
    /// <para>When trying to load the file, the file format could not be determined,</para>
    /// <para>or the file was too broken to open it.</para>
    /// </summary>
    UnknownFormat = -17,

    /// <summary>
    /// <para>Generic error for signaling that certain system requirements are not</para>
    /// <para>fulfilled.</para>
    /// </summary>
    Unsupported = -18,

    /// <summary>
    /// The API function which was called is a stub only.
    /// </summary>
    NotImplemented = -19,

    /// <summary>
    /// Unspecified error.
    /// </summary>
    Generic = -20,
}
