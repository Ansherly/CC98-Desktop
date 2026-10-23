using System;
using System.Linq;

namespace CC98.Controls.ComposeSheet;

/// <summary>
///     表情名（UBB 里 <c>[name]</c> 的 name）→ CC98 站点上的图片地址。
/// </summary>
/// <remarks>
///     各系列的路径以站点实际响应为准（2026-10 实测）：
///     <list type="bullet">
///         <item><c>tb01</c> → <c>/static/images/tb/tb01.png</c>（200）</item>
///         <item><c>ms01</c> → <c>/static/images/ms/ms01.png</c>（200）</item>
///         <item><c>em01</c> → <c>/static/images/em/em01.gif</c>（png 会 404，只有 gif）</item>
///         <item><c>ac01</c> → <c>/static/images/ac/01.png</c>（站点文件名不带 ac 前缀，实测 01-50 可用）</item>
///         <item><c>cc9801</c> → <c>/static/images/CC98/CC9801.gif</c>，<c>cc9823</c> → <c>CC9823.png</c></item>
///     </list>
///     CC98 官方表情里序号 01-14、31-35 是动图（gif），其余是 png —— 与仓库里的 Assets 一致。
/// </remarks>
public static class EmojiUrls
{
    private const string Base = "https://www.cc98.org/static/images/";

    /// <summary>CC98 官方表情中后缀为 gif 的序号，其余按 png 处理。</summary>
    private static readonly int[] Cc98GifIndexes = [.. Enumerable.Range(1, 14), .. Enumerable.Range(31, 5)];

    /// <summary>
    ///     解析表情名对应的图片地址。
    /// </summary>
    /// <param name="name">表情名，例如 <c>tb01</c>、<c>cc9823</c>、<c>823</c>。</param>
    /// <returns>绝对地址；不认识的表情返回 <c>null</c>。</returns>
    public static string? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var n = name.Trim().ToLowerInvariant();

        // cc98 + 2-4 位序号：CC98 官方表情（01-14、31-35 是动图）
        if (n.StartsWith("cc98") && TryParseIndex(n[4..], out var official))
            return Base + $"CC98/CC98{official:00}{Cc98Extension(official)}";

        // 贴吧（tb）、雀魂（ms）：都是 png
        if ((n.StartsWith("tb") || n.StartsWith("ms")) && TryParseIndex(n[2..], out _))
            return Base + $"{n[..2]}/{n}.png";

        // 经典表情（em）：站点上只有 gif
        if (n.StartsWith("em") && TryParseIndex(n[2..], out _))
            return Base + $"em/{n}.gif";

        // AC 娘（ac）：站点上的文件名只有序号，不带 ac 前缀（ac/01.png，实测 01-50 都有）
        if (n.StartsWith("ac") && TryParseIndex(n[2..], out _))
            return Base + $"ac/{n[2..]}.png";

        return null;
    }

    /// <summary>序号是 2-4 位纯数字才算合法表情名。</summary>
    private static bool TryParseIndex(string digits, out int value)
    {
        value = 0;
        if (digits.Length is < 2 or > 4) return false;
        if (!digits.All(char.IsAsciiDigit)) return false;
        return int.TryParse(digits, out value);
    }

    private static string Cc98Extension(int index) => Cc98GifIndexes.Contains(index) ? ".gif" : ".png";
}
