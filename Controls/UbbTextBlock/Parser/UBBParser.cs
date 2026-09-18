using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CC98.Controls.UbbTextBlock.Tokenizer;

namespace CC98.Controls.UbbTextBlock.Parser;

/// <summary>
///     将词元序列转换为 UBB 文档树的核心类。
/// </summary>
public class UbbParser(IEnumerable<Token> tokens)
{
    private readonly List<Token> _tokens = [.. tokens];
    private int _index;

    private Token Peek()
    {
        return _index < _tokens.Count ? _tokens[_index] : new(TokenType.Eof, "", -1);
    }

    private Token Consume()
    {
        return _tokens[_index++];
    }

    public UbbDocument Parse()
    {
        var doc = new UbbDocument();
        // 递归解析根节点，直到 EOF
        ParseContent(doc.Root, null);
        return doc;
    }

    /// <param name="parent">当前父节点</param>
    /// <param name="closingTag">期望遇到的闭合标签名，为 null 则解析到 EOF</param>
    private void ParseContent(UbbNode parent, UbbNodeType? closingTag)
    {
        while (_index < _tokens.Count)
        {
            var token = Peek();

            // 1. 闭合标签检测逻辑 (保持原样)
            if (token.Type == TokenType.LeftBracket && PeekNext()?.Type == TokenType.Slash)
            {
                var nextTagNameToken = PeekOffset(2);
                if (nextTagNameToken?.Type == TokenType.TagName)
                {
                    var foundType = MapToNodeType(nextTagNameToken.Value);
                    if (closingTag != null && closingTag == foundType)
                    {
                        // 消费 [/tag] 并返回
                        Consume();
                        Consume();
                        Consume();
                        if (Peek().Type == TokenType.RightBracket) Consume();
                        return;
                    }

                    if (closingTag != null && IsKnownType(foundType)) return;
                }
            }

            var node = ParseElement();
            if (node != null)
            {
                parent.AddChild(node);

                if (node is TagNode tag && !IsSelfClosing(tag.Type))
                {
                    if (tag.Type == UbbNodeType.Code || tag.Type == UbbNodeType.NoUbb ||
                        tag.Type == UbbNodeType.Markdown)
                        // 进入“逐字模式”，直接寻找闭合标签
                        ParseVerbatimContent(tag);
                    else
                        ParseContent(tag, tag.Type);
                }
            }
            else
            {
                _index++;
            }
        }
    }

    private UbbNode ParseElement()
    {
        var token = Peek();
        switch (token.Type)
        {
            case TokenType.Text:
                Consume();
                return new TextNode(token.Value);
            case TokenType.Dollar:
            case TokenType.DoubleDollar:
                return ParseLatex();
            case TokenType.At: // 新增：处理@提及
                Consume();
                return new AtNode(token.Value);
            case TokenType.LeftBracket:
                Consume(); // 消耗 '['
                return ParseTagHeaderOrFallback();

            case TokenType.Eof:
                return null;

            default:
                // 遇到未知的 Token 类型（如孤立的等号、逗号），当作文本处理
                Consume();
                return new TextNode(token.Value);
        }
    }

    // 核心改进：处理标签头，失败则回退为文本
    private UbbNode ParseTagHeaderOrFallback()
    {
        // 记录进入此方法前的起始索引（即 '[' 之后的位置，由于 '[' 已消费，起始应为 _index - 1）
        var startIndex = _index - 1;

        // 1. 检查 TagName
        if (Peek().Type != TokenType.TagName) return new TextNode("[");

        var name = Consume().Value;
        var type = MapToNodeType(name);

        // 2. 如果是未知标签，直接回退
        if (type == UbbNodeType.Text) return new TextNode("[" + name);


        var attributes = new Dictionary<string, string>();
        var attrCount = 0;

        // 3. 解析属性循环
        while (Peek().Type != TokenType.RightBracket && Peek().Type != TokenType.Eof)
        {
            var t = Consume();

            if (t.Type == TokenType.Equal || t.Type == TokenType.Comma)
            {
                // 关键修复点：如果在等号/逗号后紧跟的是另一个 '['，说明格式非法
                if (Peek().Type == TokenType.LeftBracket) return FallbackToText(startIndex);

                if (Peek().Type == TokenType.AttrValue)
                {
                    var valToken = Consume();

                    // 关键修复点：如果 Scanner 错误地将 '[' 包含在 AttrValue 中，这里进行二次检查
                    if (valToken.Value.Contains('[')) return FallbackToText(startIndex);

                    var key = attrCount == 0 ? GetAttributeName(type) : $"value{attrCount}";
                    attributes[key] = valToken.Value;
                    attrCount++;
                }
                else
                {
                    // 如果有 = 但后面不是合法的属性值（也不是闭合括号），回退
                    if (Peek().Type != TokenType.RightBracket && Peek().Type != TokenType.Comma)
                        return FallbackToText(startIndex);
                }
            }
            else
            {
                // 标签内部出现了意料之外的 Token 类型
                return FallbackToText(startIndex);
            }
        }

        if (type == UbbNodeType.Emoji) attributes["code"] = name;
        // 4. 检查是否以 ']' 正常结尾
        if (Peek().Type == TokenType.RightBracket)
        {
            Consume();
            return TagNode.Create(type, attributes);
        }

        // 到达 EOF 仍未闭合，回退
        return FallbackToText(startIndex);
    }

