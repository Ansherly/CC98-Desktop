// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Client;

[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct MpvEventLogMessage
{
    internal IntPtr _prefixPtr;

    /// <summary>
    /// <para>The module prefix, identifies the sender of the message. As a special</para>
    /// <para>case, if the message buffer overflows, this will be set to the string</para>
    /// <para>&quot;overflow&quot; (which doesn't appear as prefix otherwise), and the text</para>
    /// <para>field will contain an informative message.</para>
    /// </summary>
    public string Prefix => Marshal.PtrToStringUTF8(_prefixPtr) ?? string.Empty;

    internal IntPtr _levelPtr;

    /// <summary>
    /// <para>The log level as string. See mpv_request_log_messages() for possible</para>
    /// <para>values. The level &quot;no&quot; is never used here.</para>
    /// </summary>
    public string Level => Marshal.PtrToStringUTF8(_levelPtr) ?? string.Empty;

    internal IntPtr _textPtr;

    /// <summary>
    /// <para>The log message. It consists of 1 line of text, and is terminated with</para>
    /// <para>a newline character. (Before API version 1.6, it could contain multiple</para>
    /// <para>or partial lines.)</para>
    /// </summary>
    public string Text => Marshal.PtrToStringUTF8(_textPtr) ?? string.Empty;

    /// <summary>
    /// <para>The same contents as the level field, but as a numeric ID.</para>
    /// <para>Since API version 1.6.</para>
    /// </summary>
    public MpvLogLevel LogLevel;
}