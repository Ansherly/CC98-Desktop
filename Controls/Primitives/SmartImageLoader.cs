using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CC98.Controls.Picture;
using CC98.Services.Helpers;
using Microsoft.UI.Xaml.Media;

namespace CC98.Controls.Primitives;

/// <summary>
///     应用内通用的图片加载器:本地 / 网络统一入口,带弱引用内存缓存。
/// </summary>
/// <remarks>
///     返回类型是 <see cref="ImageSource" />,可同时承载位图(<c>BitmapImage</c>)与矢量图
///     (<c>SvgImageSource</c>)—— 具体类型由 <see cref="ImageHelper" /> 按内容嗅探决定,本类不关心。
///     缓存因此也按 <see cref="ImageSource" /> 存放。
/// </remarks>
public class SmartImageLoader : IImageLoader
{
    // 单例实例
    private static SmartImageLoader? _instance;
    private static readonly Lock Lock = new();


    private static readonly ConcurrentDictionary<bool, SmartImageLoader> Instances = new();

    private static readonly ConcurrentDictionary<string, WeakReference<ImageSource?>> Cache = new();

    // 缓存访问计数:周期性清理已被 GC 的死条目,防止静态缓存无界增长
    private static int _cacheAccessCount;
    private const int PurgeInterval = 64;

    public SmartImageLoader()
    {
    }

    public SmartImageLoader(bool lowRes)
    {
        LowRes = lowRes;
    }

    // 公共属性
    public bool LowRes { get; set; }

    public static SmartImageLoader Default
    {
        get
        {
            if (_instance == null)
                lock (Lock)
                {
                    _instance ??= new();
                }

            return _instance;
        }
    }

    public async Task<ImageSource?> LoadImage(string src)
    {
        if (string.IsNullOrEmpty(src))
            return null;

        // 每 N 次访问清理一次死条目(目标已被 GC 的弱引用),避免字典条目无限堆积
        if (Interlocked.Increment(ref _cacheAccessCount) % PurgeInterval == 0)
        {
            PurgeDeadEntries();
        }

        var cacheKey = (LowRes ? "lr:" : "hr:") + src;

        if (Cache.TryGetValue(cacheKey, out var weak) && weak.TryGetTarget(out var cached) && cached != null)
            return cached;

        ImageSource? result = null;

        try
        {
            // ImageHelper 内部按内容嗅探 SVG 与位图,这里无需区分
            if (UrlEx.IsLocalPath(src)) result = await ImageHelper.LoadLocalImage(src, LowRes);
            else if (UrlEx.IsWebUrl(src)) result = await ImageHelper.LoadWebImageAsync(src, LowRes);
        }
        catch
        {
            result = null;
        }

        // 缓存
        if (result != null)
        {
            Cache.AddOrUpdate(
                cacheKey, 
                new WeakReference<ImageSource?>(result),
               (k, old) => new(result));
        }

        return result;
    }

    /// <summary>
    /// 移除目标已被 GC 回收的死缓存条目。
    /// </summary>
    private static void PurgeDeadEntries()
    {
        foreach (var kv in Cache)
        {
            if (!kv.Value.TryGetTarget(out var target) || target == null)
            {
                Cache.TryRemove(kv.Key, out _);
            }
        }
    }

    public static SmartImageLoader GetInstance(bool lowRes)
    {
        return Instances.GetOrAdd(lowRes, l => new(l));
    }

    public static void ClearCache()
    {
        Cache.Clear();
    }
}
