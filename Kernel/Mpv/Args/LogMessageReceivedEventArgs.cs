// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;

namespace Mpv.Core.Args;

/// <summary>
/// 日志消息接收事件参数.
/// </summary>
public sealed class LogMessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LogMessageReceivedEventArgs"/> class.
    /// </summary>
    public LogMessageReceivedEventArgs(string prefix, string message, MpvLogLevel level)
    {
        Prefix = prefix;
        Message = message;
        Level = level;
    }

    /// <summary>
    /// 前缀.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// 消息.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 日志级别.
    /// </summary>
    public MpvLogLevel Level { get; }
}
