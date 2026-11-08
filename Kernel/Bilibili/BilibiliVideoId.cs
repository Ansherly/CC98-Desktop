using System;
using System.Text.RegularExpressions;

namespace CC98.Kernel.Bilibili;

/// <summary>
///     B 站视频标识的归一化:把 <c>[bili]</c> 里五花八门的写法解析成可请求的 id 与分 P 序号。
/// </summary>
/// <remarks>
///     帖子里 <c>[bili]</c> 的内容并不统一:<c>[bili]BV1xx411c7mD[/bili]</c>、<c>[bili=BV1xx411c7mD]</c>、
///     <c>av170001</c>,也有人直接贴整条视频页地址(可能带 <c>?p=2</c> 或 <c>&amp;t=30</c>)。
///     渲染层只需要"能解析 / 不能解析"两种结果,所以归一化放在这一处完成,接口层与控件都复用同一套判定。
/// </remarks>
public static class BilibiliVideoId
{
    /// <summary>视频页前缀(<c>[bili]</c> 里只写号码时,用于拼"在 B 站打开"的地址)。</summary>
    public const string PagePrefix = "https://www.bilibili.com/video/";

    // 编译期单例正则:每次 new Regex 都会重新编译,列表滚动时渲染频繁,不能放在方法里
    private static readonly Regex BvPattern = new(@"BV[0-9A-Za-z]{10}", RegexOptions.Compiled);
    private static readonly Regex AvPattern = new(@"av(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PagePattern = new(@"[?&]p=(\d+)", RegexOptions.Compiled);

    /// <summary>
    ///     尝试解析视频标识。
    /// </summary>
    /// <param name="raw">原始内容,可以是 BV 号、av 号、整条视频页地址,或夹杂着这些的文本。</param>
    /// <param name="id">解析成功时的标识:<c>BVxxxxxxxxxx</c> 或 <c>av12345</c>。</param>
    /// <param name="page">分 P 序号(从 1 开始);解析不到时为 1。</param>
    /// <returns>解析成功返回 <see langword="true" />。</returns>
    public static bool TryParse(string? raw, out string id, out int page)
    {
        id = string.Empty;
        page = 1;

        if (string.IsNullOrWhiteSpace(raw)) return false;

        var text = raw.Trim();

        var pageMatch = PagePattern.Match(text);
        if (pageMatch.Success
            && int.TryParse(pageMatch.Groups[1].Value, out var pageNumber)
            && pageNumber > 0)
            page = pageNumber;

        // BV 号优先:地址里同时出现 av 与 BV 时以 BV 为准
        var bvMatch = BvPattern.Match(text);
        if (bvMatch.Success)
        {
            id = bvMatch.Value;
            return true;
        }

        var avMatch = AvPattern.Match(text);
        if (avMatch.Success)
        {
            id = "av" + avMatch.Groups[1].Value;
            return true;
        }

        return false;
    }

    /// <summary>标识是否为 BV 号(否则按 av 号处理)。</summary>
    public static bool IsBv(string id) => id.StartsWith("BV", StringComparison.Ordinal);

    /// <summary>拼出视频页地址(供"在 B 站打开"使用,分 P 会带上 <c>?p=</c>)。</summary>
    public static string ToPageUrl(string id, int page) =>
        page > 1 ? $"{PagePrefix}{id}?p={page}" : PagePrefix + id;
}
