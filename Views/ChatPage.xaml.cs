using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using CC98.Controls.Primitives;
using CC98.Controls.UbbTextBlock.Common.Events;
using CC98.Kernel;
using CC98.Objects;
using CC98.Services;
using CC98.Services.Extensions;
using CC98.Services.Helpers;

using DevWinUI;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CC98.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     聊天页面。
/// </summary>
public sealed partial class ChatPage : Page
{
    public Increment ChatHistoryIncrement = new();
    public ObservableCollection<ChatInfo> ChatInfoList = [];
    public ApiService ApiService = App.Current.GetService<ApiService>();
    public int CurrentUserId;

    public ObservableCollection<ChatMessage> Messages = [];
    public ChatInfo TargetUserInfo = new();
    public Increment UserIncrement = new();

    public ChatPage()
    {
        InitializeComponent();
        if (App.Current.AppMainWindow is MainWindow mainwindow) mainwindow.NavigationView.IsPaneOpen = false;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var args = e.TryGetParameter<ChatNavigationInfo>();
        await GetRecent();

        if (args != null && args.HasTarget) //由私信功能跳转
        {
            var info = args.ChatUserInfo;
            if (info != null) TargetUserInfo = info; //获取要私信的对象
            StartChat();
        }
        else
        {
            UserList.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// 搜索框回车触发用户搜索。
    /// </summary>
    private void SearchUser_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = SearchAndSelectUserAsync();
        }
    }

