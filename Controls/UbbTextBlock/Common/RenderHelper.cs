using CC98.Controls.UbbTextBlock.Parser;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Text;
using Windows.UI.Text;
namespace CC98.Controls.UbbTextBlock.Common;

public class RenderHelper
{
    /// <summary>从渲染属性袋中取出画刷(键不存在或类型不符时返回 null)。</summary>
    public static Brush? GetBrush(RenderContext context, string key)
    {
        return context.Properties.TryGetValue(key, out var value) ? value as Brush : null;
    }

    /// <summary>从渲染属性袋中取出字体(键不存在或类型不符时返回 null)。</summary>
    public static FontFamily? GetFontFamily(RenderContext context, string key)
    {
        return context.Properties.TryGetValue(key, out var value) ? value as FontFamily : null;
    }

    /// <summary>从渲染属性袋中取出 bool(键不存在或类型不符时返回 <paramref name="fallback" />)。</summary>
    public static bool GetBool(RenderContext context, string key, bool fallback = false)
    {
        return context.Properties.TryGetValue(key, out var value) && value is bool flag ? flag : fallback;
    }

    /// <summary>
    ///     给链接统一应用样式(颜色 + 可选下划线)。所有链接类策略都应经由本方法,便于后续统一调整。
    /// </summary>
    /// <remarks>颜色读取属性袋的 <c>LinkForeground</c>(M2 会由同名依赖属性填充);未设置时保持 CC98 传统色。</remarks>
    public static void ApplyLinkStyle(Hyperlink hyperlink, RenderContext context, bool underline = true)
    {
        hyperlink.Foreground = GetBrush(context, RenderPropertyKeys.LinkForeground) ??
                               new SolidColorBrush(Colors.LightSeaGreen);
        hyperlink.TextDecorations = underline ? TextDecorations.Underline : TextDecorations.None;
    }

    public static void ApplyAlignToContext(string align, UbbNode node, RenderContext context)
    {
        //使用Grid进行对齐控制
        var panel = new StackPanel
        {
            HorizontalAlignment = align switch
            {
                "center" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left
            }
        };

        // 切入面板渲染子节点(容器状态的保存 / 恢复由 RenderContext 统一负责)
        context.PushContainer(panel);
        foreach (var child in node.Children) context.RenderNode(child);
        context.FinalizeCurrentTextBlock();
        context.PopContainer();

        // 将 panel 添加回之前的容器
        context.AddToContainer(panel);
    }


    public static string CollectText(UbbNode node)
    {
        var sb = new StringBuilder();
        CollectTextRecursive(node, sb);
        return sb.ToString().Trim();
    }

    private static void CollectTextRecursive(UbbNode node, StringBuilder sb)
    {
        foreach (var child in node.Children)
            if (child is TextNode textNode)
                sb.Append(textNode.Content);
            else
                CollectTextRecursive(child, sb);
    }
}
