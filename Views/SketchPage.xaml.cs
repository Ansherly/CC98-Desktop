using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using CC98.Kernel;
using CC98.Objects;
using CC98.Services;
using CommunityToolkit.WinUI.Controls;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using CC98.Services.Extensions;
using CC98.Services.Helpers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SketchPage : Page
{
    private string Tail => AppSettings.Current.LittleTail;

    private string TextContent = "";

    //约定:可以在本页更改的环境量由以下字段表示，
    //而不可变参数由NavigationInfo传入。
    private int TextContentType; //UBB
    private string CurrentLabel = ""; //记录实时指令
    private List<Emoji> Emojis = [];
    // 表情分组缓存:悬停切换类型时避免重复扫描磁盘与重建整组 Image
    private readonly Dictionary<string, List<Emoji>> _emojiCache = [];
    private string _currentEmojiType = "";
    private bool IsAnonymous = false;
    private bool IsTailVisible;
    private bool NotifyAllReplier = false;
    private bool NotifyPoster = true;
    private int PostTypeValue; //普通帖子
    private ApiService ApiService = App.Current.GetService<ApiService>();
    public SketchPage()
    {
        InitializeComponent();
        if (App.Current.AppMainWindow is MainWindow mainwindow) mainwindow.NavigationView.IsPaneOpen = false;
        LoadEmojiSet("CC98");
    }

    public SketchNavigationInfo NavigationInfo { get; set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var args = e.TryGetParameter<SketchNavigationInfo>();
        if (args == null) return;
        NavigationInfo = args;
        ApplyEditorEnv();
    }

    #region 初始化环境

    private void LoadEmojiSet(string type)
    {
        //存在问题，如果使用xaml绑定,向下滚动时会崩溃。因此使用代码。
        //类型未变化(如悬停回当前类型)时直接跳过,避免重复扫描与重建
        if (_currentEmojiType == type && Emojis.Count > 0) return;
        _currentEmojiType = type;

        EmojiContainer.ItemsSource = null;
        Emojis.Clear();

        if (!_emojiCache.TryGetValue(type, out var cached))
        {
            try
            {
                var emojiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Emoji", type);
                var files = Directory.GetFiles(emojiPath, "*", SearchOption.TopDirectoryOnly);
                cached = files.Select(file =>
                {
                    var filename = Path.GetFileName(file);
                    return new Emoji { EmojiName = filename.Split(".")[0].ToLower(), EmojiPath = file };
                }).ToList();
                _emojiCache[type] = cached;
            }
            catch (Exception ex)
            {
                // 目录缺失/被改名:避免悬停事件中抛未捕获异常崩溃
                Debug.WriteLine($"加载表情失败: {ex.Message}");
                cached = [];
            }
        }

        Emojis.AddRange(cached);
        EmojiContainer.ItemsSource = Emojis;
    }

    //将编辑器模式应用到UI
    private void ApplyEditorEnv()
    {
        IsTailVisible = AppSettings.Current.IsTailVisible;
        TextContentType = NavigationInfo.ContentType;
        if (TextContentType == (int)ContentType.Markdown)
        {
            MdViewer.Visibility = Visibility.Visible;
            UbbViewer.Visibility = Visibility.Collapsed;
        }

        switch (NavigationInfo.EditorMode)
        {
            case EditorMode.ReplyToTopic:
                status.Text = $"回复主题:{NavigationInfo.HintText}";
                replyselector.IsSelected = true;
                //回复主题不需要设置标题和帖子类型
                SetTitle.IsEnabled = false;
                SetPostType.IsEnabled = false;
                break;
            case EditorMode.ReplyToPost:
                status.Text = NavigationInfo.HintText;
                Editor.Text = NavigationInfo.QuoteHeader;
                if (!Editor.Text.EndsWith(Environment.NewLine)) Editor.Text += Environment.NewLine;
                Editor.SelectionStart = Editor.Text.Length;
                replyselector.IsSelected = true;
                SetTitle.IsEnabled = false;
                SetPostType.IsEnabled = false;
                break;
            case EditorMode.DraftNewTopic:
                status.Text = "发表新主题";
                SetTitle.IsEnabled = true;
                topicselector.IsSelected = true;
                break;
            case EditorMode.EditMyPost:
                status.Text = $"编辑帖子:{NavigationInfo.HintText}";
                Editor.Text = NavigationInfo.BaseText;
                SetTitle.Text = NavigationInfo.HintText;
                if (!Editor.Text.EndsWith(Environment.NewLine)) Editor.Text += Environment.NewLine;
                Editor.SelectionStart = Editor.Text.Length;
                replyselector.IsSelected = true;
                //编辑非主题帖不允许修改标题和帖子类型
                SetTitle.IsEnabled = false;
                SetPostType.IsEnabled = false;
                break;
            case EditorMode.EditMyTopic:
                status.Text = $"编辑主题:{NavigationInfo.HintText}";
                Editor.Text = NavigationInfo.BaseText;
                SetTitle.Text = NavigationInfo.HintText;
                if (!Editor.Text.EndsWith(Environment.NewLine)) Editor.Text += Environment.NewLine;
                Editor.SelectionStart = Editor.Text.Length;
                topicselector.IsSelected = true;
                SetTitle.IsEnabled = true;
                SetPostType.IsEnabled = false;
                break;
        }

        //初始化内容,以免由于xaml加载顺序content为空。有时候textchanged事件不会立即触发。
        TextContent = Editor.Text.Replace("\r\n", "\n").Replace("\r", "\n");
        ApplyContentToViewer();
    }

    //渲染实时预览
    private void ApplyContentToViewer()
    {
        //关闭预览窗格可以避免卡顿，尤其是在内容较长时
        if (!EditArea.IsPaneOpen) return;
        if (TextContentType == (int)Objects.ContentType.Ubb)
            UbbViewer.UbbText = TextContent;
        else
            MdViewer.Text = TextContent;
    }

    #endregion


    #region 编辑器

    private async void AppBarButton_Click(object sender, RoutedEventArgs e)
    {
        var b = sender as AppBarButton;
        if (b == null) return;
        var isMd = TextContentType == (int)Objects.ContentType.Markdown;
        switch (b.Label)
        {
            case "预览":
                EditArea.IsPaneOpen = true;
                break;
            case "粗体":
                if (isMd) InsertMdSyntax("**", "**", "粗体文本");
                else InsertTag("b", "b", "");
                break;
            case "斜体":
                if (isMd) InsertMdSyntax("*", "*", "斜体文本");
                else InsertTag("i", "i", "");
                break;
            case "删除线":
                if (isMd) InsertMdSyntax("~~", "~~", "删除线文本");
                else InsertTag("del", "del", "");
                break;
            case "下划线":
                if (isMd) InsertMdSyntax("<u>", "</u>", "下划线文本");
                else InsertTag("u", "u", "");
                break;
            case "左对齐":
                if (isMd) InsertMdSyntax("<div align=\"left\">\n", "\n</div>", "文本", isBlock: true);
                else InsertTag("align=left", "align", "");
                break;
            case "居中":
                if (isMd) InsertMdSyntax("<div align=\"center\">\n", "\n</div>", "文本", isBlock: true);
                else InsertTag("align=center", "align", "");
                break;
            case "右对齐":
                if (isMd) InsertMdSyntax("<div align=\"right\">\n", "\n</div>", "文本", isBlock: true);
                else InsertTag("align=right", "align", "");
                break;
            case "引用":
                if (isMd) InsertMdQuote();
                else InsertTag("quote", "quote", "");
                break;
            case "代码":
                if (isMd) InsertMdSyntax("```\n", "\n```", "code", isBlock: true);
                else InsertTag("code", "code", "");
                break;
            case "链接":
                if (isMd) InsertMdLink();
                else InsertTag("url", "url", "");
                break;
            case "颜色":
                var r = await ShowDialogSafelyAsync(ColorPanel);
                if (r == ContentDialogResult.Primary)
                {
                    var colorwithalpha = Colors.Color.ToString().ToLower();
                    var color = string.Concat(colorwithalpha.AsSpan(0, 1), colorwithalpha.AsSpan(3, 6));
                    if (isMd) InsertMdSyntax($"<span style=\"color:#{color}\">", "</span>", "彩色文本");
                    else InsertTag("color=" + color, "color", "");
                }

                break;
            case "图片":
                CurrentLabel = "img";
                FileHelper.XamlRoot = XamlRoot;
                await ShowDialogSafelyAsync(FileHelper);
                break;
            case "视频":
                CurrentLabel = "video";
                FileHelper.XamlRoot = XamlRoot;
                await ShowDialogSafelyAsync(FileHelper);
                break;
            case "音频":
                CurrentLabel = "audio";
                FileHelper.XamlRoot = XamlRoot;
                await ShowDialogSafelyAsync(FileHelper);
                break;
            case "哔哩":
                // UBB 格式 [bili]BV号[/bili],中间是 BV 号
                if (isMd) InsertMdSyntax("<iframe src=\"//player.bilibili.com/player.html?bvid=", "\" scrolling=\"no\" border=\"0\" frameborder=\"no\" framespacing=\"0\" allowfullscreen=\"true\"></iframe>", "BV1xx411c7mD");
                else InsertTag("bili", "bili", "");
                break;
            case "文档":
                CurrentLabel = "upload";
                FileHelper.XamlRoot = XamlRoot;
                await ShowDialogSafelyAsync(FileHelper);
                break;
            case "分割线":
                if (isMd)
                {
                    var mdSelStart = Editor.SelectionStart;
                    Editor.Text = Editor.Text.Insert(mdSelStart, "\n---\n");
                    Editor.SelectionStart = mdSelStart + 4;
                    Editor.Focus(FocusState.Programmatic);
                }
                else
                {
                    var selectionStart = Editor.SelectionStart;
                    Editor.Text = Editor.Text.Insert(selectionStart, "[line]");
                    Editor.SelectionStart = selectionStart + 6;
                    Editor.Focus(FocusState.Programmatic);
                }
                break;
            case "贴图":
                // TODO: 实现贴图功能
                break;
        }
    }

    /// <summary>
    /// MD 模式插入引用:选中文本每行加 "> ",无选中时插入占位。
    /// </summary>
    private void InsertMdQuote()
    {
        var selStart = Editor.SelectionStart;
        var selLen = Editor.SelectionLength;
        if (selLen > 0)
        {
            var selected = Editor.Text.Substring(selStart, selLen);
            var quoted = "> " + selected.Replace("\n", "\n> ");
            Editor.Text = Editor.Text.Remove(selStart, selLen).Insert(selStart, quoted);
        }
        else
        {
            Editor.Text = Editor.Text.Insert(selStart, "\n> 引用文本\n");
            Editor.SelectionStart = selStart + 3;
        }
        Editor.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// MD 模式插入链接:插入 [链接文字](url),选中"链接文字"供直接输入。
    /// </summary>
    private void InsertMdLink()
    {
        const string placeholder = "链接文字";
        const string urlPlaceholder = "url";
        var md = $"[{placeholder}]({urlPlaceholder})";
        var selStart = Editor.SelectionStart;
        Editor.Text = Editor.Text.Insert(selStart, md);
        Editor.SelectionStart = selStart + 1; // 选中"链接文字"
        Editor.SelectionLength = placeholder.Length;
        Editor.Focus(FocusState.Programmatic);
    }

    private void InsertTag(string leftTag, string rightTag, string content, int offset = 0, bool selectText = false)
    {
        var openTag = $"[{leftTag}]";
        var closeTag = $"[/{rightTag}]";
        var fullTag = $"{openTag}{content}{closeTag}";
        var selectionStart = Editor.SelectionStart;
        Editor.Text = Editor.Text.Insert(selectionStart, fullTag);
        if (selectText)
        {
            // 选中已插入的内容,输入即可整体替换
            Editor.SelectionStart = selectionStart + openTag.Length + offset;
            Editor.SelectionLength = content.Length;
        }
        else
        {
            // 有内容时把光标移到闭合标签之后;空标签则停在标签内部,便于直接输入。
            // 若一律停在内部,连续插入多个媒体标签时后一个会被插进前一个内部,
            // 产生 [img]A[upload]B[/upload][/img] 这类错误嵌套。
            Editor.SelectionStart = string.IsNullOrEmpty(content)
                ? selectionStart + openTag.Length + offset
                : selectionStart + fullTag.Length + offset;
            Editor.SelectionLength = 0;
        }
        Editor.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 在光标位置插入 MD 包裹语法。
    /// </summary>
    /// <param name="left">左侧包裹符号</param>
    /// <param name="right">右侧包裹符号</param>
    /// <param name="placeholder">无选中文本时的占位文字,插入后自动选中,输入即可替换</param>
    /// <param name="isBlock">块级语法时前后加换行</param>
    private void InsertMdSyntax(string left, string right, string placeholder = "", bool isBlock = false)
    {
        var selStart = Editor.SelectionStart;
        var selLen = Editor.SelectionLength;
        var hasSelection = selLen > 0;
        var selected = hasSelection ? Editor.Text.Substring(selStart, selLen) : placeholder;

        var prefix = isBlock ? "\n" : "";
        var suffix = isBlock ? "\n" : "";
        var fullTag = $"{prefix}{left}{selected}{right}{suffix}";

        Editor.Text = Editor.Text.Insert(selStart, fullTag);
        if (hasSelection)
        {
            // 选中文本被包裹,光标置于包裹内容之后
            Editor.SelectionStart = selStart + prefix.Length + left.Length + selected.Length;
            Editor.SelectionLength = 0;
        }
        else if (!string.IsNullOrEmpty(placeholder))
        {
            // 选中占位文字,输入即可替换
            Editor.SelectionStart = selStart + prefix.Length + left.Length;
            Editor.SelectionLength = placeholder.Length;
        }
        else
        {
            Editor.SelectionStart = selStart + prefix.Length + left.Length;
            Editor.SelectionLength = 0;
        }
        Editor.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 在 MD 模式下插入媒体标签（图片/视频/音频/文档）。
    /// </summary>
    private void InsertMdMedia(string label, string url)
    {
        var md = label switch
        {
            "img" => $"![图片]({url})",
            "video" => $@"<video src=""{url}"" controls></video>",
            "audio" => $@"<audio src=""{url}"" controls></audio>",
            "upload" => $"[下载]({url})",
            _ => url
        };
        var selStart = Editor.SelectionStart;
        Editor.Text = Editor.Text.Insert(selStart, md);
        Editor.SelectionStart = selStart + md.Length;
        Editor.Focus(FocusState.Programmatic);
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        TextContent = Editor.Text.Replace("\r\n", "\n").Replace("\r", "\n");
        // 防抖:连续输入期间 300ms 后才刷新一次预览,避免每次击键全量重渲染
        _previewDebounceTimer ??= CreatePreviewDebounceTimer();
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    // 预览刷新防抖计时器
    private DispatcherTimer? _previewDebounceTimer;

    private DispatcherTimer CreatePreviewDebounceTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplyContentToViewer();
        };
        return timer;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // 停止防抖计时器,避免计时器持有页面引用造成泄漏
        _previewDebounceTimer?.Stop();
        base.OnNavigatedFrom(e);
    }

    //以下方法用于创建Md的代码块,但是UBB编辑器不需要支持这个操作。
    private void InsertCodeBlock()
    {
        var cursorPos = Editor.SelectionStart;
        var selectionLen = Editor.SelectionLength;
        var originalText = Editor.Text;
        var textWithoutSelection = originalText.Remove(cursorPos, selectionLen);
        var codeBlock = "`" + "`";
        var newText = textWithoutSelection.Insert(cursorPos, codeBlock);
        Editor.Text = newText;
        var newCursorPos = cursorPos + 1;
        Editor.SelectionStart = newCursorPos;
        Editor.Focus(FocusState.Programmatic);
    }

    private void EmojiContainer_ItemClick(object sender, ItemClickEventArgs e)
    {
        var i = e.ClickedItem;
        if (i == null) return;
        var index = EmojiContainer.Items.IndexOf(i);
        if (Emojis == null) return;
        if (Emojis.Count <= index) return;
        var tag = Emojis[index].EmojiName;
        if (tag == null) return;
        var selectionStart = Editor.SelectionStart;
        var emojiName = "[" + tag + "]";
        Editor.Text = Editor.Text.Insert(selectionStart, emojiName);
        Editor.SelectionStart = selectionStart + emojiName.Length;
        Editor.Focus(FocusState.Programmatic);
    }

    private async void FileUploadChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileUploadChoice.SelectedIndex != -1)
        {
            var item = FileUploadChoice.SelectedItem as ListViewItem;
            if (item == null) return;
            var tag = item?.Tag;
            if (tag == null) return;
            var operation = tag.ToInt();
            if (operation == 1)
            {
                // 上传本地文件:选择后上传,成功后按当前模式插入标签
                FileHelper.Hide();
                var result = await FileUploadService.PickAndUploadAsync(XamlRoot, CurrentLabel);
                if (!result.Success)
                {
                    if (!string.IsNullOrEmpty(result.Error))
                        Flower.Play(FlowStatus.Fail, $"上传失败:{result.Error}");
                    return;
                }

                if (TextContentType == (int)Objects.ContentType.Markdown)
                    InsertMdMedia(CurrentLabel, result.Url);
                else
                    InsertTag(CurrentLabel, CurrentLabel, result.Url);
            }
            else if (operation == 2)
            {
                // 仅输入标签:插入空标签,由用户在编辑器中补充内容
                FileHelper.Hide();
                if (TextContentType == (int)Objects.ContentType.Markdown)
                    InsertMdMedia(CurrentLabel, "");
                else
                    InsertTag(CurrentLabel, CurrentLabel, "");
            }
            else
            {
                // 使用自定义 URL
                CustomLink.Visibility = Visibility.Visible;
                CustomLink.Focus(FocusState.Keyboard); //自动聚焦，减少鼠标操作
            }
        }
    }

    /// <summary>
    /// 编辑器粘贴(Ctrl+V):剪贴板中为文件或图片时先上传,再按当前编辑器模式插入标签;
    /// 普通文本不干预,交给默认粘贴行为。
    /// </summary>
    private async void Editor_Paste(object sender, TextControlPasteEventArgs e)
    {
        try
        {
            var content = Clipboard.GetContent();
            var hasFiles = content.Contains(StandardDataFormats.StorageItems);
            var hasBitmap = !hasFiles && content.Contains(StandardDataFormats.Bitmap);
            if (!hasFiles && !hasBitmap) return; // 普通文本:走默认粘贴

            // 必须在任何 await 之前同步置位:本方法是 async void,遇到 await 就会返回,
            // 若此时仍未置位,框架会认为未处理而照常执行默认粘贴。
            e.Handled = true;

            await UploadClipboardMediaAsync(content, hasFiles);
        }
        catch (Exception ex)
        {
            // async void 必须兜底:未捕获异常会终止进程
            Debug.WriteLine($"粘贴上传失败: {ex.Message}");
            Flower.Play(FlowStatus.Fail, "粘贴上传失败，请重试");
        }
    }

    /// <summary>
    /// 上传剪贴板中的媒体并插入标签。文件与位图两种来源共用。
    /// </summary>
    private async Task UploadClipboardMediaAsync(DataPackageView content, bool hasFiles)
    {
        await RunUploadBusyAsync(async () =>
        {
            if (hasFiles)
                await UploadPastedFilesAsync(await content.GetStorageItemsAsync());
            else
                await UploadPastedBitmapAsync(await content.GetBitmapAsync());
        });
    }

    /// <summary>
    /// 上传期间的忙碌态:提示进度并暂时禁止编辑,避免重复粘贴触发并发上传;结束后恢复原状态。
    /// 用 IsReadOnly 而非 IsEnabled=false —— 后者会连带禁用右键菜单与文本选择高亮。
    /// </summary>
    private async Task RunUploadBusyAsync(Func<Task> upload)
    {
        var previousStatus = status.Text;
        var previousReadOnly = Editor.IsReadOnly;
        try
        {
            Editor.IsReadOnly = true;
            status.Text = "正在上传…";
            await upload();
        }
        finally
        {
            Editor.IsReadOnly = previousReadOnly;
            status.Text = previousStatus;
        }
    }

    private async Task UploadPastedFilesAsync(IReadOnlyList<IStorageItem> items)
    {
        var files = items.OfType<StorageFile>().ToList();
        if (files.Count == 0) return;

        var uploaded = 0;
        foreach (var file in files)
        {
            status.Text = $"正在上传 {file.Name}…";
            var result = await FileUploadService.UploadAsync(file);
            if (!result.Success)
            {
                // 仅在确实有错误时提示(用户取消不算错误)
                if (!string.IsNullOrEmpty(result.Error))
                    Flower.Play(FlowStatus.Fail, $"上传失败:{result.Error}");
                continue;
            }

            InsertMediaTag(FileUploadService.GetLabelForFileName(file.Name), result.Url);
            uploaded++;
        }

        if (uploaded > 0) Flower.Play(FlowStatus.Success, $"已上传 {uploaded} 个文件");
    }

    private async Task UploadPastedBitmapAsync(RandomAccessStreamReference bitmap)
    {
        status.Text = "正在上传图片…";
        var result = await FileUploadService.UploadClipboardBitmapAsync(bitmap);
        if (!result.Success)
        {
            if (!string.IsNullOrEmpty(result.Error))
                Flower.Play(FlowStatus.Fail, $"上传失败:{result.Error}");
            return;
        }

        InsertMediaTag("img", result.Url);
        Flower.Play(FlowStatus.Success, "图片已上传");
    }

    /// <summary>
    /// 按当前编辑器模式插入媒体标签:UBB 生成 [xx]url[/xx],Markdown 生成对应语法。
    /// </summary>
    private void InsertMediaTag(string label, string url)
    {
        if (TextContentType == (int)Objects.ContentType.Markdown)
            InsertMdMedia(label, url);
        else
            InsertTag(label, label, url);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var r = await SendDialog.ShowAsync();
            if (r != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(TextContent))
            {
                Flower.Play(FlowStatus.Warning, "内容不能为空");
                return;
            }
            switch (NavigationInfo.EditorMode)
            {
                case EditorMode.ReplyToTopic:
                case EditorMode.ReplyToPost:
                    await SendReply();
                    break;
                case EditorMode.EditMyPost:
                case EditorMode.EditMyTopic:
                    await EditPost();
                    break;
                case EditorMode.DraftNewTopic:
                    await DraftNewTopic();
                    break;
            }
        }
        catch (Exception ex)
        {
            // 对话框宿主卸载/网络异常等:避免 async void 未捕获异常导致进程崩溃
            Debug.WriteLine($"发送失败: {ex.Message}");
            Flower.Play(FlowStatus.Fail, "发送失败，请重试");
        }
    }

    /// <summary>
    /// 安全地显示对话框:页面被导航移除等场景下 ShowAsync 会抛异常,在此兜底。
    /// </summary>
    private async Task<ContentDialogResult> ShowDialogSafelyAsync(ContentDialog dialog)
    {
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"对话框打开失败: {ex.Message}");
            return ContentDialogResult.None;
        }
    }

    #endregion

    #region 实现请求

    private async Task EditPost()
    {
        var url = ApiEndpoints.Post.Edit(NavigationInfo.PostId);
        var request = new EditPostRequest
        {
            Content = TextContent,
            ContentType = TextContentType,
            NotifyPoster = NotifyPoster,
            Title = SetTitle.Text
        };
        var text = SerializationHelper.TrySerialize(request);
        var requestBody = new StringContent(text, Encoding.UTF8, "application/json");
        var res = await ApiService.Put(url, requestBody);
        if (!res.IsSuccess)
        {
            status.Text = $"编辑失败:{res.Message}";
        }
        else
        {
            var param = new TopicNavigationInfo
            {
                IsJumpingMode = true,
                TopicId = NavigationInfo.TopicId,
                TargetFloor = NavigationInfo.Floor
            };
            GoBackToTopic(param);
        }
    }

    private async Task SendReply()
    {
        var url = ApiEndpoints.Topic.SendReply(NavigationInfo.TopicId);
        // 尾注只在发送时追加,不修改 TextContent,避免重试时重复追加
        var content = IsTailVisible ? TextContent + "\n" + Tail : TextContent;
        // 服务器仅在 ReplyToPost 模式接受 parentId 字段,否则 500,故按模式使用不同请求类型
        string text;
        if (NavigationInfo.EditorMode == EditorMode.ReplyToPost)
        {
            text = SerializationHelper.TrySerialize(new SendReplyToPostRequest
            {
                Content = content,
                ContentType = TextContentType,
                IsAnonymous = IsAnonymous,
                NotifyAllReplier = NotifyAllReplier,
                Title = "",
                ParentId = NavigationInfo.ParentId
            });
        }
        else
        {
            text = SerializationHelper.TrySerialize(new SendReplyRequest
            {
                Content = content,
                ContentType = TextContentType,
                IsAnonymous = IsAnonymous,
                NotifyAllReplier = NotifyAllReplier,
                Title = ""
            });
        }

        var requestBody = new StringContent(text, Encoding.UTF8, "application/json");
        var res = await ApiService.Submit<int>(url, requestBody);
        if (!res.IsSuccess)
        {
            //
            status.Text = $"发送失败:{res.Message}";
        }
        else
        {
            var param = new TopicNavigationInfo
            {
                IsJumpingMode = NavigationInfo.EditorMode == EditorMode.ReplyToPost,
                TopicId = NavigationInfo.TopicId,
                TargetFloor = NavigationInfo.Floor,
                GoToLatest = NavigationInfo.EditorMode == EditorMode.ReplyToTopic
            };
            GoBackToTopic(param);
        }
    }

    private async Task DraftNewTopic()
    {
        var url = ApiEndpoints.Board.SendNewTopic(NavigationInfo.BoardId);
        var request = new CreateTopicRequest
        {
            Content = TextContent,
            ContentType = TextContentType,
            IsAnonymous = IsAnonymous,
            NotifyPoster = NotifyPoster,
            Title = SetTitle.Text,
            Type = PostTypeValue
        };
        var text = SerializationHelper.TrySerialize(request);
        var requestBody = new StringContent(text, Encoding.UTF8, "application/json");
        var res = await ApiService.Submit<int>(url, requestBody);
        if (!res.IsSuccess)
        {
            //
            status.Text = $"发送新主题失败:{res.Message}";
        }
        else
        {
            // 注意:本分支目前没有调用方(BoardPage 的"发主题"入口尚未开放)。
            // 返回目标是版面页而不是主题页,故不使用导航参数覆盖;将来接入时
            // 需要另行决定如何打开新建的主题(可直接用 res.Data 作为 TopicId)。
            GoBack();
        }
    }

    #endregion


    #region UI事件处理

    private void PreviewMode_Click(object sender, RoutedEventArgs e)
    {
        EditArea.IsPaneOpen = !EditArea.IsPaneOpen;
        if (EditArea.IsPaneOpen) ApplyContentToViewer();
    }

    private void EmojiType_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var type = sender as SegmentedItem;
        if (type == null) return;
        type.IsSelected = true;
        if (type.Tag is not string tag) return;
        LoadEmojiSet(tag);
    }

    private void SwitchContentType_Click(object sender, RoutedEventArgs e)
    {
        if (TextContentType == (int)ContentType.Ubb)
        {
            TextContentType = 1;
            MD.Visibility = Visibility.Visible;
            UBB.Visibility = Visibility.Collapsed;
            MdViewer.Visibility = Visibility.Visible;
            UbbViewer.Visibility = Visibility.Collapsed;
            Flower.Play("\uE946", "切换到Markdown");
        }
        else
        {
            TextContentType = 0;
            MD.Visibility = Visibility.Collapsed;
            UBB.Visibility = Visibility.Visible;
            MdViewer.Visibility = Visibility.Collapsed;
            UbbViewer.Visibility = Visibility.Visible;
            Flower.Play("\uE946", "切换到UBB");
        }

        ApplyContentToViewer();
    }

    private void ReceiveNotice_Unchecked(object sender, RoutedEventArgs e)
    {
        NotifyPoster = false;
    }

    private void ReceiveNotice_Checked(object sender, RoutedEventArgs e)
    {
        NotifyPoster = true;
    }

    private void PostType_Checked(object sender, RoutedEventArgs e)
    {
        var r = sender as RadioButton;
        if (r?.Tag is not int type) return;
        PostTypeValue = type;
    }

    private void ConfirmCustomLink_Click(object sender, RoutedEventArgs e)
    {
        if (TextContentType == (int)Objects.ContentType.Markdown)
            InsertMdMedia(CurrentLabel, CustomLink.Text);
        else
            InsertTag(CurrentLabel, CurrentLabel, CustomLink.Text);
        FileHelper.Hide();
    }

    private void FileHelper_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        CustomLink.Visibility = Visibility.Collapsed;
        FileUploadChoice.SelectedIndex = -1;
    }

    private void GoBack()
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    /// <summary>
    /// 返回主题页,并把"应跳转到的位置"一并交给它。
    /// 主题页会被重建,而后退栈里的原始导航参数只含 TopicId、不含目标楼层,
    /// 因此必须通过 <see cref="NavigationArgs"/> 覆盖通道传递新参数。
    /// </summary>
    /// <param name="param">主题页应采用的新导航参数。</param>
    private void GoBackToTopic(TopicNavigationInfo param)
    {
        if (!Frame.CanGoBack) return;

        // 仅当返回目标确实是主题页时才登记:本页也可能从版面页(投票入口)进入,
        // 那种情况下登记会留下无人消费的陈旧载荷,并影响后续真正的主题页导航。
        var backStack = Frame.BackStack;
        if (backStack.Count > 0 && backStack[backStack.Count - 1].SourcePageType == typeof(TopicPage))
            NavigationArgs.Set<TopicPage>(param);

        Frame.GoBack();
    }

    #endregion
}