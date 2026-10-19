using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CC98.Kernel;
using CC98.Objects;
using CC98.Services.Extensions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CC98.Controls.ComposeSheet;

/// <summary>
///     帖子页底部滑出的发帖浮层。
/// </summary>
/// <remarks>
///     结构是「变暗层 + 按钮行 + 亚克力面板」，整体用一个 <see cref="TranslateTransform" /> 从下往上滑，
///     动效与 SplitView 展开时一致（滑入 + 底层变暗）。
///     放在 TopicPage 的 RootGrid 里、跨全部行，这样宽度就是整页宽度。
///     面板的亚克力需要一个采样源，否则只是在纯色上糊一层 —— 由 <see cref="BackgroundImageSource" /> 传入背景图。
/// </remarks>
public sealed partial class ComposeSheet : UserControl
{
    private const int OpenDurationMs = 300;
    private const int CloseDurationMs = 200;

    /// <summary>布局还没算出来时的兜底滑动距离。</summary>
    private const double FallbackSlideDistance = 800;

    /// <summary>背景图：垫在亚克力面板后面，作为模糊的采样源。为空则不显示。</summary>
    /// <summary>
    ///     亚克力的采样源（背景图）。类型故意是 <see cref="string" />：直接接路径或 URL，
    ///     这样 XAML 里可以 <c>{x:Bind AppSettings.Current.ThemePicture}</c> 这样绑，不用在代码里转。
    /// </summary>
    public static readonly DependencyProperty BackgroundImageSourceProperty = DependencyProperty.Register(
        nameof(BackgroundImageSource), typeof(string), typeof(ComposeSheet),
        new PropertyMetadata(null, OnBackgroundImageSourceChanged));

    private bool _isOpen;
    private bool _isClosing;
    private bool _isSending;

    /// <summary>收起后要不要清空内容（只有发送成功才清）。</summary>
    private bool _resetOnHide;

    /// <summary>这一次会话是不是编辑模式（编辑的原文不该当草稿留着）。</summary>
    private bool _sessionWasEditing;

    public ComposeSheet()
    {
        InitializeComponent();
    }

    /// <summary>浮层已经收起（点关闭、点发送，或点了变暗的页面）。</summary>
    public event EventHandler? Dismissed;

    /// <summary>发送成功（调用方可以借此刷新帖子列表）。</summary>
    public event EventHandler? Sent;

    public bool IsOpen => _isOpen;

    /// <summary>
    ///     当前编辑模式。决定标题行是否出现、发送走哪套接口：
    ///     <see cref="EditorMode.ReplyToTopic" /> / <see cref="EditorMode.ReplyToPost" /> 走回帖，
    ///     <see cref="EditorMode.EditMyPost" /> / <see cref="EditorMode.EditMyTopic" /> 走编辑。
    /// </summary>
    public EditorMode Mode { get; set; } = EditorMode.ReplyToTopic;

    /// <summary>回帖目标主题 id（<see cref="EditorMode.ReplyToTopic" /> 用）。</summary>
    public int TopicId { get; set; }

    /// <summary>编辑目标楼层 id（编辑模式用）。</summary>
    public int PostId { get; set; }

    /// <summary>被回复楼层 id（<see cref="EditorMode.ReplyToPost" /> 用；服务器只在这个模式接受 parentId）。</summary>
    public int ParentId { get; set; }

    /// <summary>编辑帖子时的原标题，用来回填标题框。</summary>
    public string InitialTitle { get; set; } = "";

    /// <summary>状态栏文案，引用回复时用「引用某某的回复」。为空则按模式给默认文案。</summary>
    public string HintText { get; set; } = "";

    private static ApiService Api => App.Current.GetService<ApiService>();

    /// <summary>
    ///     亚克力的采样源。面板本身是半透明的模糊层，底下没有东西可模糊时就只是块灰；
    ///     给它一张背景图（比如版块主题图）才会出现真正的亚克力质感。
    ///     传 <c>ms-appx:///…</c>、绝对路径、相对路径或 http(s) 地址都行，为空则隐藏背景层。
    /// </summary>
    public string? BackgroundImageSource
    {
        get => (string?)GetValue(BackgroundImageSourceProperty);
        set => SetValue(BackgroundImageSourceProperty, value);
    }

