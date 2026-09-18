using System.Collections.Generic;

namespace CC98.Controls.UbbTextBlock.Tokenizer;

/// <summary>
///     将原始文本分解为一系列 Token 的词法分析器。
/// </summary>
/// <param name="input"></param>
public class UbbTokenizer(string input)
{
    private readonly string _input = input ?? "";
    private bool _inTag;
    private TokenType _lastType = TokenType.Eof; // 记录上一个 Token 类型以判断上下文
    private int _pos;

    public IEnumerable<Token> ScanTokens()
    {
        while (_pos < _input.Length)
        {
            Token token;
            if (_inTag)
            {
                token = ScanInTag();
            }
            else
            {
                var c = Peek();
                if (c == '[')
                {
                    _inTag = true;
                    token = new(TokenType.LeftBracket, Advance().ToString(), _pos - 1);
                }
                else if (c == '$')
                {
                    token = ScanMathDelimiter();
                }
                else if (c == '@') // 新增：处理@符号
                {
                    token = ScanAtMention();
                }
                else
                {
                    token = ScanText();
                }
            }

            _lastType = token.Type;
            yield return token;
        }

        yield return new(TokenType.Eof, "", _pos);
    }

    private Token ScanInTag()
    {
        var c = Peek();

        switch (c)
        {
            case ']':
                _inTag = false;
                return new(TokenType.RightBracket, Advance().ToString(), _pos - 1);
            case ',':
                return new(TokenType.Comma, Advance().ToString(), _pos - 1);
            case '=':
                // 关键修复：只有在 TagName 之后，等号才是属性开始的分隔符
                if (_lastType == TokenType.TagName) return new(TokenType.Equal, Advance().ToString(), _pos - 1);
                // 否则（例如在 URL 内部），它是属性内容的一部分，交给 ScanTagContent 处理
                return ScanTagContent();
            case '/':
                if (_lastType == TokenType.LeftBracket) return new(TokenType.Slash, Advance().ToString(), _pos - 1);
                return ScanTagContent();
            default:
                return ScanTagContent();
        }
    }


    private Token ScanTagContent()
    {
        var start = _pos;
        while (_pos < _input.Length && !IsTagDelimiter(Peek())) Advance();

        var value = _input[start.._pos];

        // 核心逻辑：根据上一个 Token 判断当前内容的性质
        // 如果前面是 '[' 或 '[/'，则当前是标签名
        if (_lastType == TokenType.LeftBracket || _lastType == TokenType.Slash)
            return new(TokenType.TagName, value, start);

        // 否则（前面是 '=' 或 ','），视为属性值
        return new(TokenType.AttrValue, value, start);
    }

    private Token ScanText()
    {
        var start = _pos;
        while (_pos < _input.Length && Peek() != '[' && Peek() != '$' && Peek() != '@') Advance();
        return new(TokenType.Text, _input[start.._pos], start);
    }

    private Token ScanMathDelimiter()
    {
        var start = _pos;
        Advance();
        if (Peek() == '$')
        {
            Advance();
            return new(TokenType.DoubleDollar, "$$", start);
        }

        return new(TokenType.Dollar, "$", start);
    }

    /// <summary>
    ///     扫描@提及，格式：@用户名（后跟空格）
    ///     用户名限制：5个以内汉字或10个以内英文/数字，只能是汉字（包括日韩）、数字、外文字母
    /// </summary>
    private Token ScanAtMention()
    {
        var start = _pos;
        Advance(); // 消费 '@'

        var nameStart = _pos;
        //这个长度是字符长度
        var nameLength = 0;
        // 等效长度:字母数字算 1、汉字等东亚文字算 2,边扫描边累加,避免每个字符都从头重算一遍
        var nameWidth = 0;
        var isValid = true;

        // 解析用户名
        while (_pos < _input.Length)
        {
            var c = Peek();

            // 用户名后必须紧跟空格才结束
            if (c == ' ') break;

            // 检查字符是否合法
            if (!IsValidUsernameChar(c))
            {
                isValid = false;
                break;
            }

            nameLength++;
            nameWidth += char.IsLetterOrDigit(c) ? 1 : 2;

            if (nameWidth > 10) // 总等效长度限制为 10
            {
                isValid = false;
                break;
            }

            Advance();
        }

        // 验证有效性：必须有用户名，且后跟空格
        if (isValid && nameLength > 0 && _pos < _input.Length && Peek() == ' ')
        {
            var username = _input[nameStart.._pos];
            Advance(); // 消费空格
            return new(TokenType.At, username, start);
        }

        // 无效情况：回退，将@作为普通文本处理
        // 将_pos重置到start + 1，然后返回一个Text token
        _pos = start + 1;
        return new(TokenType.Text, "@", start);
    }


    private char Peek()
    {
        return _pos < _input.Length ? _input[_pos] : '\0';
    }

    private char Advance()
    {
        return _input[_pos++];
    }

    // 修改判定逻辑：= 是否作为分隔符取决于当前上下文
    private bool IsTagDelimiter(char c)
    {
        // ] , 和 结束符 永远是分隔符
        if (c == ']' || c == ',' || c == '\0') return true;

        // 只有在寻找 TagName 的阶段，= 才是分隔符
        if (c == '=' && (_lastType == TokenType.LeftBracket || _lastType == TokenType.Slash)) return true;

        return false;
    }

    #region 辅助方法

    /// <summary>
    ///     判断字符是否为合法的用户名组成字符
    ///     包括：汉字（CJK统一表意文字）、字母、数字
    /// </summary>
    private static bool IsValidUsernameChar(char c)
    {
        // 字母或数字
        if (char.IsLetterOrDigit(c))
            return true;

        // CJK统一表意文字范围（基本汉字）
        // 包括日文、韩文等东亚文字
        if ((c >= 0x4E00 && c <= 0x9FFF) || // CJK统一表意文字
            (c >= 0x3400 && c <= 0x4DBF) || // CJK扩展A
            (c >= 0x20000 && c <= 0x2A6DF)) // CJK扩展B
            return true;

        return false;
    }

    #endregion
}