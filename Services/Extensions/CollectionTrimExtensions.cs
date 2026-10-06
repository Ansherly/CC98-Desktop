using System.Collections.ObjectModel;

namespace CC98.Services.Extensions;

/// <summary>
///     ObservableCollection 修剪扩展。
/// </summary>
/// <remarks>
///     用于 <c>NavigationCacheMode="Required"</c> 的页面:其实例伴随应用整个生命周期,
///     "无限下拉"累积的集合若不加限制会常驻内存并持续增长。
/// </remarks>
public static class CollectionTrimExtensions
{
    /// <summary>
    ///     把集合条数裁剪到 <paramref name="maxCount" /> 以内,超出时从头部移除最早的条目。
    /// </summary>
    public static void TrimToCount<T>(this ObservableCollection<T> collection, int maxCount)
    {
        while (collection.Count > maxCount)
        {
            collection.RemoveAt(0);
        }
    }
}