    private static void OnBackgroundImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComposeSheet sheet) return;

        var source = CreateImageSource(e.NewValue as string);
        sheet.BackdropImage.Source = source;
        sheet.BackdropImage.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>把字符串转成图片源；解析不了就当没设（浮层照常打开，不抛异常）。</summary>
    private static ImageSource? CreateImageSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            // 绝对 URI（ms-appx:///、http(s)://、file:///、带盘符的路径）直接吃
            if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
                return new BitmapImage(absolute);

            // 相对路径按应用目录解析（Assets 是随程序复制出去的）
            return new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, path)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"背景图读不出来：{path} / {ex.Message}");
            return null;
        }
    }

    /// <summary>从底部滑出。</summary>
    public void Show()
    {
        if (_isOpen) return;

        _isOpen = true;
        ConfigureForMode();

        // 这次打开时该用哪种语言。浮层第一次显示时模板才实体化，
        // Segmented 会按 XAML 里的默认项再抛一次 SelectionChanged 把语言拉回 UBB
        //（表现就是「编辑 MD 帖子却用 UBB 预览」），所以等动画结束再断言一次。
        var language = Editor.ContentType;

        Root.Visibility = Visibility.Visible;
        Scrim.Opacity = 0;
        SheetTransform.Y = SlideDistance;

        var storyboard = new Storyboard();
        AddAnimation(storyboard, Scrim, "Opacity", 1, OpenDurationMs, null);
        AddAnimation(storyboard, SheetTransform, "Y", 0, OpenDurationMs,
            new CubicEase { EasingMode = EasingMode.EaseOut });

        // 滑到位再把焦点给源码框（动画期间抢焦点会打断入场）
        storyboard.Completed += (_, _) =>
        {
            if (Editor.ContentType != language) Editor.ContentType = language;

            // 收起时预览被清空了；如果是保留的草稿，这里按当前内容补回来
            Editor.RefreshPreview();
            Editor.FocusInput();
        };

        storyboard.Begin();
    }

    /// <summary>滑回底部后收起。</summary>
    public void Hide()
    {
        if (!_isOpen || _isClosing) return;

        _isClosing = true;

        var storyboard = new Storyboard();
        AddAnimation(storyboard, Scrim, "Opacity", 0, CloseDurationMs, null);
        AddAnimation(storyboard, SheetTransform, "Y", SlideDistance, CloseDurationMs,
            new CubicEase { EasingMode = EasingMode.EaseIn });

        storyboard.Completed += (_, _) =>
        {
            _isClosing = false;
            _isOpen = false;
            Root.Visibility = Visibility.Collapsed;

            // 发送成功 → 清空；编辑会话取消 → 也清空（原帖内容留在服务器上，留着没有意义）；
            // 只有回复草稿才保留，方便用户关了再回来接着写。
            if (_resetOnHide || _sessionWasEditing)
            {
                _resetOnHide = false;
                Editor.Reset();
            }

            // 预览栏一律清掉：草稿文本会留着，但排版内容不该挂在浮层里过夜，
            // 否则下次打开会先闪一下上次的渲染结果。
            Editor.ClearPreview();

            Dismissed?.Invoke(this, EventArgs.Empty);
        };

        storyboard.Begin();
    }

    /// <summary>滑动距离：整页高度（控件被拉伸成整页，所以这个高度就是页面高度）。</summary>
    private double SlideDistance => ActualHeight > 0 ? ActualHeight : FallbackSlideDistance;

    /// <summary>
    ///     动画直接挂到目标对象上，不用 <c>TargetName</c>（资源字典的命名域解析不到可视树元素）。
    /// </summary>
    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string property, double to,
        int durationMs, EasingFunctionBase? easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs))
        };

        if (easing is not null) animation.EasingFunction = easing;

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Scrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        Hide();
    }

    /// <summary>
    ///     按模式调整界面。三种形态要分清：
    ///     <list type="bullet">
    ///         <item>回复（ReplyToTopic/ReplyToPost）—— 没有标题；</item>
    ///         <item>编辑回复（EditMyPost）—— 也没有标题，回复本身不带头衔；</item>
    ///         <item>编辑主题（EditMyTopic）—— 只有这一种才放出标题行，并回填原标题。</item>
    ///     </list>
    /// </summary>
    private void ConfigureForMode()
    {
        var editingTopic = Mode == EditorMode.EditMyTopic;
        var editingReply = Mode == EditorMode.EditMyPost;
        var editing = editingTopic || editingReply;

        // 引用回复要把"引用某某的回复"显示在标题栏，所以回复模式优先用调用方给的文案
        Editor.Title = editingTopic
            ? "编辑主题"
            : editingReply
                ? "编辑回复"
                : string.IsNullOrWhiteSpace(HintText) ? "回复主题" : HintText;

        // 编辑主题才允许改标题；编辑回复时标题行收起，但值照样带上（要回传原标题）
        Editor.SetTitleEditing(editingTopic, editing ? InitialTitle : null);

        // 编辑改不了匿名（接口没这个字段），把勾选框置灰
        Editor.SetAnonymousEditable(!editing);

        // 新回复从"不匿名"开始（和 SketchPage 每次进页面一样）；编辑则显示原帖状态
        if (!editing) Editor.IsAnonymous = false;

        _sessionWasEditing = editing;
    }

    /// <summary>
    ///     把要编辑的原文连同它的状态一起填进编辑器：内容、UBB/MD 类型、匿名。
    ///     少搬一个就会出现「MD 帖子用 UBB 预览」「匿名帖显示成实名」这类错位。
    /// </summary>
    public void Prefill(string content, int contentType, bool isAnonymous = false)
    {
        Editor.ContentType = contentType;
        Editor.ContentText = content;
        Editor.IsAnonymous = isAnonymous;
    }

    /// <summary>
    ///     预填引用块（回帖用）。引用语法一律是 UBB，不管原帖是 UBB 还是 Markdown
    ///     —— 和 SketchPage 一致，它也是直接把 UBB 引用串塞进编辑器。
    /// </summary>
    public void PrefillQuote(string quote)
    {
        Editor.PrefillQuote(quote);
    }

    #region 发送

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        await SendAsync();
    }

    private async Task SendAsync()
    {
        if (_isSending) return;

        if (string.IsNullOrWhiteSpace(Editor.ContentText))
        {
            Editor.Notify(FlowStatus.Warning, "内容不能为空");
            return;
        }

        // 只有编辑主题需要校验标题；编辑回复带的是主题原标题，界面上看不到也无从修改
        if (Mode == EditorMode.EditMyTopic && string.IsNullOrWhiteSpace(Editor.ContentTitle))
        {
            Editor.Notify(FlowStatus.Warning, "标题不能为空");
            return;
        }

        _isSending = true;
        SendButton.IsEnabled = false;
        try
        {
            var (success, message) = Mode switch
            {
                EditorMode.ReplyToTopic or EditorMode.ReplyToPost => await SendReplyAsync(),
                EditorMode.EditMyPost or EditorMode.EditMyTopic => await EditPostAsync(),
                _ => (false, "这个模式还不支持发送")
            };

            if (!success)
            {
                Editor.Notify(FlowStatus.Fail, message);
                return;
            }

            _resetOnHide = true;
            Hide();
            Sent?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // async void 必须兜底，未捕获异常会终止进程
            Debug.WriteLine($"发送失败：{ex}");
            Editor.Notify(FlowStatus.Fail, "发送失败，请重试");
        }
        finally
        {
            _isSending = false;
            SendButton.IsEnabled = true;
        }
    }

    /// <summary>
    ///     回帖。与 SketchPage 保持一致：只有 ReplyToPost 模式带 parentId，
    ///     <see cref="SendReplyRequest" /> 带上 parentId 服务器会 500。
    /// </summary>
    private async Task<(bool Success, string Message)> SendReplyAsync()
    {
        var url = ApiEndpoints.Topic.SendReply(TopicId);

        var json = Mode == EditorMode.ReplyToPost
            ? SerializationHelper.TrySerialize(new SendReplyToPostRequest
            {
                Content = Editor.ContentText,
                ContentType = Editor.ContentType,
                IsAnonymous = Editor.IsAnonymous,
                NotifyAllReplier = Editor.NotifyAllReplier,
                Title = "",
                ParentId = ParentId
            })
            : SerializationHelper.TrySerialize(new SendReplyRequest
            {
                Content = Editor.ContentText,
                ContentType = Editor.ContentType,
                IsAnonymous = Editor.IsAnonymous,
                NotifyAllReplier = Editor.NotifyAllReplier,
                Title = ""
            });

        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await Api.Submit<int>(url, body);
        return res.IsSuccess ? (true, "") : (false, $"发送失败：{res.Message}");
    }

    /// <summary>编辑帖子/主题：唯一允许改标题的入口，所以请求里要带上标题。</summary>
    private async Task<(bool Success, string Message)> EditPostAsync()
    {
        var request = new EditPostRequest
        {
            Content = Editor.ContentText,
            ContentType = Editor.ContentType,
            NotifyPoster = Editor.NotifyPoster,
            Title = Editor.ContentTitle
        };

        using var body = new StringContent(SerializationHelper.TrySerialize(request), Encoding.UTF8,
            "application/json");
        var res = await Api.Put(ApiEndpoints.Post.Edit(PostId), body);
        return res.IsSuccess ? (true, "") : (false, $"保存失败：{res.Message}");
    }

    #endregion
}
