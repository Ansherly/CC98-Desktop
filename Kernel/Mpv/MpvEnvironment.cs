using System;
using System.IO;
using System.Threading;
using Mpv.Core;

namespace CC98.Kernel.Mpv;

/// <summary>
///     libmpv 的装载入口:把原生库路径指给 <see cref="Resolver" />,之后 mpv 的 P/Invoke 才能落地。
/// </summary>
/// <remarks>
///     <para>
///         打包应用的当前目录不是安装目录,<c>NativeLibrary.Load("libmpv-2.dll")</c> 的默认搜索
///         找不到随包部署的文件,所以这里显式给绝对路径。
///     </para>
///     <para>libmpv-2.dll 不进版本库,由 <c>tools\fetch-libmpv.ps1</c> 下载到 <c>native\</c>。</para>
/// </remarks>
public static class MpvEnvironment
{
    /// <summary>随包部署的 libmpv 文件名。</summary>
    public const string LibraryFileName = "libmpv-2.dll";

    private static int _state; // 0=未初始化 1=成功 2=失败
    private static string? _libraryPath;

    /// <summary>libmpv 的实际路径;没找到时为 <c>null</c>。</summary>
    public static string? LibraryPath => _libraryPath;

    /// <summary>libmpv 是否可用(找不到文件时为 <c>false</c>,调用方应降级处理)。</summary>
    public static bool IsAvailable => _libraryPath is not null;

    /// <summary>
    ///     初始化一次:定位 libmpv、注册导入解析器。可重复调用。
    /// </summary>
    /// <returns>libmpv 是否可用。</returns>
    public static bool Initialize()
    {
        // 0=未开始 1=成功 2=失败 3=正在初始化;多线程同时进来时只让一个线程真正干活
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if (state == 1) return true;
            if (state == 2) return false;

            if (state == 3)
            {
                Thread.Sleep(10);
                continue;
            }

            if (Interlocked.CompareExchange(ref _state, 3, 0) == 0) break;
        }

        try
        {
            var path = ResolveLibraryPath();
            if (path is null)
            {
                Volatile.Write(ref _state, 2);
                return false;
            }

            _libraryPath = path;
            Resolver.SetCustomMpvPath(path);
            Resolver.SetResolver();
            Volatile.Write(ref _state, 1);
            return true;
        }
        catch (Exception)
        {
            Volatile.Write(ref _state, 2);
            return false;
        }
    }

    private static string? ResolveLibraryPath()
    {
        // 应用根目录:打包运行时是包安装目录,非打包运行时是 exe 所在目录
        foreach (var dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            if (string.IsNullOrEmpty(dir)) continue;

            var candidate = Path.Combine(dir, LibraryFileName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
