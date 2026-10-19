using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using CC98.Objects;
using CC98.Services.Helpers;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

using Symbol = FluentIcons.Common.Symbol;

namespace CC98.Controls.ComposeSheet;

/// <summary>
///     发帖浮层里的编辑器界面：状态栏 + 工具条 + 源码/预览两栏。
/// </summary>
/// <remarks>
///     目前只有界面：格式按钮是空壳（<see cref="Format_Click" /> 里按 Tag 分派的位置已经留好），
///     预览栏也没有接渲染。真正能用的只有纯 UI 行为——UBB/MD 切换、编辑/分屏/预览、字数统计、匿名。
///     工具条图标取自 FluentIcons，集合与 SketchPage 支持的输入一致；UBB 没有标题标签，所以没有 H1/H2。
/// </remarks>
public sealed partial class ComposeEditor : UserControl
{
    private const double PaneMinWidth = 260;

    /// <summary>哔哩哔哩的 MD 是没有原生语法的，用官方播放器 iframe。</summary>
    private const string BiliIframe =
        "<iframe src=\"//player.bilibili.com/player.html?bvid=BV1xx411c7mD\" scrolling=\"no\" border=\"0\" " +
        "frameborder=\"no\" framespacing=\"0\" allowfullscreen=\"true\"></iframe>";

    /// <summary>插 iframe 时选中的样例 BV 号，直接打字即可替换。</summary>
    private const string BiliSampleId = "BV1xx411c7mD";

    private bool _initialized;

    /// <summary>正在按 Tag 回写选中项，此时不要再响应 SelectionChanged（避免递归）。</summary>
    private bool _syncing;

    private int _contentType = 0; // 0 = Ubb, 1 = Markdown
    private string _mode = "split";

    public ComposeEditor()
    {
        InitializeComponent();

        // XAML 解析阶段就会把 SelectionChanged 打出来，那时可视树还没建全；
        // 另外 Segmented 加载后还会补一次选中，所以最后在 Loaded 里强制同步一次
        _initialized = true;
        ApplyMode(_mode);
        ApplyLanguage(_contentType);
        Loaded += (_, _) =>
        {
            ApplyMode(_mode);
            ApplyLanguage(_contentType);
        };
    }

    /// <summary>标题栏显示的文字，例如「回复主题」「编辑帖子」。</summary>
    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    /// <summary>
    ///     当前选中的语言，<c>ubb</c> 或 <c>md</c>。
    ///     注意别叫 <c>Language</c>：那是 <see cref="FrameworkElement.Language" />，会撞名（CS0108）。
    /// </summary>
    public int ContentType
    {
        get => _contentType;
        set => ApplyLanguage(value);
    }

    /// <summary>当前视图模式：<c>edit</c> / <c>split</c> / <c>preview</c>。</summary>
    public string Mode
    {
        get => _mode;
        set => ApplyMode(value);
    }

    /// <summary>是否匿名发表。</summary>
    public bool IsAnonymous
    {
        get => AnonCheck.IsChecked == true;
        set => AnonCheck.IsChecked = value;
    }

    /// <summary>回复时是否通知所有回复者。默认不通知。</summary>
    public bool NotifyAllReplier { get; set; } = false;

    /// <summary>编辑帖子时是否通知被回复者。默认通知。</summary>
    public bool NotifyPoster { get; set; } = true;

    /// <summary>发主题帖时的帖子类型。目前只接了回帖与编辑，先留着给发主题用。</summary>
    public int TopicTypeValue { get; set; } = (int)TopicType.Normal;

    /// <summary>
    ///     源码内容。别叫 <c>Content</c>：<see cref="UserControl" /> 自带一个 Content，
    ///     同名会让 XAML 编译器在解析根元素时拿到 string，报 WMC9999 那种看不懂的强制转换错误。
    /// </summary>
    public string ContentText
    {
        get => EditorBox.Text;
        set => EditorBox.Text = value ?? string.Empty;
    }

    /// <summary>帖子标题。只有编辑帖子/主题时才有意义。</summary>
    public string ContentTitle
    {
        get => TitleBox.Text?.Trim() ?? "";
        set => TitleBox.Text = value ?? "";
    }

