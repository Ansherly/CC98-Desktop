using System.Text;

namespace CC98.Controls.UbbTextBlock.Parser;

/// <summary>
///     UBB 节点的纯文本收集工具。
/// </summary>
/// <remarks>
///     纯 C#，不引用任何 WinUI 类型，因此可以脱离主工程单独编译做回归测试
///     （见技能 <c>dotnet-source-probe</c>）。
/// </remarks>
public static class UbbNodeTextExtensions
{
    /// <summary>
    ///     递归收集节点**所有后代**里的文本，并去除首尾空白。
    /// </summary>
    /// <remarks>
    ///     地址类标签（img / url / audio / video / upload）的地址常常写在子节点里，
    ///     而子节点可能被 <c>[b]</c> / <c>[color]</c> 等包住，
    ///     例如 <c>[img][b]http://…[/b][/img]</c>。此时只看第一个子节点是不是
    ///     <see cref="TextNode" /> 会漏掉地址，必须递归收集。
    /// </remarks>
    public static string CollectText(this UbbNode node)
    {
        var sb = new StringBuilder();
        Collect(node, sb);
        return sb.ToString().Trim();
    }

    private static void Collect(UbbNode node, StringBuilder sb)
    {
        foreach (var child in node.Children)
            if (child is TextNode textNode)
                sb.Append(textNode.Content);
            else
                Collect(child, sb);
    }
}
