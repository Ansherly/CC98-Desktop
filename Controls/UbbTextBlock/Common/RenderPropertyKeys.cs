namespace CC98.Controls.UbbTextBlock.Common;

/// <summary>
///     渲染上下文属性袋的键名(由 <see cref="UbbTextBlock" /> 在渲染前填充)。
/// </summary>
/// <remarks>
///     键集中在此定义,避免各策略里散落字符串字面量;按项目约定与 MarkdownTextBlock 的同类定义各自维护。
/// </remarks>
internal static class RenderPropertyKeys
{
    public const string FontSize = "FontSize";
    public const string Foreground = "Foreground";
    public const string BoldFontFamily = "BoldFontFamily";
    public const string CodeBackground = "CodeBackground";
    public const string QuoteBackground = "QuoteBackground";
    public const string LinkForeground = "LinkForeground";
    public const string ImageMaxWidth = "ImageMaxWidth";
    public const string ImageMaxHeight = "ImageMaxHeight";
}