    /// <summary>
    /// 搜索按钮点击触发用户搜索。
    /// </summary>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _ = SearchAndSelectUserAsync();
    }

    /// <summary>
    /// 按用户名搜索用户:存在则获取头像/ID 加入左侧列表并选中,否则提示未找到。
    /// </summary>
    private async Task SearchAndSelectUserAsync()
    {
        var userName = SearchUser.Text.Trim();
        if (string.IsNullOrEmpty(userName)) return;
        // 左侧列表已有该用户(按用户名,忽略大小写):直接选中并滚动到可见,无需请求 API
        var existingIndex = ChatInfoList.ToList().FindIndex(
            x => string.Equals(x.Name, userName, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            var item = ChatInfoList[existingIndex];
            UserList.SelectedIndex = existingIndex;
            UserList.ScrollIntoView(item,ScrollIntoViewAlignment.Leading); // 让该项滚入左侧列表可视区域
            Tip.Show(InfoType.Success, $"找到用户:{item.Name}");
            return;
        }
        try
        {
            var url = ApiEndpoints.User.SearchUserByName(userName);
            var result = await ApiService.Fetch<UserInfo>(url);
            if (!result.IsSuccess || result.Data == null || result.Data.Id == 0)
            {
                Tip.Show(InfoType.Error, $"未找到用户:{userName}");
                Debug.WriteLine($"未找到用户:{userName}");
                return;
            }

            var user = result.Data;
            if (user.Id == AppSettings.Current.UserId)
            {
                Tip.Show(InfoType.Information, "不能和自己聊天哦~");
                return;
            }
            var info = new ChatInfo
            {
                UserId = user.Id,
                Name = user.Name,
                PortraitUrl = user.PortraitUrl
            };
            // 复用 StartChat:加入列表(已存在则选中现有项)并触发消息加载
            TargetUserInfo = info;
            StartChat();
            Tip.Show(InfoType.Success, $"找到用户:{user.Name}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"搜索用户失败: {ex.Message}");
            Tip.Show(InfoType.Error, "搜索失败，请重试");
        }
    }

    //用于添加目标用户到聊天列表，并执行选中
    private void StartChat()
    {
        var list = ChatInfoList.Select(x => x.UserId);
        if (list.Contains(TargetUserInfo.UserId))
        {
            UserList.SelectedIndex = ChatInfoList.ToList().FindIndex(x => x.UserId == TargetUserInfo.UserId);
        }
        else
        {
            ChatInfoList.Insert(0, TargetUserInfo);
            UserList.SelectedIndex = 0;
        }
    }

    private async void ContactRepeater_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var i = UserList.SelectedIndex;
        if (i > -1)
        {
            ChatHistoryIncrement.Clear();
            CurrentUserId = ChatInfoList[i].UserId;
            await RefreshMessageList();
        }
    }

    /// <summary>
    /// 左侧联系人列表增量加载:滚动到末尾附近时加载下一页最近联系人。
    /// LoadNextPage 自带防重入,滚动回收导致的重复触发会被拦截。
    /// </summary>
    private async void UserList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemIndex >= ChatInfoList.Count - 3 && UserIncrement.HasMore)
            await UserIncrement.LoadNextPage(GetRecent);
    }

    private async Task<bool> GetRecent()
    {
        var chatInfoUrl = ApiEndpoints.User.RecentChatUserList(UserIncrement.StartIndex);
        var chatInfoResult = await ApiService.Fetch<List<ChatInfo>>(chatInfoUrl);
        if (!chatInfoResult.IsSuccess || chatInfoResult.Data == null)
            //
            return false;
        var data = chatInfoResult.Data;

        var param = string.Join("&", data.Select(x => $"id={x.UserId}").Distinct());
        var userInfoUrl = ApiEndpoints.User.BasicUserInfoList(param);
        var userInfoResult = await ApiService.Fetch<List<BasicUserInfo>>(userInfoUrl);
        if (!userInfoResult.IsSuccess || userInfoResult.Data == null)
            //报错
            return false;

        var userInfoList = userInfoResult.Data;
        foreach (var info in data)
        {
            var user = userInfoList.FirstOrDefault(x => x.Id == info.UserId);
            if (user != null)
            {
                info.Name = user.Name;
                info.PortraitUrl = user.PortraitUrl;
            }
        }

        UserIncrement.HasMore = data.Count == UserIncrement.PageSize;
        ChatInfoList.AddRange(data);
        UserListEmptyState.Visibility = ChatInfoList.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        return true;
    }

    private async Task<bool> GetMessageList()
    {
        var messageUrl = ApiEndpoints.User.ChatHistory(CurrentUserId, ChatHistoryIncrement.StartIndex);
        var messageResult = await ApiService.Fetch<List<ChatMessage>>(messageUrl);
        if (!messageResult.IsSuccess || messageResult.Data == null)
        {
            //
            Tip.Show(InfoType.Error, "获取聊天记录失败");
            return false;
        }
            
        var data = messageResult.Data;
        ChatHistoryIncrement.HasMore = data.Count == ChatHistoryIncrement.PageSize;

        var ordered = data.OrderBy(m => m.MessageId).ToList();
        foreach (var message in ordered)
        {
            message.IsMe = message.ReceiverId == CurrentUserId;
        }

        // 一次性构造完整列表(更早的消息在前)再整体填充:
        // 避免逐条 Insert(0) 导致的 O(n^2) 元素移动与多次布局,改为尾部 O(1) 追加
        var combined = new List<ChatMessage>(ordered.Count + Messages.Count);
        combined.AddRange(ordered);
        combined.AddRange(Messages);
        Messages.Clear();
        foreach (var message in combined) Messages.Add(message);

        ChatEmptyState.Visibility = Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        return true;
    }


    private async Task RefreshMessageList()
    {
        Messages.Clear();
        ChatHistoryIncrement.Clear();
        if (await GetMessageList()) ScrollToBottom();
    }

    // 上滑加载防重入标志
    private bool _loadingEarlier;

    /// <summary>
    /// 滚动到接近顶部(上滑)时加载更早的消息,并显示顶部进度环。
    /// </summary>
    private void HistoryViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        // 忽略滚动动画的中间帧,仅在稳定状态且接近顶部时触发
        if (!e.IsIntermediate && HistoryViewer.VerticalOffset <= 2 && ChatHistoryIncrement.HasMore)
        {
            _ = LoadEarlierMessagesAsync();
        }
    }

    /// <summary>
    /// 加载更早一页消息:插入列表顶部,并补偿滚动偏移保持视觉位置。
    /// </summary>
    private async Task LoadEarlierMessagesAsync()
    {
        if (_loadingEarlier) return;
        _loadingEarlier = true;
        LoadingMore.Visibility = Visibility.Visible;
        LoadingMore.IsActive = true;
        var oldExtent = HistoryViewer.ExtentHeight;
        try
        {
            await ChatHistoryIncrement.LoadNextPage(GetMessageList);
            // 等布局完成后补偿滚动偏移:新消息在顶部展开,offset 需增加新增高度
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                var delta = HistoryViewer.ExtentHeight - oldExtent;
                if (delta > 0)
                    HistoryViewer.ChangeView(null, HistoryViewer.VerticalOffset + delta, null, true);
            });
        }
        finally
        {
            _loadingEarlier = false;
            LoadingMore.IsActive = false;
            LoadingMore.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 滚动到底部,展示最新消息。
    /// ItemsRepeater 填充集合后布局是异步的,单次 TryEnqueue 可能早于布局完成,
    /// 故延迟一小段时间等布局稳定后再滚动,确保切换对话/发送消息后总能停在最新消息。
    /// </summary>
    private void ScrollToBottom()
    {
        _scrollToBottomTimer ??= DispatcherQueue.CreateTimer();
        _scrollToBottomTimer.Interval = TimeSpan.FromMilliseconds(120);
        _scrollToBottomTimer.IsRepeating = false;
        _scrollToBottomTimer.Tick -= ScrollToBottomHandler;
        _scrollToBottomTimer.Tick += ScrollToBottomHandler;
        _scrollToBottomTimer.Start();
    }

    private DispatcherQueueTimer? _scrollToBottomTimer;

    private void ScrollToBottomHandler(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        HistoryViewer.ChangeView(null, HistoryViewer.ExtentHeight, null, false);
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ReplyBody.Text)) return;
        SendButton.IsEnabled = false;
        try
        {
            var url = ApiEndpoints.User.SendPrivateMessage;
            var post = new PrivateMessage
            {
                ReceiverId = CurrentUserId,
                Content = ReplyBody.Text
            };
            var postText = SerializationHelper.TrySerialize(post);
            var requestBody = new StringContent(postText, Encoding.UTF8, "application/json");
            var res = await ApiService.Submit<string>(url, requestBody);
            if (res.IsSuccess)
            {
                ReplyBody.Text = "";
                await RefreshMessageList();
            }
                
            else
                Tip.Show(InfoType.Error, "发送回复失败");
        }
        catch (Exception ex)
        {
            // 避免 async void 未捕获异常崩溃,并确保按钮恢复
            System.Diagnostics.Debug.WriteLine($"发送失败: {ex.Message}");
            Tip.Show(InfoType.Error, "发送失败，请重试");
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 简易 UBB 工具栏统一入口:按按钮 Tag 分发。
    /// emoji 打开表情浮出层,img/upload 走文件上传,其余插入 UBB 标签。
    /// </summary>
    private async void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton b || b.Tag is not string tag) return;
        switch (tag)
        {
            case "emoji":
                EmojiFlyout.ShowAt(b);
                break;
            case "img":
            case "upload":
                await InsertMediaAsync(tag);
                break;
            default:
                InsertTag(tag);
                break;
        }
    }

    /// <summary>
    /// 表情浮出层打开时初始化分类栏(仅首次),并显式加载当前分类的表情。
    /// 注意:不在 Opening 期间设置 SelectedIndex,避免 SelectionChanged 在 Flyout
    /// 内容树初始化期间同步触发导致 XAML 模板重入崩溃(0xc000027b)。
    /// </summary>
    private void EmojiFlyout_Opening(object sender, object e)
    {
        if (EmojiCategoryBar.ItemsSource == null)
        {
            var categories = EmojiService.GetCategories();
            EmojiCategoryBar.ItemsSource = categories;
        }

        // 无选中时(首次打开)默认显示第一个分类;已有选中(上次选择)则保持
        if (EmojiCategoryBar.SelectedItem is not EmojiCategoryInfo current
            && EmojiCategoryBar.Items.Count > 0)
        {
            current = (EmojiCategoryInfo)EmojiCategoryBar.Items[0];
            EmojiGrid.ItemsSource = EmojiService.GetEmojis(current.Tag);
        }
    }

    /// <summary>
    /// 切换表情分类:GridView 选中项变化时加载对应表情。
    /// </summary>
    private void EmojiCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmojiCategoryBar.SelectedItem is EmojiCategoryInfo category)
            EmojiGrid.ItemsSource = EmojiService.GetEmojis(category.Tag);
    }

    /// <summary>
    /// 点击表情:在回复框光标位置插入 [表情名] 并关闭浮出层。
    /// </summary>
    private void EmojiGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Emoji emoji) return;
        InsertEmoji(emoji.EmojiName);
        EmojiFlyout.Hide();
    }

    private void InsertEmoji(string name)
    {
        var selStart = ReplyBody.SelectionStart;
        var code = $"[{name}]";
        ReplyBody.Text = ReplyBody.Text.Insert(selStart, code);
        ReplyBody.SelectionStart = selStart + code.Length;
        ReplyBody.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 在回复框光标位置插入 UBB 包裹标签,有选中文本时包裹选中内容。
    /// </summary>
    private void InsertTag(string tag)
    {
        var selStart = ReplyBody.SelectionStart;
        var selLen = ReplyBody.SelectionLength;
        var hasSelection = selLen > 0;
        var selected = hasSelection ? ReplyBody.Text.Substring(selStart, selLen) : "";
        var tagText = $"[{tag}]{selected}[/{tag}]";

        ReplyBody.Text = ReplyBody.Text.Insert(selStart, tagText);
        // 开标签长度为 tag.Length + 2("[tag]");光标停在开标签之后、
        // 包裹内容之前(无选中时即两个标签中间,直接输入即可)
        var openTagLen = tag.Length + 2;
        ReplyBody.SelectionStart = selStart + openTagLen + (hasSelection ? selected.Length : 0);
        ReplyBody.SelectionLength = 0;
        ReplyBody.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 选择文件并上传,成功后插入媒体标签。
    /// </summary>
    private async Task InsertMediaAsync(string label)
    {
        var result = await FileUploadService.PickAndUploadAsync(XamlRoot, label);
        if (!result.Success)
        {
            if (!string.IsNullOrEmpty(result.Error))
                Tip.Show(InfoType.Error, $"上传失败:{result.Error}");
            return; // 取消或失败都不插入
        }

        var selStart = ReplyBody.SelectionStart;
        var tagText = $"[{label}]{result.Url}[/{label}]";
        ReplyBody.Text = ReplyBody.Text.Insert(selStart, tagText);
        ReplyBody.SelectionStart = selStart + tagText.Length;
        ReplyBody.Focus(FocusState.Programmatic);
        Tip.Show(InfoType.Success, "上传成功");
    }


    private async void Ref_Click(object sender, RoutedEventArgs e)
    {
        await RefreshMessageList();
    }

    private async void UbbTextBlock_MediaClicked(object sender, MediaClickEventArgs e)
    {
        var context = new LinkContext
        {
            Frame = Frame,
            CurrentTopicId = null,
            JumpToFloor = null,
            ImageList = null,
            Notify = Tip.Show
        };

        switch (e.MediaType)
        {
            case MediaType.Image:
                // UBB 图片:显式启动预览器
                LinkNavigationService.ShowImageViewer(e.Source);
                break;
            case MediaType.Link:
                await LinkNavigationService.HandleLinkAsync(e.Source, context);
                break;
            case MediaType.AtUser:
                await LinkNavigationService.HandleAtUserAsync(e.Source, context);
                break;
            case MediaType.File or MediaType.Audio or MediaType.Video:
                // UBB 文件/音视频:显式下载
                await LinkNavigationService.DownloadFileAsync(e.Source, context);
                break;
        }
    }

    private void UserButton_Click(object sender, RoutedEventArgs e)
    {
        if(sender is HyperlinkButton userButton&&userButton.Tag is int userId)
        {
            var param = new ProfileNavigationInfo
            {
                IsMe = false,//聊天列表不会有自己
                UserId = userId
            };
            Frame.Navigate(typeof(ProfilePage), param);
        }
    }
}