using System;

namespace CC98.Services;

/// <summary>
/// 一次性的「导航参数覆盖」通道,用于处理"返回时目标页应采用新参数"的场景。
///
/// 场景举例:SketchPage 发帖/编辑成功后返回 TopicPage,希望它跳到刚发布的楼层。
/// 但返回导航重放的是 Frame 后退栈里的<b>原始</b>参数(只有 TopicId、没有楼层),
/// 因此发起方需要把新参数另行交给目标页 —— 这就是本通道的用途。
///
/// 用法:
/// <code>
/// // 发起方(返回前登记,目标页显式声明,无需在 MainWindow 维护规则表)
/// NavigationArgs.Set&lt;TopicPage&gt;(new TopicNavigationInfo { ... });
/// Frame.GoBack();
///
/// // 目标页(取用即清除,一次性语义)
/// var args = NavigationArgs.TryConsume&lt;TopicPage, TopicNavigationInfo&gt;(out var overridden)
///     ? overridden
///     : e.TryGetParameter&lt;TopicNavigationInfo&gt;();
/// </code>
///
/// 相比原先的 GlobalService.ShouldReplaceNavigationArgs + NavigationAnchor:
/// <list type="number">
/// <item>载荷自带目标页类型,不可能串页,也不必在 MainWindow 手工维护 (from, to) 规则表;</item>
/// <item>取用时做类型校验,类型不符会返回 false(旧实现用 is/as 运行时猜测并静默回落到默认值);</item>
/// <item>取用即清除,不会残留陈旧载荷。</item>
/// </list>
///
/// 注意:"当前是否处于返回导航"由页面自行判断 <c>e.NavigationMode == NavigationMode.Back</c>,
/// 不需要(也不应该)再引入全局开关。
/// </summary>
public static class NavigationArgs
{
    /// <summary>登记的载荷及其目标页类型。</summary>
    private sealed record Entry(Type Target, object Payload);

    /// <summary>当前待取用的载荷。同一时刻只允许存在一个,因为导航是串行的。</summary>
    private static Entry? _pending;

    /// <summary>
    /// 登记一个将由 <typeparamref name="TPage"/> 页消费的导航参数。
    /// 应在触发返回导航之前调用。
    /// </summary>
    /// <typeparam name="TPage">目标页面类型。</typeparam>
    /// <param name="payload">交给目标页的参数对象。</param>
    public static void Set<TPage>(object payload) => _pending = new Entry(typeof(TPage), payload);

    /// <summary>
    /// 尝试取出登记给 <typeparamref name="TPage"/> 的参数,<b>取出即清除</b>。
    /// 若当前登记的载荷不是给本页的、或类型与 <typeparamref name="TArgs"/> 不符,
    /// 则返回 <see langword="false"/> 且<b>不清除</b>,把载荷留给真正的目标页。
    /// </summary>
    /// <typeparam name="TPage">调用方页面类型。</typeparam>
    /// <typeparam name="TArgs">期望的参数类型。</typeparam>
    /// <param name="args">取到的参数;未取到时为 <see langword="default"/>。</param>
    /// <returns>是否成功取到属于本页的参数。</returns>
    public static bool TryConsume<TPage, TArgs>(out TArgs args)
    {
        if (_pending is { } entry && entry.Target == typeof(TPage) && entry.Payload is TArgs typed)
        {
            _pending = null;
            args = typed;
            return true;
        }

        args = default!;
        return false;
    }
}
