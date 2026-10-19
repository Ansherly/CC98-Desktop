using System;

namespace CC98.Controls.ComposeSheet;

/// <summary>
///     源码插入的纯逻辑：只算「新文本 + 新选区」，不碰任何控件，方便单独验证。
/// </summary>
/// <remarks>
///     语法映射跟 <c>SketchPage</c> 保持一致（粗体 <c>[b]</c> / <c>**</c>、引用 <c>[quote]</c> / <c>&gt; </c>、
///     分隔线 <c>[line]</c> / <c>---</c>……），唯一的改进是两种语言都支持「有选中就包住选中内容」。
/// </remarks>
internal static class MarkupEditing
{
    /// <summary>插入后的结果：整段新文本，以及应该重新设给编辑框的选区。</summary>
    public readonly record struct Result(string Text, int SelectionStart, int SelectionLength);

    /// <summary>包裹式插入：有选中就包住它；没选中就插入占位符并选中占位符，直接输入即可替换。</summary>
    public static Result Wrap(string text, int start, int length, string open, string close, string placeholder)
    {
        if (length > 0)
        {
            var selected = text.Substring(start, length);
            var insert = open + selected + close;
            return new Result(text[..start] + insert + text[(start + length)..], start + insert.Length, 0);
        }

        var literal = open + placeholder + close;
        var updated = text[..start] + literal + text[(start + length)..];

        // 占位符为空时把光标停在开标签里面，便于直接打字
        return new Result(updated, start + open.Length, placeholder.Length);
    }

    /// <summary>
    ///     块级插入：前后按需补换行。
    ///     只有在「光标前不是行首」时才补前导换行、后面不是换行/文末时才补尾随换行 ——
    ///     否则在空文档或行首插入会凭空多出一行空白。
    /// </summary>
    public static Result Block(string text, int start, int length, string open, string close, string placeholder)
    {
        var hasSelection = length > 0;
        var selected = hasSelection ? text.Substring(start, length) : placeholder;

        var prefix = NeedsLeadingNewline(text, start) ? "\n" : "";
        var suffix = NeedsTrailingNewline(text, start + length) ? "\n" : "";

        var insert = prefix + open + selected + close + suffix;
        var updated = text[..start] + insert + text[(start + length)..];

        return hasSelection
            ? new Result(updated, start + insert.Length, 0)
            : new Result(updated, start + prefix.Length + open.Length, placeholder.Length);
    }

    /// <summary>块级字面量（比如 MD 的 <c>---</c>）：前后按需补换行，光标落在后面。</summary>
    public static Result BlockLiteral(string text, int start, int length, string literal)
    {
        var prefix = NeedsLeadingNewline(text, start) ? "\n" : "";
        var suffix = NeedsTrailingNewline(text, start + length) ? "\n" : "";
        var insert = prefix + literal + suffix;

        return new Result(text[..start] + insert + text[(start + length)..], start + insert.Length, 0);
    }

    /// <summary>插入一段字面量（分隔线、哔哩 iframe 之类），光标落在末尾。</summary>
    public static Result Literal(string text, int start, int length, string literal)
    {
        return new Result(text[..start] + literal + text[(start + length)..], start + literal.Length, 0);
    }

    /// <summary>插入字面量，但把其中的 <paramref name="marker" /> 选中（媒体标签的 url 占位）。</summary>
    public static Result LiteralSelecting(string text, int start, int length, string literal, string marker)
    {
        var updated = text[..start] + literal + text[(start + length)..];
        var index = literal.IndexOf(marker, StringComparison.Ordinal);

        return index < 0
            ? new Result(updated, start + literal.Length, 0)
            : new Result(updated, start + index, marker.Length);
    }

    /// <summary>Markdown 引用：选中的每一行前面加 <c>&gt; </c>；没选中就插一个空引用行。</summary>
    public static Result QuoteMd(string text, int start, int length)
    {
        if (length > 0)
        {
            var selected = NormalizeNewlines(text.Substring(start, length));
            var quoted = "> " + selected.Replace("\n", "\n> ");
            return new Result(text[..start] + quoted + text[(start + length)..], start + quoted.Length, 0);
        }

        const string placeholder = "引用文本";
        const string marker = "> ";

        var prefix = NeedsLeadingNewline(text, start) ? "\n" + marker : marker;
        var suffix = NeedsTrailingNewline(text, start + length) ? "\n" : "";
        var literal = prefix + placeholder + suffix;

        return new Result(text[..start] + literal + text[start..], start + prefix.Length, placeholder.Length);
    }

    /// <summary>Markdown 链接：<c>[链接文字](url)</c>，选中「链接文字」。</summary>
    public static Result LinkMd(string text, int start, int length, string placeholder = "链接文字", string url = "url")
    {
        var literal = $"[{placeholder}]({url})";
        return new Result(text[..start] + literal + text[(start + length)..], start + 1, placeholder.Length);
    }

    /// <summary>UBB 链接：<c>[url=url]链接文字[/url]</c>，选中 url 那一段。</summary>
    public static Result LinkUbb(string text, int start, int length, string url = "url", string placeholder = "链接文字")
    {
        const string openPrefix = "[url=";
        var literal = $"{openPrefix}{url}]{placeholder}[/url]";
        var updated = text[..start] + literal + text[(start + length)..];

        return new Result(updated, start + openPrefix.Length, url.Length);
    }

    /// <summary>统一换行成 \n，避免 TextBox 的 \r\n 让「每行加前缀」重复插入 \r。</summary>
    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>插入点前不是行首（且不在文首）才需要补换行。</summary>
    private static bool NeedsLeadingNewline(string text, int index)
    {
        return index > 0 && text[index - 1] != '\n';
    }

    /// <summary>插入点后既不是换行也不是文末时，才需要补尾随换行。</summary>
    private static bool NeedsTrailingNewline(string text, int index)
    {
        return index < text.Length && text[index] != '\n';
    }
}
