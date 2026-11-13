using System;
using System.Diagnostics;
using System.Text;

namespace CC98.Controls.Primitives;

/// <summary>
///     视频卡片(哔哩 / 内链 <c>[video]</c>)播放链路的调试输出。
/// </summary>
/// <remarks>
///     <para>
///         统一成 <c>[时:分:秒.毫秒] 标签 | 内容</c>,Debug 输出窗口里按标签一筛就是完整一条链路:
///         <c>Bilibili</c> 是哔哩卡片,<c>VideoCard</c> 是内链卡片,<c>VideoPlayer</c> 是播放器控件本身。
///     </para>
///     <para>
///         底层用 <see cref="Debug.WriteLine(string)" />,<see cref="Write" /> 自己也带
///         <c>Conditional("DEBUG")</c>:Release 构建里调用点连同实参拼接都会被编译器整条删掉,
///         既不占体积也没有运行时开销。
///     </para>
///     <para>
///         <b>不打印请求头的值</b>:内链视频带的是 <c>Authorization: Bearer &lt;token&gt;</c>,
///         只报名字就够了。
///     </para>
/// </remarks>
internal static class MediaDebugLog
{
    /// <summary>写一行调试日志。</summary>
    /// <param name="tag">来源标签,建议是宿主类型名。</param>
    /// <param name="message">内容。</param>
    /// <remarks>
    ///     打了 <see cref="ConditionalAttribute" />(<c>DEBUG</c>):Release 构建里调用点连同
    ///     <c>Url()</c> / <c>HeaderNames()</c> 这些实参拼接都会被编译器整条删掉。
    /// </remarks>
    [Conditional("DEBUG")]
    public static void Write(string tag, string message)
        => Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {tag} | {message}");

    /// <summary>
    ///     把地址裁成 <c>主机+路径</c>;查询串(B 站直链常有几百字符)只报长度。
    /// </summary>
    public static string Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(空)";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return Clip(url);

        var builder = new StringBuilder(uri.Host).Append(uri.AbsolutePath);
        if (!string.IsNullOrEmpty(uri.Query)) builder.Append("?<").Append(uri.Query.Length).Append(" 字符>");

        return Clip(builder.ToString());
    }

    /// <summary>
    ///     把逗号分隔的 <c>Name: Value</c> 列表压缩成只有名字的形式。<b>不会</b>输出值。
    /// </summary>
    public static string HeaderNames(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers)) return "(无)";

        var names = new StringBuilder();
        foreach (var pair in headers.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf(':');
            var name = index > 0 ? pair[..index].Trim() : pair.Trim();
            if (name.Length == 0) continue;

            if (names.Length > 0) names.Append('+');
            names.Append(name);
        }

        return names.Length == 0 ? "(无)" : names.ToString();
    }

    private static string Clip(string text) => text.Length <= 72 ? text : string.Concat(text.AsSpan(0, 69), "...");
}
