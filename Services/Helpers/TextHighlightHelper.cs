using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using CC98.Objects;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.UI;
using Windows.UI.Text;

namespace CC98.Services.Helpers;
/// <summary>
/// 提供了对普通文本框高亮的拓展方法，也适用于版面页的高亮
/// </summary>
public static class TextBlockExtensions
{
    public static readonly DependencyProperty HighlightedTextProperty =
        DependencyProperty.RegisterAttached(
            "HighlightedText",
            typeof(string),
            typeof(TextBlockExtensions),
            new PropertyMetadata(null, OnHighlightedTextChanged));

    public static string GetHighlightedText(TextBlock obj)
    {
        return (string)obj.GetValue(HighlightedTextProperty);
    }

    public static void SetHighlightedText(TextBlock obj, string value)
    {
        obj.SetValue(HighlightedTextProperty, value);
    }

    /// <summary>
    ///     高亮信息(CC98 的 HighlightInfo:Color / IsBold / IsItalic)。
    ///     **本属性为 null 时整条高亮逻辑不生效**,TextBlock 退化为原生行为(只赋 Text)。
    ///     用对象整体传入而不是拆成多个附加属性,一是让"是否高亮"有唯一判据,
    ///     二是把每个标题要触发的重算次数从 6 次降到 1 次。
    /// </summary>
    public static readonly DependencyProperty HighlightInfoProperty =
        DependencyProperty.RegisterAttached(
            "HighlightInfo",
            typeof(object),
            typeof(TextBlockExtensions),
            new PropertyMetadata(null, OnHighlightedTextChanged));

    public static object? GetHighlightInfo(TextBlock obj) => obj.GetValue(HighlightInfoProperty);

    public static void SetHighlightInfo(TextBlock obj, object? value) => obj.SetValue(HighlightInfoProperty, value);

    // 可选的：高亮颜色
    // 注意:必须挂回调。元素被列表回收复用时,新条目会重设本属性(常为 null);
    // 若无回调,最后一次重算可能仍使用上一个条目残留的颜色,导致无 HighlightInfo
    // 的帖子"随机"带上别的帖子的高亮色。
    public static readonly DependencyProperty HighlightColorProperty =
        DependencyProperty.RegisterAttached(
            "HighlightColor",
            typeof(string),
            typeof(TextBlockExtensions),
            new PropertyMetadata("", OnHighlightedTextChanged));
    public static readonly DependencyProperty BoldFontFamilyProperty =
    DependencyProperty.RegisterAttached(
        "BoldFontFamily",
        typeof(FontFamily),
        typeof(TextBlockExtensions),
        new PropertyMetadata(null, OnHighlightedTextChanged));
    public static string GetHighlightColor(TextBlock obj)
    {
        return (string)obj.GetValue(HighlightColorProperty);
    }

    public static void SetHighlightColor(TextBlock obj, string value)
    {
        obj.SetValue(HighlightColorProperty, value);
    }

    // 可选的：关键词列表
    public static readonly DependencyProperty KeywordsProperty =
        DependencyProperty.RegisterAttached(
            "Keywords",
            typeof(string),
            typeof(TextBlockExtensions),
            new PropertyMetadata(null, OnKeywordsChanged));

    public static readonly DependencyProperty IsBoldProperty =
            DependencyProperty.RegisterAttached(
                "IsBold",
                typeof(bool),
                typeof(TextBlockExtensions),
                new PropertyMetadata(false, OnHighlightedTextChanged));

    public static readonly DependencyProperty IsItalicProperty =
        DependencyProperty.RegisterAttached(
            "IsItalic",
            typeof(bool),
            typeof(TextBlockExtensions),
            new PropertyMetadata(false, OnHighlightedTextChanged));
    public static string GetKeywords(TextBlock obj)
    {
        return (string)obj.GetValue(KeywordsProperty);
    }

