// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Enums.Client;

/// <summary>
/// <para>Numeric log levels. The lower the number, the more important the message is.</para>
/// <para>Mpv_LOG_LEVEL_NONE is never used when receiving messages. The string in.</para>
/// <para>the comment after the value is the name of the log level as used for the.</para>
/// <para>mpv_request_log_messages() function.</para>
/// <para>Unused numeric values are unused, but reserved for future use.</para>
/// </summary>
public enum MpvLogLevel
{
    /// <summary>
    /// "no"    - disable absolutely all messages.
    /// </summary>
    None = 0,

    /// <summary>
    /// "fatal" - critical/aborting errors.
    /// </summary>
    Fatal = 10,

    /// <summary>
    /// "error" - simple errors.
    /// </summary>
    Error = 20,

    /// <summary>
    /// "warn"  - possible problems.
    /// </summary>
    Warn = 30,

    /// <summary>
    /// "info"  - informational message.
    /// </summary>
    Info = 40,

    /// <summary>
    /// "v"     - noisy informational message.
    /// </summary>
    V = 50,

    /// <summary>
    /// "debug" - very noisy technical information.
    /// </summary>
    Debug = 60,

    /// <summary>
    /// "trace" - extremely noisy.
    /// </summary>
    Trace = 70,
}
