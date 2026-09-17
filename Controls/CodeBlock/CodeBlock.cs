using System;
using System.Globalization;
using ColorCode;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CC98.Controls;

/// <summary>
///     代码块控件:基于 ColorCode 做语法高亮,等宽字体 + 可横向滚动,背景色由外部注入。
/// </summary>
/// <remarks>
///     滚动行为依赖控件模板的结构(参考 CC98\Controls\CodeBlock):ScrollViewer 放在 Grid 的 * 行内、
///     且不显式设置 ScrollMode,此时它的视口高度与内容一致、纵向没有可滚动范围,
///     纵向滚轮会自然交给外层 ScrollViewer,无需任何滚轮转发代码。
/// </remarks>
public sealed partial class CodeBlock : Control
{
    private const string ViewerPartName = "PART_Viewer";
    private const string LanguageTagPartName = "PART_LanguageTag";
    private const string ScrollerPartName = "PART_Scroller";

    /// <summary>
    ///     滚轮转发步长:WinUI ScrollViewer 的原生步长为固定 3 行 = 48 DIP/格(与字号无关)。
    ///     转发时必须用同一数值,否则鼠标停在代码块上时的滚动速度会与其它区域不一致。
    /// </summary>
    private const double WheelStepPerNotch = 48.0;

    private TextBlock? _languageTag;
    private RichTextBlock? _viewer;
    private ScrollViewer? _scroller;
    private bool _wheelHandlerAttached;

    public CodeBlock()
    {
        DefaultStyleKey = typeof(CodeBlock);
    }

    /// <summary>
    ///     调试用:输出内部 ScrollViewer 的视口与可滚动范围,用于排查滚动相关问题。
    /// </summary>
    public string GetScrollDebugInfo()
    {
        if (_scroller is null) return "(无 ScrollViewer)";

        return $"viewport={_scroller.ViewportWidth:0}x{_scroller.ViewportHeight:0} " +
               $"content={_scroller.ExtentWidth:0}x{_scroller.ExtentHeight:0} " +
               $"scrollable={_scroller.ScrollableWidth:0}x{_scroller.ScrollableHeight:0}";
    }

    #region 依赖属性

    public static readonly DependencyProperty CodeProperty =
        DependencyProperty.Register(
            nameof(Code),
            typeof(string),
            typeof(CodeBlock),
            new(string.Empty, OnCodeChanged));

    public static readonly DependencyProperty LanguageNameProperty =
        DependencyProperty.Register(
            nameof(LanguageName),
            typeof(string),
            typeof(CodeBlock),
            new("plaintext", OnLanguageChanged));

    /// <summary>代码文本。</summary>
    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>语言标识(单行 id,如 csharp / python / plaintext)。</summary>
    public string LanguageName
    {
        get => (string)GetValue(LanguageNameProperty);
        set => SetValue(LanguageNameProperty, value);
    }

    #endregion

    #region 生命周期

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _viewer = GetTemplateChild(ViewerPartName) as RichTextBlock;
        _languageTag = GetTemplateChild(LanguageTagPartName) as TextBlock;
        _scroller = GetTemplateChild(ScrollerPartName) as ScrollViewer;

        if (!_wheelHandlerAttached)
        {
            AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
            _wheelHandlerAttached = true;
        }

        RenderCode();
    }

    /// <summary>
    ///     处理"代码块吞掉纵向滚轮"的 WinUI 已知行为(参见 microsoft-ui-xaml#10172):
    ///     嵌套 ScrollViewer 只要**横向有可滚动内容**,就会消费鼠标纵向滚轮且不再向外传递。
    ///     因此仅在"横向确实溢出、且纵向无需自身滚动"时,按外层 ScrollViewer 的原生步长与动画补一次转发;
    ///     其余情况一律不干预,滚轮由框架原样交给外层。
    /// </summary>
    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;

        // 横向滚轮(Shift + 滚轮 / 倾斜滚轮)属于代码块自身,直接放行
        if (properties.IsHorizontalMouseWheel) return;

        // 没有横向溢出 → 框架会正常把滚轮交给外层;纵向还能滚 → 让代码块自己滚
        if (_scroller is null || _scroller.ScrollableWidth <= 0 || _scroller.ScrollableHeight > 0) return;

        var ancestor = FindAncestorScrollViewer();
        if (ancestor is null) return;

        var notches = properties.MouseWheelDelta / 120.0;
        ancestor.ChangeView(null, ancestor.VerticalOffset - notches * WheelStepPerNotch, null);
    }

    private ScrollViewer? FindAncestorScrollViewer()
    {
        var parent = VisualTreeHelper.GetParent(this);
        while (parent is not null)
        {
            if (parent is ScrollViewer scrollViewer) return scrollViewer;
            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CodeBlock codeBlock) codeBlock.RenderCode();
    }

    private static void OnLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CodeBlock codeBlock) codeBlock.RenderCode();
    }

    #endregion

    #region 渲染

    private void RenderCode()
    {
        if (_viewer is null) return;

        var languageName = string.IsNullOrWhiteSpace(LanguageName) ? "plaintext" : LanguageName.Trim();
        var language = languageName.Equals("plaintext", StringComparison.OrdinalIgnoreCase)
            ? null
            : Languages.FindById(languageName);

        if (_languageTag is not null)
        {
            var displayName = language is null ? string.Empty : GetLanguageDisplayName(language, languageName);
            _languageTag.Text = displayName;
            _languageTag.Visibility = string.IsNullOrEmpty(displayName) ? Visibility.Collapsed : Visibility.Visible;
        }

        if (language is null)
        {
            // 未知语言 / 纯文本:等宽字体直出,不做高亮
            _viewer.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run { Text = Code ?? string.Empty });
            _viewer.Blocks.Add(paragraph);
            return;
        }

        var formatter = new RichTextBlockFormatter();
        formatter.FormatRichTextBlock(Code ?? string.Empty, language, _viewer);
    }

    private static string GetLanguageDisplayName(ILanguage language, string languageName)
    {
        if (!string.IsNullOrEmpty(language.Name)) return language.Name;

        return languageName.ToLowerInvariant() switch
        {
            "cpp" or "c++" => "C++",
            "csharp" or "cs" => "C#",
            "javascript" or "js" => "JavaScript",
            "typescript" or "ts" => "TypeScript",
            "python" or "py" => "Python",
            "java" => "Java",
            "html" => "HTML",
            "css" => "CSS",
            "sql" => "SQL",
            "php" => "PHP",
            "ruby" or "rb" => "Ruby",
            "go" => "Go",
            "rust" => "Rust",
            "swift" => "Swift",
            "kotlin" => "Kotlin",
            "dart" => "Dart",
            "markdown" or "md" => "Markdown",
            "yaml" or "yml" => "YAML",
            "json" => "JSON",
            "xml" => "XML",
            "bash" or "sh" => "Bash",
            "powershell" or "ps" => "PowerShell",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(languageName)
        };
    }

    #endregion
}
