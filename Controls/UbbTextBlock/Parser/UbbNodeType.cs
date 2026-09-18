using System.Collections.Generic;

namespace CC98.Controls.UbbTextBlock.Parser;

public enum UbbNodeType
{
    Document, // 文档根节点
    Text, // 纯文本
    Bold, // 粗体 [b]
    Italic, // 斜体 [i]
    Underline, // 下划线 [u]
    Strikethrough, // 删除线 [del]
    Size, // 字体大小 [size]
    Font, // 字体 [font]
    Color, // 颜色 [color]
    Url, // 链接 [url]
    Topic, // 话题 [topic]
    Image, // 图片 [img]
    Audio, // 音频 [audio]
    Video, // 视频 [video]
    Code, // 代码块 [code]
    Quote, // 引用 [quote]
    Align, // 对齐 [align]
    Left, // 左对齐 [left]
    Center, // 居中 [center]
    Right, // 右对齐 [right]
    Table, // 表格[table]
    TableRow, // 表格行 [tr]
    TableCell, // 表格单元格 [td]
    Paragraph, // 段落（自动生成）
    Divider, // 分隔线 [line]
    Emoji, // 表情 [em]
    Latex, // 公式
    Upload, //上传
    Bilibili, //B站视频
    NoUbb, //非UBB内容块
    Markdown, //markdown内容
    NeedReply, //需要回复
    ReplyView, //设置回复可见,
    PosterOnly, //仅楼主可见
    At
}

public static class UbbNodeTypeExtensions
{
    private static readonly HashSet<UbbNodeType> BlockTypes =
    [
        UbbNodeType.Code,
        UbbNodeType.Quote,
        UbbNodeType.Table,
        UbbNodeType.TableRow,
        UbbNodeType.TableCell,
        UbbNodeType.Image,
        UbbNodeType.Audio,
        UbbNodeType.Video,
        UbbNodeType.Bilibili,
        UbbNodeType.Paragraph,
        UbbNodeType.Divider,
        UbbNodeType.Align,
        UbbNodeType.Left,
        UbbNodeType.Center,
        UbbNodeType.Right,
        UbbNodeType.NoUbb,
        UbbNodeType.Markdown,
        UbbNodeType.ReplyView
    ];

    // 使用 HashSet 的 Contains 方法判断
    public static bool IsBlock(this UbbNodeType type)
    {
        return BlockTypes.Contains(type);
    }
}