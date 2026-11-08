// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Args;

/// <summary>
/// 初始化参数.
/// </summary>
public sealed class InitializeArgument
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InitializeArgument"/> class.
    /// </summary>
    public InitializeArgument()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InitializeArgument"/> class.
    /// </summary>
    public InitializeArgument(
        string configFile,
        bool autoPlay = true,
        Func<string, IntPtr>? func = default)
    {
        ConfigFile = configFile;
        AutoPlay = autoPlay;
        if (func != null)
        {
            OpenGLGetProcAddress = func;
        }
    }

    /// <summary>
    /// 配置文件路径.
    /// </summary>
    public string? ConfigFile { get; }

    /// <summary>
    /// 是否自动播放.
    /// </summary>
    public bool AutoPlay { get; set; }

    /// <summary>
    /// 获取OpenGL函数指针.
    /// </summary>
    public Func<string, IntPtr>? OpenGLGetProcAddress { get; }
}
