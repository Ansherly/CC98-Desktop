// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Structs.Player;

/// <summary>
/// The version of the service on which the MPV depends.
/// </summary>
public class LibMpvDependencies
{
    /// <summary>
    /// Libmpv build time.
    /// </summary>
    public DateTimeOffset BuildTime { get; set; }

    /// <summary>
    /// libplacebo version.
    /// </summary>
    public string? LibplaceboVersion { get; set; }

    /// <summary>
    /// FFmpeg version.
    /// </summary>
    public string? FFmpegVersion { get; set; }

    /// <summary>
    /// Libavutil version.
    /// </summary>
    public string? LibavutilVersion { get; set; }

    /// <summary>
    /// Libavcodec version.
    /// </summary>
    public string? LibavcodecVersion { get; set; }

    /// <summary>
    /// Libavformat version.
    /// </summary>
    public string? LibavformatVersion { get; set; }

    /// <summary>
    /// Libavdevice version.
    /// </summary>
    public string? LibavfilterVersion { get; set; }

    /// <summary>
    /// Libavdevice version.
    /// </summary>
    public string? LibswscaleVersion { get; set; }

    /// <summary>
    /// Libavdevice version.
    /// </summary>
    public string? LibswresampleVersion { get; set; }
}
