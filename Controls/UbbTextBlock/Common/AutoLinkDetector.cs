using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CC98.Controls.UbbTextBlock.Parser;

namespace CC98.Controls.UbbTextBlock.Common;

/// <summary>
///     裸 URL 识别:把一段纯文本切成"普通文本 + 链接"若干段,供 <see cref="TextRenderStrategy" /> 输出可点击链接。
/// </summary>
/// <remarks>
///     <para>纯 C#(不引用任何 WinUI 类型),可直接被测试工程编译。</para>
///     <para>
///         识别范围(已确认):只认 <c>http://</c> / <c>https://</c> 与 <c>www.</c> 开头的链接;
///         不做裸域名识别(<c>cc98.org/x</c> 不会被链接);<c>ftp://</c> / <c>mailto:</c> 等特殊协议不支持
///         —— 如需支持,在 <see cref="UrlCandidateRegex" /> 的前缀里加上对应 scheme,并在 <see cref="Detect" /> 里补默认协议即可。
///     </para>
/// </remarks>
internal static partial class AutoLinkDetector
{
    /// <summary>www. 链接点击时补全的协议。</summary>
    private const string DefaultScheme = "https://";

    /// <summary>
    ///     候选 URL:以 http(s):// 或 www. 开头,后接一串"URL 可能出现的字符"。
    ///     字符集刻意排除空白、引号、尖括号、方括号/花括号(UBB 标签语法)、反引号、星号与中日韩标点;
    ///     括号先纳入匹配,再由 <see cref="TrimTail" /> 按配对情况裁掉多余的右括号。
    /// </summary>
    [GeneratedRegex(@"(?:(?:https?://)|(?:www\.))[^\s<>""'`\[\]{}*\u3000-\u303F\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF\uFF00-\uFFEF\u2018\u2019\u201C\u201D\u2026\u2014\uFF5E]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlCandidateRegex();

    /// <summary>识别结果:相对原文的起止位置,以及点击应打开的地址(www. 会补 https://)。</summary>
    public readonly record struct AutoLinkSpan(int Start, int Length, string Url);

    /// <summary>
    ///     识别文本里的裸链接;没有命中时返回空列表。
    /// </summary>
    public static List<AutoLinkSpan> Detect(string text)
    {
        var spans = new List<AutoLinkSpan>();
        if (string.IsNullOrEmpty(text)) return spans;

        foreach (Match match in UrlCandidateRegex().Matches(text))
        {
            var candidate = match.Value;
            var length = TrimTail(candidate);
            if (length <= 0) continue;

            var url = candidate[..length];
            // www. 开头的补上默认协议;http(s) 原样使用
            if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                // 要求主机名里至少再有一个点(www.a.b),挡掉 "www.foo" 这类误报
                var hostEnd = url.IndexOfAny(['/', '?', '#', ':'], 4);
                var host = hostEnd < 0 ? url[4..] : url[4..hostEnd];
                if (!host.Contains('.')) continue;
                url = DefaultScheme + url;
            }

            spans.Add(new AutoLinkSpan(match.Index, length, url));
        }

        return spans;
    }

    /// <summary>
    ///     该文本节点是否允许自动链接:位于 <c>[url]</c> 内部时不参与(否则会与标签自身的链接重复)。
    /// </summary>
    /// <remarks>沿父链向上查,走到块级边界(或文档根)为止 —— 块级内容之间不会共享行内上下文。</remarks>
    public static bool IsAutoLinkAllowed(UbbNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Type == UbbNodeType.Url) return false;
            if (parent.Type == UbbNodeType.Document || parent.Type.IsBlock()) return true;
        }

        return true;
    }

    /// <summary>
    ///     裁掉 URL 尾部的标点:先按配对裁掉多余的右括号,再反复裁掉中英文句读与引号。
    /// </summary>
    private static int TrimTail(string candidate)
    {
        var length = candidate.Length;

        // 1) 括号配对:只在"右括号多于左括号"时裁掉末尾的 ')'
        //    例:(https://a.com/x) → 裁掉收尾 ')'  ;  https://a.com/Page_(film) → 保留成对括号
        while (length > 0 && candidate[length - 1] == ')')
        {
            var open = 0;
            var close = 0;
            for (var i = 0; i < length; i++)
            {
                if (candidate[i] == '(') open++;
                else if (candidate[i] == ')') close++;
            }

            if (close <= open) break;
            length--;
        }

        // 2) 尾部标点:句号、逗号、分号、冒号、感叹号、问号、引号等
        //    注意:右括号只归上面的配对规则管 —— 若在这里无条件裁掉,会切掉 Page_(film) 的尾巴
        while (length > 0 && IsTrailingPunctuation(candidate[length - 1])) length--;

        return length;
    }

    private static bool IsTrailingPunctuation(char c)
    {
        return c switch
        {
            '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '"' or '>' => true,
            '。' or '，' or '、' or '；' or '：' or '！' or '？' or '…' => true,
            '“' or '”' or '‘' or '’' => true,
            '（' or '）' or '【' or '】' or '「' or '」' or '『' or '』' => true,
            '《' or '》' or '〈' or '〉' => true,
            _ => false
        };
    }
}
