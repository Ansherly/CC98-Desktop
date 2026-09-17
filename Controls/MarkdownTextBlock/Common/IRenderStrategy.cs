using Markdig.Syntax;

namespace CC98.Controls.MarkdownTextBlock.Common;

/// <summary>
///     渲染策略接口。每种 Markdig 节点类型对应一个策略,由 <see cref="MarkdownTextBlock.RenderStrategies" /> 分发。
/// </summary>
public interface IRenderStrategy
{
    void Render(MarkdownObject node, RenderContext context);
}