    public string GetAttributeName(UbbNodeType type)
    {
        return type switch
        {
            UbbNodeType.Size => "size",
            UbbNodeType.Font => "font",
            UbbNodeType.Color => "color",
            UbbNodeType.Url => "href",
            UbbNodeType.Code => "language",
            UbbNodeType.Quote => "author",
            UbbNodeType.Emoji => "code",
            _ => "value"
        };
    }

    private void ParseVerbatimContent(TagNode parent)
    {
        var sb = new StringBuilder();
        // 确定我们要找的闭合标签名（统一转小写处理）
        var targetType = GetVerbatimTagType(parent.Type);

        while (_index < _tokens.Count)
        {
            // 探测：当前位置是否是闭合标签 [/targetName]
            if (IsClosingTag(targetType))
            {
                // 1. 将之前积累的所有文本存入 TextNode
                if (sb.Length > 0) parent.AddChild(new TextNode(sb.ToString()));

                // 2. 消费掉整个闭合标签 [/xxx]
                Consume(); // [
                Consume(); // /
                Consume(); // TagName
                if (Peek().Type == TokenType.RightBracket) Consume(); // ]

                return; // 结束逐字解析，返回父级
            }

            // 没遇到闭合标签，则消费当前 Token 并记录其原始值
            sb.Append(Consume().Value);
        }

        // 容错：如果直到 EOF 都没找到闭合标签
        if (sb.Length > 0) parent.AddChild(new TextNode(sb.ToString()));
    }

    // 辅助方法：精准探测闭合标签
    private bool IsClosingTag(UbbNodeType tagType)
    {
        return Peek().Type == TokenType.LeftBracket &&
               PeekNext()?.Type == TokenType.Slash &&
               PeekOffset(2)?.Type == TokenType.TagName &&
               MapToNodeType(PeekOffset(2).Value) == tagType &&
               PeekOffset(3)?.Type == TokenType.RightBracket;
    }

    private LatexNode ParseLatex()
    {
        //这里只有$形式的Latex会调用到
        //[math]是TagNode而不是LatexNode,尽管它们的Type属性相同
        //用CollectText获得公式块的Latex,用Latex属性获得行内公式的Latex
        var token = Consume();
        var isBlock = token.Type == TokenType.DoubleDollar;
        var latex = "";
        if (Peek().Type == TokenType.Text) latex = Consume().Value;
        if (Peek().Type == token.Type) Consume();
        return new(latex, isBlock);
    }

    private bool IsKnownType(UbbNodeType type)
    {
        return type != UbbNodeType.Text && type != UbbNodeType.Document;
    }

    private Token PeekNext()
    {
        return PeekOffset(1);
    }

    private Token PeekOffset(int offset)
    {
        return (_index + offset < _tokens.Count) ? _tokens[_index + offset] : null;
    }

    private bool IsSelfClosing(UbbNodeType type)
    {
        return type switch
        {
            UbbNodeType.Divider => true, // [line]
            UbbNodeType.Emoji => true, // [ac01]
            _ => false
        };
    }

    private UbbNodeType GetVerbatimTagType(UbbNodeType src)
    {
        return src switch
        {
            UbbNodeType.Code => UbbNodeType.Code,
            UbbNodeType.NoUbb => UbbNodeType.NoUbb,
            UbbNodeType.Markdown => UbbNodeType.Markdown,
            _ => throw new InvalidOperationException()
        };
    }

    //注册新标签节点必要的映射
    private UbbNodeType MapToNodeType(string tagName)
    {
        tagName = tagName.ToLower();
        // 处理 CC98 特有的表情前缀
        string[] emojiPrefix = ["cc98", "a:", "c:", "f:", "tb", "ms", "em", "ac"];
        if (emojiPrefix.Any(p => tagName.StartsWith(p)))
            return UbbNodeType.Emoji;

        return tagName switch
        {
            "b" => UbbNodeType.Bold,
            "i" => UbbNodeType.Italic,
            "u" => UbbNodeType.Underline,
            "del" => UbbNodeType.Strikethrough,
            "size" => UbbNodeType.Size,
            "font" => UbbNodeType.Font,
            "color" => UbbNodeType.Color,
            "url" => UbbNodeType.Url,
            "topic" => UbbNodeType.Topic,
            "img" => UbbNodeType.Image,
            "audio" => UbbNodeType.Audio,
            "video" => UbbNodeType.Video,
            "code" => UbbNodeType.Code,
            "quote" => UbbNodeType.Quote,
            "quotex" => UbbNodeType.Quote,
            "align" => UbbNodeType.Align,
            "left" => UbbNodeType.Left,
            "center" => UbbNodeType.Center,
            "right" => UbbNodeType.Right,
            "table" => UbbNodeType.Table,
            "tr" => UbbNodeType.TableRow,
            "td" => UbbNodeType.TableCell,
            "hr" => UbbNodeType.Divider,
            "line" => UbbNodeType.Divider,
            "math" => UbbNodeType.Latex,
            "bili" => UbbNodeType.Bilibili,
            "upload" => UbbNodeType.Upload,
            "noubb" => UbbNodeType.NoUbb,
            "md" => UbbNodeType.Markdown,
            "replyview" => UbbNodeType.ReplyView,
            "needreply" => UbbNodeType.NeedReply,
            _ => UbbNodeType.Text
        };
    }


    // 辅助方法：将当前解析进度涉及的所有 Token 还原为原始文本
    private TextNode FallbackToText(int startIndex)
    {
        var sb = new StringBuilder();
        // 从最初的 '[' 开始拼接
        for (var i = startIndex; i < _index; i++) sb.Append(_tokens[i].Value);
        return new(sb.ToString());
    }
}