    public static void SetKeywords(TextBlock obj, string value)
    {
        obj.SetValue(KeywordsProperty, value);
    }
    public static bool GetIsBold(TextBlock obj) => (bool)obj.GetValue(IsBoldProperty);
    public static void SetIsBold(TextBlock obj, bool value) => obj.SetValue(IsBoldProperty, value);

    public static bool GetIsItalic(TextBlock obj) => (bool)obj.GetValue(IsItalicProperty);
    public static void SetIsItalic(TextBlock obj, bool value) => obj.SetValue(IsItalicProperty, value);
    public static FontFamily GetBoldFontFamily(TextBlock obj) => (FontFamily)obj.GetValue(BoldFontFamilyProperty);
    public static void SetBoldFontFamily(TextBlock obj, FontFamily value) => obj.SetValue(BoldFontFamilyProperty, value);

    private static void OnHighlightedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            UpdateHighlightedText(textBlock);
        }
    }

    private static void OnKeywordsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            UpdateHighlightedText(textBlock);
        }
    }

    private static void UpdateHighlightedText(TextBlock textBlock)
    {
        var text = GetHighlightedText(textBlock);
        var keywordsStr = GetKeywords(textBlock);
        var info = GetHighlightInfo(textBlock) as HighlightInfo;

        if (string.IsNullOrEmpty(text))
        {
            textBlock.Inlines.Clear();
            textBlock.Inlines.Add(new Run { Text = "" });
            return;
        }

        // 既没有 HighlightInfo 也没有关键词 → 完全按原生 TextBlock 处理:
        // 直接赋 Text(Text 与 Inlines 互斥,等价于一个普通 Run),不构造 Run、不做正则匹配。
        // 版面页的普通主题行走的就是这条路径;幂等判断让重复回调(多个附加属性各触发一次)
        // 在第二次之后直接返回,不再产生任何原生文本重排。
        if (info is null && string.IsNullOrEmpty(keywordsStr))
        {
            if (textBlock.Inlines.Count == 0 && textBlock.Text == text) return;

            textBlock.Inlines.Clear();
            textBlock.Text = text;
            return;
        }

        // 样式来源:HighlightInfo 优先,缺省回退到旧的独立属性(搜索模板仍在用那一套)
        var colorStr = string.IsNullOrEmpty(info?.Color) ? GetHighlightColor(textBlock) : info!.Color;
        var isBold = info?.IsBold ?? GetIsBold(textBlock);
        var isItalic = info?.IsItalic ?? GetIsItalic(textBlock);
        var boldFontFamily = GetBoldFontFamily(textBlock);

        // 未指定高亮颜色时 highlightBrush 为 null：高亮 Run 不覆盖前景色，
        // 从而继承 TextBlock 的 ThemeResource(主题感知)前景色，避免深色模式下黑字不可读。
        SolidColorBrush? highlightBrush = null;
        if (!string.IsNullOrEmpty(colorStr))
        {
            highlightBrush = new SolidColorBrush(ParseColor(colorStr));
        }

        textBlock.Inlines.Clear();

        // 有 HighlightInfo 但没有关键词:整段套用该样式。
        // CC98 的 HighlightInfo 语义本来就是"整条标题命中关注关键词",无需再做关键词切分,
        // 因此这里省掉了每行一次 Regex 构造 + 逐段匹配。
        if (info is not null && string.IsNullOrEmpty(keywordsStr))
        {
            textBlock.Inlines.Add(CreateStyledRun(text, highlightBrush, isBold, isItalic, boldFontFamily, textBlock.FontFamily));
            return;
        }

        var keywords = keywordsStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var pattern = string.Join("|", keywords.Select(k => Regex.Escape(k)));
        // CultureInvariant 避免区域差异(如土耳其语 I)导致大小写匹配不一致。
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        int lastIndex = 0;
        foreach (Match match in regex.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                textBlock.Inlines.Add(CreatePlainRun(text[lastIndex..match.Index]));
            }

            textBlock.Inlines.Add(CreateStyledRun(match.Value, highlightBrush, isBold, isItalic, boldFontFamily, textBlock.FontFamily));

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            textBlock.Inlines.Add(CreatePlainRun(text[lastIndex..]));
        }
    }

    /// <summary>
    /// 创建一段完全继承 TextBlock 样式(含主题感知前景色)的普通纯文本 Run。
    /// </summary>
    private static Run CreatePlainRun(string text) => new() { Text = text };

    /// <summary>
    /// 创建一段高亮 Run，按 isBold/isItalic/boldFontFamily 统一应用字型；
    /// 前景色为 null 时不覆盖(继承 TextBlock 主题感知前景色)，避免冻结 ThemeResource 画刷。
    /// </summary>
    private static Run CreateStyledRun(string text, SolidColorBrush? foreground,
        bool isBold, bool isItalic, FontFamily? boldFontFamily, FontFamily baseFontFamily)
    {
        var run = new Run
        {
            Text = text,
            FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = isItalic ? FontStyle.Italic : FontStyle.Normal,
            FontFamily = isBold && boldFontFamily != null ? boldFontFamily : baseFontFamily
        };
        if (foreground is not null)
        {
            run.Foreground = foreground;
        }
        return run;
    }
    private static Color ParseColor(string colorStr)
    {
        try
        {
            // 1) 资源键:优先在应用资源中查找 Brush/Color(支持 {ThemeResource} 间接传入的资源名)
            if (Application.Current.Resources.TryGetValue(colorStr, out var res))
            {
                if (res is SolidColorBrush brush) return brush.Color;
                if (res is Color c) return c;
            }

            // 2) 系统强调色(规格默认高亮色)
            if (string.Equals(colorStr, "SystemAccentColor", StringComparison.OrdinalIgnoreCase))
            {
                return new Windows.UI.ViewManagement.UISettings()
                    .GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            }

            // 3) 十六进制 #RRGGBB / #AARRGGBB
            if (colorStr.StartsWith("#"))
            {
                if (colorStr.Length == 7)
                {
                    return ColorHelper.FromArgb(255,
                        Convert.ToByte(colorStr.Substring(1, 2), 16),
                        Convert.ToByte(colorStr.Substring(3, 2), 16),
                        Convert.ToByte(colorStr.Substring(5, 2), 16));
                }
                if (colorStr.Length == 9)
                {
                    return ColorHelper.FromArgb(
                        Convert.ToByte(colorStr.Substring(1, 2), 16),
                        Convert.ToByte(colorStr.Substring(3, 2), 16),
                        Convert.ToByte(colorStr.Substring(5, 2), 16),
                        Convert.ToByte(colorStr.Substring(7, 2), 16));
                }
            }

            // 4) 命名颜色(常见色,AOT 安全,无反射)
            if (NamedColors.TryGetValue(colorStr, out var named)) return named;

            return ColorHelper.FromArgb(255, 196, 171, 212);
        }
        catch
        {
            return ColorHelper.FromArgb(255, 196, 171, 212);
        }
    }

    /// <summary>
    /// 常见命名颜色映射(AOT 环境下不能反射访问 Colors 静态属性,故显式列出常用色)。
    /// </summary>
    private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Black"] = Colors.Black,
        ["White"] = Colors.White,
        ["Gray"] = Colors.Gray,
        ["DarkGray"] = Colors.DarkGray,
        ["LightGray"] = Colors.LightGray,
        ["Red"] = Colors.Red,
        ["DarkRed"] = Colors.DarkRed,
        ["Green"] = Colors.Green,
        ["DarkGreen"] = Colors.DarkGreen,
        ["Blue"] = Colors.Blue,
        ["DarkBlue"] = Colors.DarkBlue,
        ["Yellow"] = Colors.Yellow,
        ["Orange"] = Colors.Orange,
        ["Purple"] = Colors.Purple,
        ["Pink"] = Colors.Pink,
        ["Cyan"] = Colors.Cyan,
        ["Teal"] = Colors.Teal,
        ["Magenta"] = Colors.Magenta,
        ["Brown"] = Colors.Brown,
        ["Transparent"] = Colors.Transparent
    };
}