    /// <summary>让源码框拿到焦点（浮层滑出后调用）。</summary>
    public void FocusInput()
    {
        EditorBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    ///     设置标题行：<paramref name="visible" /> 只控制「能不能改」。
    /// </summary>
    /// <remarks>
    ///     值始终会写进去，即使行是收起的 —— 编辑回复也要把主题原标题原样回传
    ///     （服务端拿这个字段覆盖主题标题，传空串会把标题清掉），
    ///     只是回复的界面不给改而已。
    ///     回复模式传 <c>null</c> 会清空，避免上一次编辑的标题串进新回复。
    /// </remarks>
    public void SetTitleEditing(bool visible, string? title)
    {
        TitleRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ContentTitle = title ?? string.Empty;
    }

    /// <summary>
    ///     匿名勾选框能不能改。编辑模式下置灰：<c>EditPostRequest</c> 里没有 IsAnonymous 字段，
    ///     服务端不支持改匿名，留个能点的框只会误导。
    /// </summary>
    public void SetAnonymousEditable(bool editable)
    {
        AnonCheck.IsEnabled = editable;
        ToolTipService.SetToolTip(AnonCheck, editable ? null : "编辑时无法修改匿名状态");
    }

    /// <summary>清空内容（发送成功后调用，下次打开是干净的）。</summary>
    public void Reset()
    {
        EditorBox.Text = string.Empty;
        SetTitleEditing(false, null);

        // TextChanged 里的预览刷新是 300ms 防抖，这里必须立刻清，
        // 否则预览栏还会挂着上一次的排版，下次打开先闪一下再消失。
        ClearPreview();
    }

    /// <summary>立刻把预览栏恢复成空态并丢掉已渲染的内容。</summary>
    public void ClearPreview()
    {
        _previewTimer?.Stop();

        PreviewEmpty.Visibility = Visibility.Visible;
        PreviewScroller.Visibility = Visibility.Collapsed;
        UbbViewer.UbbText = string.Empty;
        MdViewer.MarkdownText = string.Empty;
    }

    /// <summary>按当前文本立刻重绘预览（不等防抖）。浮层重新打开、或刚清过预览时用。</summary>
    public void RefreshPreview()
    {
        _previewTimer?.Stop();
        RenderPreview();
    }

    /// <summary>
    ///     预填引用块（回帖）。
    /// </summary>
    /// <remarks>
    ///     引用串本身是 UBB 语法，所以语言强制切到 UBB —— 不管被引用的帖子是 UBB 还是 Markdown
    ///     （SketchPage 也是这么干的：直接把 UBB 引用串塞进编辑器）。
    ///     末尾补一个换行、光标落到最底部，让用户接着写自己的话，别让他手动把光标挪出去。
    /// </remarks>
    public void PrefillQuote(string quote)
    {
        ContentType = (int)MarkupContentType.Ubb;

        var text = quote ?? string.Empty;
        if (!text.EndsWith('\n')) text += Environment.NewLine;

        ContentText = text;

        EditorBox.SelectionStart = EditorBox.Text.Length;
        EditorBox.SelectionLength = 0;
    }

    /// <summary>弹一条临时提示，外层用它上报发送结果。</summary>
    public void Notify(FlowStatus status, string text)
    {
        Flower.Play(status, text);
    }

    #region 状态栏

    private void LangSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;

        // 读"这次新选中的那一项"的 Tag，比读 SelectedIndex 可靠
        //（工具包的 Segmented 自己也会改选中项，索引未必是稳定顺序）
        if (SelectedTag(e) is { } language) ApplyLanguage(language == "md" ? (int)MarkupContentType.Markdown : (int)MarkupContentType.Ubb);
    }

    private void ApplyLanguage(int contentType)
    {
        _contentType = contentType;

        var ubb = contentType == (int)MarkupContentType.Ubb;
        SyncSelection(LangSegmented, ubb ? "ubb" : "md");

        PaneLabelText.Text = ubb ? "UBB 源码" : "Markdown 源码";
        EditorBox.PlaceholderText = ubb ? "在这里输入 UBB 内容…" : "在这里输入 Markdown 内容…";

        // 换语言要立刻换渲染控件，不用等防抖
        RenderPreview();
    }

