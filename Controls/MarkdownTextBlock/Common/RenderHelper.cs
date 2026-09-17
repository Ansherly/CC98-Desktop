using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CC98.Controls.MarkdownTextBlock.Common;

/// <summary>
///     渲染辅助方法。
/// </summary>
public static class RenderHelper
{
    /// <summary>
    ///     提取叶子块的原始文本(代码块 / HTML 块等)。
    /// </summary>
    public static string GetLeafText(LeafBlock block)
    {
        var group = block.Lines;
        var lines = group.Lines;
        var count = group.Count;

        if (count <= 0) return string.Empty;
        if (count == 1) return lines[0].Slice.ToString() ?? string.Empty;

        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Append(lines[i].Slice.ToString());
            if (i < count - 1) builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    ///     递归收集节点及其子节点的纯文本。
    /// </summary>
    public static string CollectText(MarkdownObject node)
    {
        var builder = new StringBuilder();
        CollectTextCore(node, builder);
        return builder.ToString();
    }

    private static void CollectTextCore(MarkdownObject node, StringBuilder builder)
    {
        switch (node)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                return;
            case CodeInline code:
                builder.Append(code.Content);
                return;
            case HtmlEntityInline entity:
                builder.Append(entity.Transcoded.ToString());
                return;
            case LineBreakInline:
                builder.Append(' ');
                return;
            case LeafBlock { Inline: not null } leafBlock:
                CollectTextCore(leafBlock.Inline, builder);
                return;
            case LeafBlock leaf:
                builder.Append(GetLeafText(leaf));
                return;
            case ContainerInline containerInline:
                foreach (var child in containerInline) CollectTextCore(child, builder);
                return;
            case ContainerBlock containerBlock:
                foreach (var child in containerBlock) CollectTextCore(child, builder);
                return;
        }
    }

    /// <summary>
    ///     判断图片是否独占一个段落(独占时按块级图片渲染,否则按行内图片渲染)。
    /// </summary>
    public static bool IsStandaloneImage(LinkInline link)
    {
        if (link.Parent is not ContainerInline parent) return true;

        var count = 0;
        foreach (var child in parent)
        {
            if (child is LineBreakInline) continue;
            if (child is LiteralInline literal && string.IsNullOrWhiteSpace(literal.Content.ToString())) continue;
            count++;
            if (count > 1) return false;
        }

        return true;
    }

    /// <summary>
    ///     从围栏代码块的信息串中取出语言标识(如 "csharp title=x" → "csharp")。
    /// </summary>
    public static string GetLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return "plaintext";

        var trimmed = info.Trim();
        var spaceIndex = trimmed.IndexOfAny([' ', '\t', '{', ',']);
        var token = spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed;

        // 常见别名归一化
        token = token.Trim().ToLowerInvariant();
        return token switch
        {
            "" or "text" or "txt" or "none" => "plaintext",
            "cs" or "c#" => "csharp",
            "js" => "javascript",
            "ts" => "typescript",
            "py" => "python",
            "rb" => "ruby",
            "sh" or "shell" => "bash",
            "ps" or "ps1" => "powershell",
            "yml" => "yaml",
            "htm" => "html",
            "md" => "markdown",
            _ => token
        };
    }

    /// <summary>
    ///     标题字号缩放系数(H1 → H6)。
    /// </summary>
    public static double GetHeadingScale(int level)
    {
        return level switch
        {
            1 => 2.0,
            2 => 1.7,
            3 => 1.4,
            4 => 1.2,
            5 => 1.1,
            _ => 1.0
        };
    }
}
