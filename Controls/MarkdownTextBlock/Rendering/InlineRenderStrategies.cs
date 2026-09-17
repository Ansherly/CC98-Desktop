using System;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using CC98.Controls.MarkdownTextBlock.Common;
using Inline = Microsoft.UI.Xaml.Documents.Inline;

namespace CC98.Controls.MarkdownTextBlock.Rendering;

#region 文本

/// <summary>普通文本。</summary>
public class LiteralRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not LiteralInline literal) return;

        var text = literal.Content.ToString();
        if (string.IsNullOrEmpty(text)) return;

        context.AddInline(new Run { Text = text });
    }
}

#endregion

#region 强调

/// <summary>粗体 / 斜体 / 删除线(**、*、~~,支持嵌套)。</summary>
public class EmphasisRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not EmphasisInline emphasis) return;

        Inline container = emphasis.DelimiterChar switch
        {
            // ~~删除线~~(单波浪线在 EmphasisExtras 中表示下标,这里按普通行内处理)
            '~' when emphasis.DelimiterCount >= 2 => new Span { TextDecorations = TextDecorations.Strikethrough },
            _ => emphasis.DelimiterCount >= 2 ? CreateBold(context) : new Italic()
        };

        context.BeginInlineContainer(container);
        context.RenderChildren(emphasis);
        context.EndInlineContainer();
    }

    /// <summary>
    ///     创建粗体容器。粗体字体未指定时不设置 FontFamily(null 会被解析为无效字体名)。
    /// </summary>
    private static Bold CreateBold(RenderContext context)
    {
        var bold = new Bold();
        var boldFontFamily = context.GetProperty<FontFamily>(RenderPropertyKeys.BoldFontFamily);
        if (boldFontFamily is not null) bold.FontFamily = boldFontFamily;
        return bold;
    }
}

#endregion

#region 行内代码

/// <summary>行内代码:等宽字体 + 代码块背景色。</summary>
public class CodeInlineRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not CodeInline code) return;

        context.AddInline(ElementFactory.CreateInlineCode(context, code.Content ?? string.Empty));
    }
}

#endregion

#region 链接

/// <summary>Markdown 链接(含行内图片判定)。</summary>
public class LinkRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not LinkInline link) return;

        // 图片:`![alt](src)`
        if (link.IsImage)
        {
            var alt = RenderHelper.CollectText(link).Trim();
            var src = link.Url ?? string.Empty;

            if (string.IsNullOrWhiteSpace(src))
            {
                context.AddInline(new Run { Text = alt });
                return;
            }

            if (RenderHelper.IsStandaloneImage(link))
                context.AddToContainer(ElementFactory.CreateBlockImage(context, src, alt));
            else
                context.AddInline(ElementFactory.CreateInlineImage(context, src, alt));

            return;
        }

        // 脚注引用 / 页内锚点:不做超链接,按普通文本渲染
        var url = link.Url ?? string.Empty;
        if (url.Length == 0 || url.StartsWith('#'))
        {
            context.RenderChildren(link);
            return;
        }

        var text = RenderHelper.CollectText(link);
        var hyperlink = ElementFactory.CreateLink(context, url, text);

        context.BeginInlineContainer(hyperlink);
        context.RenderChildren(link);
        context.EndInlineContainer();
    }
}

/// <summary>自动链接(&lt;url&gt; 与 GFM 裸 URL / 邮箱)。</summary>
public class AutolinkRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not AutolinkInline autolink) return;

        var url = autolink.Url ?? string.Empty;
        if (url.Length == 0) return;

        var hyperlink = ElementFactory.CreateLink(context, url, url, true);
        hyperlink.Inlines.Add(new Run { Text = url });
        context.AddInline(hyperlink);
    }
}

#endregion

#region 换行与实体

/// <summary>换行(硬换行与软换行均按换行处理,保留用户输入的折行)。</summary>
public class LineBreakRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        context.AddInline(new LineBreak());
    }
}

/// <summary>HTML 实体(&amp;amp; 等)。</summary>
public class HtmlEntityRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not HtmlEntityInline entity) return;
        context.AddInline(new Run { Text = entity.Transcoded.ToString() });
    }
}

#endregion

#region HTML 行内标签

/// <summary>HTML 行内标签(白名单映射,详见 <see cref="HtmlTagMapper" />)。</summary>
public class HtmlInlineRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not HtmlInline html) return;
        context.HtmlMapper.ProcessTag(html.Tag);
    }
}

#endregion

#region 任务列表

/// <summary>GFM 任务列表勾选框。</summary>
public class TaskListRenderStrategy : IRenderStrategy
{
    public void Render(MarkdownObject node, RenderContext context)
    {
        if (node is not TaskList taskList) return;

        context.AddInline(ElementFactory.CreateTaskListCheckBox(taskList.Checked));
    }
}

#endregion
