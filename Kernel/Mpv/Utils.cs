// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using Mpv.Core.Interop;

namespace Mpv.Core;

internal static class Utils
{
    public static string ToLogLevel(this MpvLogLevel level)
    {
        return level switch
        {
            MpvLogLevel.None => "no",
            MpvLogLevel.Fatal => "fatal",
            MpvLogLevel.Error => "error",
            MpvLogLevel.Warn => "warn",
            MpvLogLevel.Info => "info",
            MpvLogLevel.V => "v",
            MpvLogLevel.Debug => "debug",
            MpvLogLevel.Trace => "trace",
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
        };
    }

    public static MpvLogLevel ToLogLevel(this string level)
    {
        return level switch
        {
            "no" => MpvLogLevel.None,
            "fatal" => MpvLogLevel.Fatal,
            "error" => MpvLogLevel.Error,
            "warn" => MpvLogLevel.Warn,
            "info" => MpvLogLevel.Info,
            "v" => MpvLogLevel.V,
            "debug" => MpvLogLevel.Debug,
            "trace" => MpvLogLevel.Trace,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
        };
    }

    internal static MpvException CreateError(MpvError error)
    {
        var msg = MpvStatics.ParseError(error);
        return new MpvException(msg, error);
    }
}