    #endregion

    #region 工具条

    private void ModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;

        if (SelectedTag(e) is { } mode) ApplyMode(mode);
    }

    private void ApplyMode(string mode)
    {
        _mode = mode;

        var edit = mode != "preview";
        var preview = mode != "edit";
        var split = mode == "split";

        SyncSelection(ModeSegmented, split ? "split" : mode == "edit" ? "edit" : "preview");

        EditPane.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        PreviewPane.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        BodySplitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;

        // 收起来的那一栏列宽必须真的给 0，光把子元素 Collapsed 列还是会占位；
        // MinWidth 也要跟着放开，否则 0 宽的列会被 260 的下限顶回去
        EditColumn.Width = edit ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        EditColumn.MinWidth = edit ? PaneMinWidth : 0;
        PreviewColumn.Width = preview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PreviewColumn.MinWidth = preview ? PaneMinWidth : 0;

        // 从"只看编辑"切回来时补一次渲染（编辑模式下 TextChanged 不渲染）
        if (preview) RenderPreview();
    }

    /// <summary>表情面板：HyperlinkButton 没有 Flyout 属性，改用附加属性手动弹出。</summary>
    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element) FlyoutBase.ShowAttachedFlyout(element);
    }

    /// <summary>
    ///     格式按钮的统一入口：按 <c>Tag</c> 决定插什么语法，UBB 与 Markdown 各一套映射
    ///     （对应关系与 <c>SketchPage.AppBarButton_Click</c> 一致）。
    /// </summary>
    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;

        // 媒体四件套要先弹浮层选「上传 / 只插标签 / 自定义 URL」，不在这里直接插
        if (tag is "img" or "video" or "audio" or "upload")
        {
            ShowMediaFlyout(sender as FrameworkElement, tag);
            return;
        }

        var md = _contentType == (int)MarkupContentType.Markdown;
        var text = EditorBox.Text;
        var start = EditorBox.SelectionStart;
        var length = EditorBox.SelectionLength;

        MarkupEditing.Result? result = tag switch
        {
            // 行内：有选中就包住，没选中插占位符
            "bold" => md ? Wrap("**", "**", "粗体文本") : Wrap("[b]", "[/b]", ""),
            "italic" => md ? Wrap("*", "*", "斜体文本") : Wrap("[i]", "[/i]", ""),
            "underline" => md ? Wrap("<u>", "</u>", "下划线文本") : Wrap("[u]", "[/u]", ""),
            "strike" => md ? Wrap("~~", "~~", "删除线文本") : Wrap("[del]", "[/del]", ""),

            // 对齐（块级）
            "align-left" => md
                ? MarkupEditing.Block(text, start, length, "<div align=\"left\">\n", "\n</div>", "文本")
                : MarkupEditing.Block(text, start, length, "[align=left]", "[/align]", ""),
            "align-center" => md
                ? MarkupEditing.Block(text, start, length, "<div align=\"center\">\n", "\n</div>", "文本")
                : MarkupEditing.Block(text, start, length, "[align=center]", "[/align]", ""),
            "align-right" => md
                ? MarkupEditing.Block(text, start, length, "<div align=\"right\">\n", "\n</div>", "文本")
                : MarkupEditing.Block(text, start, length, "[align=right]", "[/align]", ""),

            // 块
            "quote" => md ? MarkupEditing.QuoteMd(text, start, length) : Wrap("[quote]", "[/quote]", ""),
            "code" => md
                ? MarkupEditing.Block(text, start, length, "```\n", "\n```", "code")
                : Wrap("[code]", "[/code]", ""),
            "link" => md
                ? MarkupEditing.LinkMd(text, start, length)
                : MarkupEditing.LinkUbb(text, start, length),

            // 插入
            "line" => md
                ? MarkupEditing.BlockLiteral(text, start, length, "---")
                : MarkupEditing.Literal(text, start, length, "[line]"),
            "bili" => md
                ? MarkupEditing.LiteralSelecting(text, start, length, BiliIframe, BiliSampleId)
                : Wrap("[bili]", "[/bili]", ""),
            // 媒体四件套在方法开头就转去弹浮层了
            "img" or "video" or "audio" or "upload" => null,
            _ => null
        };

        if (result is not { } inserted) return;

        ApplyResult(inserted);
    }

    /// <summary>媒体标签：UBB 插空标签把光标停在里面；MD 插对应语法并选中 url 占位。传了 url 就直接写进去。</summary>
    private MarkupEditing.Result MediaTag(bool md, string text, int start, int length, string ubbTag, string label,
        string? url)
    {
        if (!md)
        {
            return url is null
                ? MarkupEditing.Wrap(text, start, length, $"[{ubbTag}]", $"[/{ubbTag}]", "")
                : MarkupEditing.Literal(text, start, length, $"[{ubbTag}]{url}[/{ubbTag}]");
        }

        var target = url ?? "url";
        var literal = ubbTag switch
        {
            "img" => url is null ? $"![{label}](url)" : $"![{label}]({target})",
            "video" => $"<video src=\"{target}\" controls></video>",
            "audio" => $"<audio src=\"{target}\" controls></audio>",
            _ => url is null ? $"[{label}](url)" : $"[{label}]({target})"
        };

        return url is null
            ? MarkupEditing.LiteralSelecting(text, start, length, literal, "url")
            : MarkupEditing.Literal(text, start, length, literal);
    }

    /// <summary>
    ///     媒体按钮的统一入口：弹一个小浮层，保留 SketchPage 那三条路
    ///     （上传本地文件 / 仅输入标签 / 使用自定义 URL），只是把 ContentDialog 换成了 Flyout。
    /// </summary>
    private void ShowMediaFlyout(FrameworkElement? anchor, string label)
    {
        if (anchor is null) return;

        var flyout = BuildMediaFlyout(label);
        FlyoutBase.SetAttachedFlyout(anchor, flyout);
        FlyoutBase.ShowAttachedFlyout(anchor);
    }

    private Flyout BuildMediaFlyout(string label)
    {
        var flyout = new Flyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        
        var confirmUrlButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE73E" },
        };

        // 自定义 URL 那一行默认收起，点第三条才展开
        var urlBox = new DevWinUI.TextBox
        {
            PlaceholderText = "粘贴或输入链接",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content=confirmUrlButton,
            Visibility=Visibility.Collapsed
        };
        //这是一个闭包函数。
        void InsertCustomUrl()
        {
            var url = urlBox.Text?.Trim();
            if (string.IsNullOrEmpty(url)) return;

            flyout.Hide();
            InsertMedia(label, url);
        }
        confirmUrlButton.Click += (_, _) => InsertCustomUrl();
        urlBox.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            InsertCustomUrl();
        };

        var panel = new StackPanel { Width = 260, Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = label switch { "img" => "插入图片", "video" => "插入视频", "audio" => "插入音频", _ => "插入附件" },
            FontSize = 12,
            Opacity = 0.7,
            Margin = new Thickness(8, 2, 8, 6)
        });

        var uploadRow = CreateMenuRow(Symbol.FolderAdd, "上传本地文件");
        uploadRow.Click += async (_, _) =>
        {
            flyout.Hide();
            await UploadAndInsertAsync(label);
        };
        panel.Children.Add(uploadRow);

        var tagOnlyRow = CreateMenuRow(Symbol.Code, "仅输入标签");
        tagOnlyRow.Click += (_, _) =>
        {
            flyout.Hide();
            InsertMedia(label, null);
        };
        panel.Children.Add(tagOnlyRow);

        var customRow = CreateMenuRow(Symbol.Link, "使用自定义 URL");
        customRow.Click += (_, _) =>
        {
            urlBox.Visibility = Visibility.Visible;
            urlBox.Focus(FocusState.Programmatic);
        };
        panel.Children.Add(customRow);
        panel.Children.Add(urlBox);

        flyout.Content = panel;
        return flyout;
    }

    /// <summary>菜单行：图标 + 文字，铺满宽度左对齐，透明底只留悬停高亮。</summary>
    private static Button CreateMenuRow(Symbol glyph, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(new FluentIcons.WinUI.SymbolIcon { Symbol = glyph, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

        return new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 7, 8, 7),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
    }

    /// <summary>在光标处插入媒体标签；<paramref name="url" /> 为空表示只插标签（光标留在里面）。</summary>
    private void InsertMedia(string label, string? url)
    {
        var md = _contentType == (int)MarkupContentType.Markdown;

        ApplyResult(MediaTag(md, EditorBox.Text, EditorBox.SelectionStart, EditorBox.SelectionLength,
            label, LabelDisplay(label), url));
    }

    private static string LabelDisplay(string label) => label switch
    {
        "img" => "图片",
        "video" => "视频",
        "audio" => "音频",
        _ => "下载"
    };

    /// <summary>选本地文件上传，成功后把服务端返回的 URL 插进编辑框。</summary>
    private async Task UploadAndInsertAsync(string label)
    {
        var result = await FileUploadService.PickAndUploadAsync(XamlRoot, label);

        // 用户取消选择时 Success=false 且 Error 为空，不提示
        if (!result.Success)
        {
            if (!string.IsNullOrEmpty(result.Error)) Flower.Play(FlowStatus.Fail, $"上传失败：{result.Error}");
            return;
        }

        InsertMedia(label, result.Url);
        Flower.Play(FlowStatus.Success, "已插入");
    }

    /// <summary>
    ///     直接粘贴（Ctrl+V）：剪贴板里是文件或图片时先上传再插标签；
    ///     普通文本不拦截，交给默认粘贴。
    /// </summary>
    private async void EditorBox_Paste(object sender, TextControlPasteEventArgs e)
    {
        try
        {
            var content = Clipboard.GetContent();
            var hasFiles = content.Contains(StandardDataFormats.StorageItems);
            var hasBitmap = !hasFiles && content.Contains(StandardDataFormats.Bitmap);
            if (!hasFiles && !hasBitmap) return;

            // 必须在第一个 await 之前置位：async void 一遇到 await 就返回，
            // 那时若还没置位，框架会认为未处理而照常执行默认粘贴。
            e.Handled = true;

            await RunUploadBusyAsync(async () =>
            {
                if (hasFiles) await UploadPastedFilesAsync(await content.GetStorageItemsAsync());
                else await UploadPastedBitmapAsync(await content.GetBitmapAsync());
            });
        }
        catch (Exception ex)
        {
            // async void 必须兜底，否则异常会终止进程
            Flower.Play(FlowStatus.Fail, $"粘贴上传失败：{ex.Message}");
        }
    }

    /// <summary>上传期间禁止编辑，避免重复粘贴触发并发上传。用 IsReadOnly 而不是 IsEnabled=false。</summary>
    private async Task RunUploadBusyAsync(Func<Task> upload)
    {
        var wasReadOnly = EditorBox.IsReadOnly;
        try
        {
            EditorBox.IsReadOnly = true;
            await upload();
        }
        finally
        {
            EditorBox.IsReadOnly = wasReadOnly;
        }
    }

    private async Task UploadPastedFilesAsync(IReadOnlyList<IStorageItem> items)
    {
        var files = items.OfType<StorageFile>().ToList();
        if (files.Count == 0) return;

        var uploaded = 0;
        foreach (var file in files)
        {
            var result = await FileUploadService.UploadAsync(file);
            if (!result.Success)
            {
                if (!string.IsNullOrEmpty(result.Error)) Flower.Play(FlowStatus.Fail, $"上传失败：{result.Error}");
                continue;
            }

            InsertMedia(FileUploadService.GetLabelForFileName(file.Name), result.Url);
            uploaded++;
        }

        if (uploaded > 0) Flower.Play(FlowStatus.Success, $"已上传 {uploaded} 个文件");
    }

    private async Task UploadPastedBitmapAsync(RandomAccessStreamReference bitmap)
    {
        var result = await FileUploadService.UploadClipboardBitmapAsync(bitmap);
        if (!result.Success)
        {
            if (!string.IsNullOrEmpty(result.Error)) Flower.Play(FlowStatus.Fail, $"上传失败：{result.Error}");
            return;
        }

        InsertMedia("img", result.Url);
        Flower.Play(FlowStatus.Success, string.IsNullOrEmpty(result.Note) ? "图片已上传" : result.Note);
    }

    /// <summary>打开取色浮层。</summary>
    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element) FlyoutBase.ShowAttachedFlyout(element);
    }

    /// <summary>取色器里点「插入」：按当前语言插 <c>[color=#…]</c> 或 <c>&lt;span style&gt;</c>。</summary>
    private void InsertColor_Click(object sender, RoutedEventArgs e)
    {
        ColorFlyout.Hide();

        var color = ColorPickerBox.Color;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var text = EditorBox.Text;
        var start = EditorBox.SelectionStart;
        var length = EditorBox.SelectionLength;

        var result = _contentType == (int)MarkupContentType.Markdown
            ? MarkupEditing.Wrap(text, start, length, $"<span style=\"color:{hex}\">", "</span>", "彩色文本")
            : MarkupEditing.Wrap(text, start, length, $"[color={hex}]", "[/color]", "");

        ApplyResult(result);
    }

    /// <summary>把插入结果写回编辑框并恢复选区。</summary>
    private void ApplyResult(MarkupEditing.Result result)
    {
        EditorBox.Text = result.Text;
        EditorBox.SelectionStart = result.SelectionStart;
        EditorBox.SelectionLength = result.SelectionLength;
        EditorBox.Focus(FocusState.Programmatic);
    }

    /// <summary>按当前选区做行内包裹。</summary>
    private MarkupEditing.Result Wrap(string open, string close, string placeholder)
    {
        return MarkupEditing.Wrap(EditorBox.Text, EditorBox.SelectionStart, EditorBox.SelectionLength,
            open, close, placeholder);
    }

    #endregion

    #region 编辑区

    private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        CharCountText.Text = $"{EditorBox.Text.Length} 字";
        SchedulePreview();
    }

    #region 预览

    /// <summary>预览刷新防抖：连打时不要每个键都全量重渲染。</summary>
    private const int PreviewDebounceMs = 300;

    /// <summary>定时器必须存字段：没有别的强引用，被 GC 回收后 Tick 就不再触发。</summary>
    private DispatcherQueueTimer? _previewTimer;

    private void SchedulePreview()
    {
        // 编辑模式/收起预览时不必渲染，切回来时 ApplyMode 会补一次
        if (_mode == "edit") return;

        _previewTimer ??= CreatePreviewTimer();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private DispatcherQueueTimer CreatePreviewTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(PreviewDebounceMs);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RenderPreview();
        };
        return timer;
    }

    /// <summary>把源码丢给对应的渲染控件；内容为空就只显示占位。</summary>
    private void RenderPreview()
    {
        var text = EditorBox.Text;
        var hasText = !string.IsNullOrWhiteSpace(text);

        PreviewEmpty.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        PreviewScroller.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

        if (!hasText)
        {
            // 清空时把内容也丢掉，避免下次进来先闪一下旧内容
            UbbViewer.UbbText = string.Empty;
            MdViewer.MarkdownText = string.Empty;
            return;
        }

        var md = _contentType == (int)MarkupContentType.Markdown;
        UbbViewer.Visibility = md ? Visibility.Collapsed : Visibility.Visible;
        MdViewer.Visibility = md ? Visibility.Visible : Visibility.Collapsed;

        if (md) MdViewer.MarkdownText = text;
        else UbbViewer.UbbText = text;
    }

    #endregion

    /// <summary>取本次新增选中项的 Tag（没有就返回 null）。</summary>
    private static string? SelectedTag(SelectionChangedEventArgs e)
    {
        return e.AddedItems.Count > 0 && e.AddedItems[0] is SegmentedItem item
            ? item.Tag as string
            : null;
    }

    /// <summary>回写选中项（事件驱动同步时跳过，避免自己触发自己）。</summary>
    private void SyncSelection(Segmented segmented, string tag)
    {
        if (_syncing) return;

        _syncing = true;
        foreach (var candidate in segmented.Items)
            if (candidate is SegmentedItem item)
                item.IsSelected = (item.Tag as string) == tag;
        _syncing = false;
    }

    #endregion
}
