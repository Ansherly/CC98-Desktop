using CC98.Controls.Extensions;
using CC98.Controls.Primitives;
using CC98.Controls.UbbTextBlock;
using CC98.Controls.UbbTextBlock.Common.Events;
using CC98.Controls.UbbTextBlock.Parser;
using CC98.Kernel;
using CC98.Kernel.Authorize;
using CC98.Objects;
using CC98.Services;
using CC98.Services.Extensions;
using CC98.Services.Helpers;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static CC98.Kernel.ApiEndpoints;
using static CSharpMath.Rendering.Text.TextAtom;

namespace CC98.Views;

/// <summary>
/// 主题页面。
/// </summary>
public sealed partial class TopicPage : Page
{
    public ObservableCollection<Reply> Replies = [];
    public TopicInfo TopicInfo { get; set; } = new() { };
    public UserInfo Profile = new() { Popularity = 0, PostCount = 0, FanCount = 0 };
    public bool IsJumping = false;//是否正在进行跳转
    public int JumpToFloor = -1;
    public int TopicId = 0;
    public int CurrentPage = 0;
    public int PageSize = 10;

    // 主题浏览位置记忆:用于"进入本页的子页面(编辑器、用户资料等)后返回同一主题"时恢复分页。
    // 本页是 NavigationCacheMode=Disabled(每次导航都会重建),故用静态表跨实例传递。
    // 按主题 Id 分别记忆(而不是只记"最近一次"),因此 A → B → 返回 A 也能回到 A 的页码;
    // 容量有上限,长时间使用不会无限增长,超限时淘汰最久未使用的条目。
    private const int MaxRememberedPositions = 30;
    private static readonly List<(int TopicId, int Page)> RecentPositions = [];

    /// <summary>记录某主题离开时的页码(命中即前移,维持"最近使用"顺序)。</summary>
    private static void RememberPosition(int topicId, int page)
    {
        if (topicId <= 0) return;

        RecentPositions.RemoveAll(p => p.TopicId == topicId);
        RecentPositions.Insert(0, (topicId, page));
        if (RecentPositions.Count > MaxRememberedPositions)
            RecentPositions.RemoveRange(MaxRememberedPositions, RecentPositions.Count - MaxRememberedPositions);
    }

    /// <summary>取出某主题上次离开时的页码(命中即前移,维持"最近使用"顺序)。</summary>
    private static bool TryGetRememberedPosition(int topicId, out int page)
    {
        var index = RecentPositions.FindIndex(p => p.TopicId == topicId);
        if (index < 0)
        {
            page = 0;
            return false;
        }

        var entry = RecentPositions[index];
        if (index > 0)
        {
            RecentPositions.RemoveAt(index);
            RecentPositions.Insert(0, entry);
        }

        page = entry.Page;
        return true;
    }

    public ApiService ApiService = App.Current.GetService<ApiService>();
    public TopicPage()
    {
        InitializeComponent();
        LoadSet();
        LoadFavorites();
        Unloaded += Topic_Unloaded;
    }

    private void Topic_Unloaded(object sender, RoutedEventArgs e)
    {
        Pager?.SelectedIndexChanged -= Pager_SelectedIndexChanged;
        GlobalMediaPlayer.Instance.Pause();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // 记录离开时的浏览位置,供返回同一主题时恢复分页(例如进入编辑器后未发送就返回)。
        RememberPosition(TopicId, CurrentPage);

        //释放资源
        base.OnNavigatedFrom(e);
        Replies.Clear();
        foreach (var item in CollectionMenu.Items.OfType<MenuFlyoutItem>())
        {
            item.Click -= CollectionItem_Click;  // 取消订阅
        }
        CollectionMenu.Items.Clear();
        Pager?.SelectedIndexChanged -= Pager_SelectedIndexChanged;
        VotePanel?.IsOpen = false;
        VotePanel?.Target = null;
        VotePanel?.Content = null;
        if (ProfileViewer != null)
        {
            ProfileViewer.IsOpen = false;
            ProfileViewer.Target = null;
            ProfileViewer.Content = null;
        }
    }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 优先采用发起方登记的覆盖参数:例如 SketchPage 发帖/编辑成功后返回本页时,
        // 后退栈里的原始参数只有 TopicId、没有目标楼层,必须用覆盖参数才能跳到指定楼层。
        // 其余情况一律使用导航参数本身。
        // 覆盖通道只在"返回导航"上取用:发起方都是先登记再 GoBack,
        // 不加这个门控的话,万一有残留载荷,会被此后任意一次进入本页的导航误用。
        TopicNavigationInfo? args =
            e.NavigationMode == NavigationMode.Back
            && NavigationArgs.TryConsume<TopicPage, TopicNavigationInfo>(out var overridden)
                ? overridden
                : e.TryGetParameter<TopicNavigationInfo>();
        if (args == null)
        {
            // 缺少导航参数(例如被错误地直接导航到本页):给出提示,而不是留一个空白页
            Flower.Play(FlowStatus.Fail, "缺少主题参数,无法打开该主题");
            return;
        }

        TopicId = args.TopicId;
        if (args.GoToLatest || args.IsJumpingMode)
        {
            // 跳转路径依赖 LoadTopicInfo 先设置 Pager.NumberOfPages,保持串行
            await LoadTopicInfo();
            if (args.GoToLatest)
            {
                // 回复数为 0 时 NumberOfPages 可能为 0,这里退化成加载第 0 页,避免索引越界
                var lastPage = System.Math.Max(Pager.NumberOfPages - 1, 0);
                if (lastPage > 0)
                    Pager.SelectedPageIndex = lastPage;
                else
                    await LoadReply();
                return;
            }
            IsJumping = true;
            await Tp(args.TargetFloor);
            return;
        }

        // 若本次是"返回同一主题"(进入编辑器未发送就返回、看完用户资料返回等),
        // 恢复到离开时的分页位置,而不是回到第 1 页。
        // 位置按主题记忆,因此 A → B → 返回 A 也能恢复到 A 的页码(命中的条目会被移到表头)。
        if (e.NavigationMode == NavigationMode.Back
            && TryGetRememberedPosition(args.TopicId, out var rememberedPage)
            && rememberedPage > 0)
        {
            // 需要先由 LoadTopicInfo 设置 Pager.NumberOfPages 才能定位页码
            await LoadTopicInfo();
            // 主题回复数可能已变化,越界时夹取到有效范围
            var target = System.Math.Clamp(rememberedPage, 0, System.Math.Max(Pager.NumberOfPages - 1, 0));
            if (target > 0)
                Pager.SelectedPageIndex = target; // 触发 Pager_SelectedIndexChanged,由它完成 LoadReply
            else
                await LoadReply();
            return;
        }

        // 普通路径:主题信息与回复列表互不依赖,并行加载缩短首屏等待
        await Task.WhenAll(LoadTopicInfo(), LoadReply());


    }
    private void LoadSet()
    {
        var hideImage = AppSettings.Current.HideImage;
        HideImageFlyoutItem.Text = hideImage ? "关闭无图模式" : "启用无图模式";
        ImageOffIcon.Symbol = hideImage ? FluentIcons.Common.Symbol.ImageOff : FluentIcons.Common.Symbol.Image;
    }
    /// <summary>
    /// 加载收藏集
    /// </summary>
    private void LoadFavorites()
    {
        var favoritesJson = AppSettings.Current.FavoriteGroups;
        try
        {
            var favoritesList = SerializationHelper.TryDeserialize<List<Favorites>>(favoritesJson);
            if (favoritesList == null)
            {
                Flower.Play(FlowStatus.Fail, "解析收藏夹缓存出错");
                return;
            }
            foreach (var favorites in favoritesList)
            {
                var item = new MenuFlyoutItem { Text = favorites.Name, Tag = favorites.Id, Icon = new FluentIcons.WinUI.SymbolIcon { Symbol = FluentIcons.Common.Symbol.Tag } };
                item.Click += CollectionItem_Click;
                CollectionMenu.Items.Add(item);
            }
        }
        catch(Exception ex)
        {
            Flower.Play(FlowStatus.Fail,ex.Message);
        }
        
    }
    //当jumping mode=true时响应。响应包括两种，来自外部页面导航的跳转和用户点击帖子内链接的跳转。
    //floor如17824L，则page为1782，sort为4，此时目标楼层的index是3.sort=1时目标index为0。如果sort=0,则目标页码在上一页。
    private async Task Tp(int floor)
    {
        // 解析楼层
        var page = floor / 10;
        var sort = floor % 10;
        var currentPage = Pager.SelectedPageIndex;
        // 情况1：目标就在当前页
        if (currentPage == page)
        {
            await HandleSamePageJump(sort);
        }
        // 情况2：目标是上一页的最后一个（特殊边界情况）
        else if (currentPage == page - 1 && sort == 0)
        {
            await HandlePrevPageLastItem();
        }
        // 情况3：需要翻页
        else
        {
            HandlePageNavigation(page, sort);
        }
    }

    /// <summary>
    /// 处理同一页内的跳转
    /// </summary>
    private async Task HandleSamePageJump(int sort)
    {
        if (sort == 0)
        {
            // 整十楼：跳转到上一页的最后一个
            Pager.SelectedPageIndex--;
            JumpToFloor = 9;
        }
        else
        {
            // 非整十楼：直接跳转到对应楼层
            await EnsureReplyLoadedAndScroll(sort - 1);
        }
    }

    /// <summary>
    /// 处理跳转到上一页最后一个的情况
    /// </summary>
    private async Task HandlePrevPageLastItem()
    {
        Pager.SelectedPageIndex++;

        // 判断第10楼是否已加载
        if (Replies.Count == 10)
        {
            ScrollTo(9);
        }
        else
        {
            await LoadReply();
            ScrollTo(9);
        }
    }

    /// <summary>
    /// 处理需要翻页的跳转
    /// </summary>
    private void HandlePageNavigation(int targetPage, int targetSort)
    {
        if (targetSort == 0)
        {
            // 整十楼：目标在上一页
            Pager.SelectedPageIndex = targetPage - 1;
            JumpToFloor = 9;
        }
        else
        {
            // 非整十楼：目标在当前页
            Pager.SelectedPageIndex = targetPage;
            JumpToFloor = targetSort - 1;
        }
    }

    /// <summary>
    /// 确保指定索引的回复已加载并滚动到该位置
    /// </summary>
    private async Task EnsureReplyLoadedAndScroll(int targetIndex)
    {
        // 如果目标索引尚未加载，先加载数据
        if (Replies.Count <= targetIndex)
        {
            await LoadReply();
        }

        ScrollTo(targetIndex);
    }
    private async Task LoadTopicInfo()
    {
        // 主题信息与收藏状态互不依赖,并行请求
        var topicInfoUrl = ApiEndpoints.Topic.TopicInfo(TopicId);
        var isFavoriteUrl = ApiEndpoints.Topic.IsFavorite(TopicId);
        var topicInfoTask = ApiService.Fetch<TopicInfo>(topicInfoUrl);
        var isFavoriteTask = ApiService.Fetch<bool>(isFavoriteUrl);

        var topicInfoResult = await topicInfoTask;
        if (!topicInfoResult.IsSuccess || topicInfoResult.Data == null)
        {
            return;
        }
        var data = topicInfoResult.Data;
        TopicInfo.FavoriteCount = data.FavoriteCount;
        TopicInfo.Title = data.Title;
        TopicInfo.Time = data.Time;
        TopicInfo.HitCount = data.HitCount;
        TopicInfo.ReplyCount = data.ReplyCount;
        TopicInfo.IsVote = data.IsVote;
        TopicInfo.IsAnonymous = data.IsAnonymous;
        var isFavoriteResult = await isFavoriteTask;
        if (isFavoriteResult.IsNotValid)
        {
            //
        }
        else
        {
            TopicInfo.IsFavorite = isFavoriteResult.Data;
        }
        Pager.NumberOfPages = (TopicInfo.ReplyCount / 10) + 1;
        PagerFix();
        
    }


    // LoadReply 的加载代次:翻页/跳页可能并发触发多次 LoadReply(如 SelectedIndexChanged 与显式调用同时发生),
    // 用代次确保只有最新一次加载的结果被写入,避免楼层重复/串页。
    private int _loadReplyGeneration;
    private async Task LoadReply()
    {
        var generation = ++_loadReplyGeneration;
        //清空
        Replies.Clear();
        var replyUrl = ApiEndpoints.Topic.ReplyList(TopicId, CurrentPage * PageSize);
        var replyResult = await ApiService.Fetch<List<Reply>>(replyUrl);
        if (!replyResult.IsSuccess || replyResult.Data == null)
        {
            //
            //await App.Logger.WriteAsync("Topic", "加载回帖失败", replyResult.Message);
            return;
        }
        var data = replyResult.Data;

        var param = string.Join("&", data.Where(x => !x.IsAnonymous && x.UserId.HasValue).Select(x => $"id={x.UserId}").Distinct());
        var userInfoUrl = ApiEndpoints.User.BasicUserInfoList(param);
        var userInfoResult = await ApiService.Fetch<List<BasicUserInfo>>(userInfoUrl);
        if (!userInfoResult.IsSuccess || userInfoResult.Data == null)
        {
            //报错
            Flower.Play(FlowStatus.Fail, $"加载用户头像失败：{userInfoResult.Message}");
        }

        var userInfoList = userInfoResult.Data;
        //提取头像链接
        foreach (var reply in data)
        {
            //CC98 Deleter
            if (reply.IsDeleted)
            {
                reply.UserName = "CC98 Deleter";
                reply.Content = "<--该回复已被管理员或发布者删除-->";
                reply.PortraitUrl = "ms-appx:///Assets/deleter.png";
                //跳过
                continue;
            }
            if (reply.IsAnonymous)
            {
                var code = reply.UserName;
                reply.UserName = $"匿名{code.ToUpper()}";
                reply.PortraitUrl = "ms-appx:///Assets/hide.gif";
                //跳过
                continue;
            }

            var user = userInfoList?.FirstOrDefault(x => x.Id == reply.UserId);
            if (user != null)
            {
                reply.PortraitUrl = user.PortraitUrl;
            }
        }
        //仅当仍是最新一次加载时才写入,防止旧请求覆盖新页数据
        if (generation != _loadReplyGeneration) return;
        Replies.AddRange(data);
        TopicEmptyState.Visibility = Replies.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }


    private void Person_Click(object sender, RoutedEventArgs e)
    {
        var h = sender as HyperlinkButton;
        ProfileViewer.Target = h;
        if (h?.Tag is not Reply t || t.IsAnonymous || t.IsDeleted) return;
        var info = new ProfileNavigationInfo { IsMe = t.IsMe, UserId = t.UserId ?? 0 };
        Frame.Navigate(typeof(ProfilePage), info);
    }
    private async void Pager_SelectedIndexChanged(PagerControl sender, PagerControlSelectedIndexChangedEventArgs args)
    {
        //此方法在页面加载完成后会被调用一次，Pager的SelectedIndex会被设置为0。
        //所以页面构造函数处不需要单独调用LoadReply方法。
        //限定了只有页面主动加载和用户点击翻页，index从-1到0不触发数据加载。

        PagerFix();
        if (args.PreviousPageIndex != -1)
        {
            var index = Pager.SelectedPageIndex;
            CurrentPage = index;
            if (index >= 0)
            {
                await LoadReply();
                if (IsJumping && JumpToFloor != -1)
                {
                    ScrollTo(JumpToFloor);
                    IsJumping = false;
                    JumpToFloor = -1;
                }
            }
        }

    }

    private async void UbbTextBlock_MediaClicked(object sender, MediaClickEventArgs e)
    {
        var context = new LinkContext
        {
            Frame = Frame,
            CurrentTopicId = TopicId,
            JumpToFloor = async floor =>
            {
                IsJumping = true;
                await Tp(floor);
            },
            ImageList = null,
            Flower = Flower
        };

        switch (e.MediaType)
        {
            case MediaType.Image:
                // UBB 图片:显式启动预览器(收集帖内全部图片做画廊),不依赖 URL 识别
                var u = sender as UbbTextBlock;
                if (u == null) return;
                var ubb = u.UbbText;
                var doc = Parser.Parse(ubb);
                if (doc == null) return;
                var list = new List<string>();
                var nodes = doc.Root.GetDescendantsByType(UbbNodeType.Image);
                foreach (var node in nodes)
                {
                    list.Add(ExtractImageUrl(node));
                }
                LinkNavigationService.ShowImageViewer(e.Source, list);
                break;
            case MediaType.Link:
                await LinkNavigationService.HandleLinkAsync(e.Source, context);
                break;
            case MediaType.AtUser:
                await LinkNavigationService.HandleAtUserAsync(e.Source, context);
                break;
            case MediaType.File or MediaType.Audio or MediaType.Video:
                // UBB 文件/音视频:显式下载,不依赖 URL 识别(http/https 均可)
                await LinkNavigationService.DownloadFileAsync(e.Source, context);
                break;
        }
    }

    private async void MdViewer_LinkClicked(object sender, Controls.MarkdownTextBlock.Common.Events.LinkClickedEventArgs e)
    {
        e.Handled = true;
        var url = e.Url;
        if (string.IsNullOrWhiteSpace(url)) return;

        var context = new LinkContext
        {
            Frame = Frame,
            CurrentTopicId = TopicId,
            JumpToFloor = async floor =>
            {
                IsJumping = true;
                await Tp(floor);
            },
            ImageList = null,
            Flower = Flower
        };

        await LinkNavigationService.HandleLinkAsync(url, context);
    }

    /// <summary>
    ///     Markdown 图片点击:与 UBB 图片点击(见 <see cref="UbbTextBlock_MediaClicked"/>)保持一致 ——
    ///     把整段内容里的图片一起交给预览器做画廊,并以被点击的这张为当前项。
    /// </summary>
    private void MdViewer_ImageClicked(object sender, Controls.MarkdownTextBlock.Common.Events.ImageClickedEventArgs e)
    {
        e.Handled = true;
        if (string.IsNullOrWhiteSpace(e.Source)) return;

        // 用控件自己维护的图片序列(已做 ~/ 展开等归一化),这样 IndexOf 一定找得到被点击的那张
        var gallery = sender is Controls.MarkdownTextBlock.MarkdownTextBlock md
            ? md.GetImageUrls().ToList()
            : null;

        LinkNavigationService.ShowImageViewer(e.Source, gallery);
    }


    
    private void writereply_Click(object sender, RoutedEventArgs e)
    {
        var param = new SketchNavigationInfo
        {
            EditorMode = EditorMode.ReplyToTopic,
            TopicId = TopicId,
            HintText = TopicInfo.Title,
        };
        Frame.Navigate(typeof(SketchPage), param);
    }

    #region 赠米(财富转账)

    /// <summary>
    /// 从帖子操作菜单(赠米)打开转账弹窗,收款人预填为被赠楼层作者。
    /// </summary>
    private async Task ShowWealthTransferAsync(Reply reply)
    {
        //不能向自己赠米:用 AppSettings 中的当前用户 id 校验
        if (reply.UserId.HasValue && reply.UserId.Value == AppSettings.Current.UserId)
        {
            Flower.Play(FlowStatus.Warning, "不能给自己赠米");
            return;
        }
        //已删除/匿名的楼层无法确定真实收款人,禁止赠米
        if (reply.IsDeleted)
        {
            Flower.Play(FlowStatus.Warning, "该楼层已被删除，无法赠米");
            return;
        }
        if (reply.IsAnonymous || string.IsNullOrEmpty(reply.UserName))
        {
            Flower.Play(FlowStatus.Warning, "不能给匿名用户赠米");
            return;
        }

        //重置弹窗输入并预填收款人
        WealthReceiver.Text = reply.UserName;
        WealthAmount.Value = 0;
        WealthReason.Text = "";
        WealthError.Text = "";
        UpdateWealthPreview();
        WealthTransferDialog.XamlRoot = XamlRoot;
        try
        {
            await WealthTransferDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // 页面被导航移除等场景下对话框可能无法显示
            System.Diagnostics.Debug.WriteLine($"赠米弹窗打开失败: {ex.Message}");
        }
    }

    private void WealthAmount_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        UpdateWealthPreview();
    }

    /// <summary>
    /// 按"手续费 = max(金额*10%, 10)"实时计算并显示实际到账。
    /// </summary>
    private void UpdateWealthPreview()
    {
        var wealth = (int)System.Math.Round(WealthAmount.Value);
        if (wealth < 10)
        {
            WealthPreview.Text = "";
            return;
        }

        var fee = System.Math.Max((int)(wealth * 0.1), 10);
        var received = wealth - fee;
        WealthPreview.Text = $"手续费 {fee} 米，对方实际收到 {received} 米";
    }

    private async void WealthTransferOk_Click(object sender, RoutedEventArgs e)
    {
        //校验收款人
        var userNames = WealthReceiver.Text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();
        if (userNames.Count == 0)
        {
            WealthError.Text = "请输入收款人用户名";
            return;
        }

        //不能给自己赠米:收款人中不允许出现当前登录用户
        if (userNames.Contains(AppSettings.Current.UserName, StringComparer.OrdinalIgnoreCase))
        {
            WealthError.Text = "不能给自己赠米";
            return;
        }

        //校验金额
        var wealth = (int)System.Math.Round(WealthAmount.Value);
        if (wealth < 10)
        {
            WealthError.Text = "转账金额不能小于 10";
            return;
        }

        var reason = WealthReason.Text.Trim();
        WealthError.Text = "";
        WealthTransferOk.IsEnabled = false;
        try
        {
            //使用强类型请求体
            var post = new WealthTransferMessage
            {
                UserNames = userNames,
                Wealth = wealth,
                Reason = reason
            };
            var postText = SerializationHelper.TrySerialize(post);
            var requestBody = new StringContent(postText, Encoding.UTF8, "application/json");
            var res = await ApiService.Put(ApiEndpoints.User.TransferWealth(), requestBody);
            if (!res.IsSuccess)
            {
                //网络问题或财富值不足等,展示服务器返回的错误信息
                WealthError.Text = $"赠米失败：{res.Message}";
                return;
            }
            try
            {
                var userList = SerializationHelper.TryDeserialize<List<string>>(res.Content) ?? [];
                Flower.Play(FlowStatus.Success, $"赠米成功：{string.Join("、", userList)}");
            }
            catch (Exception ex)
            {
                Flower.Play(FlowStatus.Success, $"解析收款人列表失败：{ex.Message}");
            }
            finally
            {
                WealthTransferDialog.Hide();
            }
            
        }
        catch (Exception ex)
        {
            WealthError.Text = $"赠米失败：{ex.Message}";
        }
        finally
        {
            WealthTransferOk.IsEnabled = true;
        }
    }

    private void WealthTransferCancel_Click(object sender, RoutedEventArgs e)
    {
        WealthTransferDialog.Hide();
    }

    private void WealthTransferDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        WealthError.Text = "";
    }

    #endregion

    #region 风评(加/扣风评)

    private int _ratingPostId;
    private int _ratingType = 1;
    private int _selectedReasonId;
    private string _selectedReason = "";

    /// <summary>
    /// 从帖子操作菜单(风评)打开风评弹窗,默认加载正面理由。
    /// </summary>
    private async Task ShowRatingAsync(Reply reply)
    {
        //匿名、已删除、自己的楼层不可风评
        if (reply.IsDeleted)
        {
            Flower.Play(FlowStatus.Warning, "该楼层已被删除，无法风评");
            return;
        }
        if (reply.IsAnonymous)
        {
            Flower.Play(FlowStatus.Warning, "不能给匿名用户风评");
            return;
        }
        if (reply.UserId.HasValue && reply.UserId.Value == AppSettings.Current.UserId)
        {
            Flower.Play(FlowStatus.Warning, "不能给自己风评");
            return;
        }

        _ratingPostId = reply.Id;
        _selectedReasonId = 0;
        _selectedReason = "";
        RatingError.Text = "";
        RatingColor.Background = null;
        RatingSelectedReason.Text = "";
        RatingOk.IsEnabled = false;

        //默认选中"正面";若与当前选择相同则不触发 SelectionChanged,需显式加载
        _isSettingRatingType = true;
        RatingTypeBar.SelectedItem = RatingTypeBar.Items[0];
        _isSettingRatingType = false;
        if (RatingTypeBar.SelectedItem == RatingTypeBar.Items[0])
        {
            await LoadRatingReasonsAsync(1);
        }

        RatingDialog.XamlRoot = XamlRoot;
        try
        {
            await RatingDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"风评弹窗打开失败: {ex.Message}");
        }
    }

    //程序化设置类型时跳过事件处理,避免重复加载
    private bool _isSettingRatingType;

    private async void RatingTypeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_isSettingRatingType) return;
        if (sender.SelectedItem?.Tag is not string s || !int.TryParse(s, out var type)) return;
        RatingError.Text = "";
        await LoadRatingReasonsAsync(type);
    }

    /// <summary>
    /// 按类型(1正面/2负面)加载理由列表,并为每项随机生成莫兰迪色。
    /// </summary>
    private async Task LoadRatingReasonsAsync(int type)
    {
        _ratingType = type;
        RatingOk.IsEnabled = false;
        try
        {
            var url = ApiEndpoints.Post.RateReason(type);
            var result = await ApiService.Fetch<List<RatingReason>>(url);
            if (!result.IsSuccess || result.Data == null)
            {
                RatingError.Text = $"加载风评理由失败：{result.Message}";
                return;
            }

            var reasons = result.Data.Where(r => r.Enabled).ToList();
            foreach (var r in reasons) r.ColorHex = ColorEx.GenerateMorandiColorHex();
            RatingRepeater.ItemsSource = reasons;

            //默认选中第一项
            if (reasons.Count > 0)
            {
                _selectedReasonId = reasons[0].Id;
                _selectedReason = reasons[0].Reason;
                if (RatingRepeater.TryGetElement(0) is Button first)
                {
                    RatingColor.Background = first.Background;
                    RatingSelectedReason.Text = _selectedReason;
                }
                RatingOk.IsEnabled = true;
            }
            else
            {
                RatingSelectedReason.Text = "暂无可用理由";
            }
        }
        catch (Exception ex)
        {
            RatingError.Text = $"加载风评理由失败：{ex.Message}";
        }
    }

    private void RatingItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not int id) return;
        _selectedReasonId = id;
        _selectedReason = b.Content?.ToString() ?? "";
        RatingColor.Background = b.Background;
        RatingSelectedReason.Text = _selectedReason;
        //滚动到该项并居中
        b.StartBringIntoView(new BringIntoViewOptions
        {
            VerticalAlignmentRatio = 0.5,
            AnimationDesired = true
        });
    }

    //理由项固定尺寸,用于按视口中心计算当前项索引
    private const double RatingItemHeight = 40;
    private const double RatingItemStep = RatingItemHeight + 6; //高度 + 上下Margin(3+3)

    private double CenterPointOfViewportInExtent()
    {
        return RatingScroll.VerticalOffset + RatingScroll.ViewportHeight / 2;
    }

    private int GetSelectedIndexFromViewport()
    {
        if (RatingRepeater.ItemsSourceView == null || RatingRepeater.ItemsSourceView.Count == 0) return -1;
        var index = (int)System.Math.Floor(CenterPointOfViewportInExtent() / RatingItemStep);
        index %= RatingRepeater.ItemsSourceView.Count;
        return index;
    }

    private void RatingScroll_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
    {
        var index = GetSelectedIndexFromViewport();
        if (index < 0) return;
        if (RatingRepeater.TryGetElement(index) is not Button selected) return;
        _selectedReasonId = (int)selected.Tag;
        _selectedReason = selected.Content?.ToString() ?? "";
        RatingColor.Background = selected.Background;
        RatingSelectedReason.Text = _selectedReason;
    }

    //参考微软 ItemsRepeater 滚动缩放示例:靠近视口中心的项放大
    private void RatingRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        var item = ElementCompositionPreview.GetElementVisual(args.Element);
        var svVisual = ElementCompositionPreview.GetElementVisual(RatingScroll);
        var scrollProperties = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(RatingScroll);

        var scaleExpresion = scrollProperties.Compositor.CreateExpressionAnimation();
        scaleExpresion.SetReferenceParameter("svVisual", svVisual);
        scaleExpresion.SetReferenceParameter("scrollProperties", scrollProperties);
        scaleExpresion.SetReferenceParameter("item", item);
        scaleExpresion.Expression = "1 - abs((svVisual.Size.Y/2 - scrollProperties.Translation.Y) - (item.Offset.Y + item.Size.Y/2))*(.25/(svVisual.Size.Y/2))";
        item.StartAnimation("Scale.X", scaleExpresion);
        item.StartAnimation("Scale.Y", scaleExpresion);

        var centerPointExpression = scrollProperties.Compositor.CreateExpressionAnimation();
        centerPointExpression.SetReferenceParameter("item", item);
        centerPointExpression.Expression = "Vector3(item.Size.X/2, item.Size.Y/2, 0)";
        item.StartAnimation("CenterPoint", centerPointExpression);
    }

    private async void RatingOk_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedReasonId == 0)
        {
            RatingError.Text = "请选择一个风评理由";
            return;
        }

        RatingError.Text = "";
        RatingOk.IsEnabled = false;
        try
        {
            //请求体:{"reasonId":12,"type":1},使用强类型,风评为 PUT 请求
            var post = new Rating
            {
                ReasonId = _selectedReasonId,
                Type = _ratingType
            };
            var postText = SerializationHelper.TrySerialize(post);
            var requestBody = new StringContent(postText, Encoding.UTF8, "application/json");
            var res = await ApiService.Put(ApiEndpoints.Post.Rate(_ratingPostId), requestBody);
            if (!res.IsSuccess)
            {
                RatingError.Text = $"风评失败：{res.Message}";
                return;
            }

            Flower.Play(FlowStatus.Success, $"风评成功：{_selectedReason}");
            RatingDialog.Hide();
        }
        catch (Exception ex)
        {
            RatingError.Text = $"风评失败：{ex.Message}";
        }
        finally
        {
            RatingOk.IsEnabled = true;
        }
    }

    private void RatingCancel_Click(object sender, RoutedEventArgs e)
    {
        RatingDialog.Hide();
    }

    private void RatingDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        RatingError.Text = "";
        RatingRepeater.ItemsSource = null;
    }

    #endregion

    private async void TileFlyout_Click(object sender, RoutedEventArgs e)
    {
        var m = sender as MenuFlyoutItem;
        if (m?.Tag is not string tag) return;
        switch (tag)
        {
            case "0":
                await LoadTopicInfo();
                await LoadReply();
                Flower.Play(FlowStatus.Success, "刷新成功");
                break;
            case "1":
                var shareUrl = $"{TopicInfo.Title} https://www.cc98.org/topic/{TopicId}";
                var dataPackage = new DataPackage();
                dataPackage.SetText(shareUrl);
                Clipboard.SetContent(dataPackage);
                Flower.Play(FlowStatus.Success, "已复制帖子链接");
                break;
            case "2":
                //无需响应
                break;
            case "3":
                var endpoint = ApiEndpoints.Topic.DeleteFavoriteTopic(TopicId);
                var res = await ApiService.Delete(endpoint);
                if (res == null || !res.IsSuccess)
                {
                    Flower.Play(FlowStatus.Fail, $"取消收藏失败:{res?.Message}");
                }
                else
                {
                    TopicInfo.IsFavorite = false;
                    Flower.Play(FlowStatus.Success, "已取消收藏");
                }
                break;
            case "4":
                AppSettings.Current.HideImage = !AppSettings.Current.HideImage;
                LoadSet();
                break;
        }
    }


    private async void CollectionItem_Click(object sender, RoutedEventArgs e)
    {
        var m = sender as MenuFlyoutItem;
        if (m?.Tag is not int groupId) return;
        var url = ApiEndpoints.Topic.AddIntoFavorites(TopicId, groupId);
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var result = await ApiService.Put(url, content);
        if (!result.IsSuccess)
        {
            //
            Flower.Play(FlowStatus.Fail, result.Message);
            return;
        }
        await LoadTopicInfo();
        Flower.Play(FlowStatus.Success, "已收藏");
    }



    private void ScrollTo(int index)
    {
        try
        {
            var element = ReplyRepeater.GetOrCreateElement(index);
            var options = new BringIntoViewOptions
            {
                VerticalAlignmentRatio = 0, // 0=顶部对齐，0.5=居中，1=底部
                AnimationDesired = true       // 启用平滑滚动动画
            };
            element.StartBringIntoView(options);
            //对于没有页码的跳转链接暂时没有处理
        }
        catch { }
    }




    private async void PostOperation_Click(object sender, RoutedEventArgs e)
    {
        var operation = sender as MenuFlyoutItem;
        DataPackage pack;
        if (operation?.DataContext is not Reply reply || operation?.Tag is not string tag) return;
        switch (tag)
        {
            case "GIFT":
                await ShowWealthTransferAsync(reply);
                break;
            case "RATE":
                await ShowRatingAsync(reply);
                break;
            case "UBB":
                pack = new DataPackage();
                pack.SetText(reply.Content);
                Clipboard.SetContent(pack);
                Flower.Play(FlowStatus.Success, "已复制为UBB代码");
                break;
            case "MD":
                pack = new DataPackage();
                if (reply.ContentType == (int)ContentType.Ubb)
                {
                    pack.SetText(UbbToMarkdown.Convert(reply.Content, true));
                }
                else
                {
                    pack.SetText(reply.Content);
                }
                Clipboard.SetContent(pack);
                Flower.Play(FlowStatus.Success, "已复制为Markdown文本");
                break;
            case "QUOTE":
                if (reply.IsDeleted)
                {
                    Flower.Play(FlowStatus.Info, "无法回复被删除的帖子");
                    return;
                }
                if (reply.Content != null)
                {
                    var floor = reply.Floor;
                    var page = 1 + floor / 10;
                    var location = floor % 10;
                    var header = $"[b]以下是引用{floor}楼：用户{reply.UserName}在{reply.Time}的发言：[url=/topic/{TopicId}/{page}#{location}]>>查看原帖<<[/url][/b]\r\n";
                    var param = new SketchNavigationInfo
                    {
                        EditorMode = EditorMode.ReplyToPost,
                        TopicId = TopicId,
                        QuoteHeader = $"[quote]{header}{reply.Content}[/quote]",
                        ParentId = reply.Id,
                        HintText = $"引用{reply.UserName}的回复",
                        Floor = floor
                    };
                    Frame.Navigate(typeof(SketchPage), param);
                }
                break;
            case "EDIT":
                //使用括号放置param变量名重复
                {
                    var param = new SketchNavigationInfo
                    {
                        EditorMode = reply.Floor == 1 ? EditorMode.EditMyTopic : EditorMode.EditMyPost,
                        TopicId = TopicId,
                        BaseText = reply.Content,
                        PostId = reply.Id,
                        HintText = TopicInfo.Title,
                        Floor = reply.Floor,
                        ContentType = reply.ContentType
                    };
                    Frame.Navigate(typeof(SketchPage), param);
                }
                break;
        }
    }
    private void PagerFix()
    {
        Pager.NextButtonVisibility = Pager.NumberOfPages == 1 ?
            PagerControlButtonVisibility.Hidden :
            PagerControlButtonVisibility.Visible;
    }



    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        var b = sender as Button;
        if (b == null) return;
        if (b.DataContext is not Reply reply || b.Tag is not string mode) return;
        // 防重入:请求期间禁用按钮,避免连点并发提交导致状态错乱
        b.IsEnabled = false;
        try
        {
            var postId = reply.Id;
            var url = ApiEndpoints.Post.React(postId);
            var content = new StringContent(mode, Encoding.UTF8, "application/json");
            var result = await ApiService.Put(url, content);
            if (!result.IsSuccess)
            {
                //
                Flower.Play(FlowStatus.Fail, "操作失败");
                return;
            }
            var newStateUrl = ApiEndpoints.Post.ReactionState(postId);
            var newStateResult = await ApiService.Fetch<ReactionState>(newStateUrl);
            if (!newStateResult.IsSuccess || newStateResult.Data == null)
            {
                //
                Flower.Play(FlowStatus.Fail, "获取赞踩数据失败");
                return;
            }
            var newState = newStateResult.Data;
            reply.LikeState = newState.LikeState;
            reply.LikeCount = newState.LikeCount;
            reply.DislikeCount = newState.DislikeCount;
        }
        finally
        {
            b.IsEnabled = true;
        }
    }





    private async void SmallProfile_Loaded(object sender, RoutedEventArgs e)
    {
        var p = sender as PersonPicture;
        if (p?.Tag is not string tag) return;
        try
        {
            var bitmap = await ImageHelper.LoadWebImageAsync(tag);
            // 加载期间 TeachingTip 可能已关闭,不再向已卸载控件赋值
            if (p.IsLoaded) p.ProfilePicture = bitmap;
        }
        catch (Exception ex)
        {
            // 避免 async void 未捕获异常崩溃
            System.Diagnostics.Debug.WriteLine($"加载用户头像失败: {ex.Message}");
        }
    }

    private void SmallProfile_Unloaded(object sender, RoutedEventArgs e)
    {
        var p = sender as PersonPicture;
        p?.ProfilePicture = null;
    }


    // 投票最多可选数(供 SelectionChanged 具名处理器使用)
    private int _voteMaxCount;

    private async Task InitializeVote()
    {
        if (TopicInfo.IsVote)
        {

            var voteUrl = ApiEndpoints.Topic.Vote(TopicId);
            var voteResult = await ApiService.Fetch<VoteInfo>(voteUrl);
            if (!voteResult.IsSuccess || voteResult.Data == null)
            {
                //
                Debug.WriteLine(voteResult.Message);
                Flower.Play(FlowStatus.Fail, "加载投票信息失败");
                return;
            }
            var data = voteResult.Data;
            VoteList.ItemsSource = data.VoteItems;
            VoteList.SelectedItems.Clear();
            var record = data.MyRecord;
            if (record != null)
            {
                foreach (var i in record)
                {
                    // 防御:API 返回的记录索引可能越界
                    var index = i - 1;
                    if (index >= 0 && index < VoteList.Items.Count)
                    {
                        VoteList.SelectedItems.Add(VoteList.Items[index]);
                    }
                }
            }

            _voteMaxCount = data.MaxVoteCount;
            // 具名处理器并先退订再订阅,避免每次初始化都累积匿名 handler
            VoteList.SelectionChanged -= VoteList_SelectionChanged;
            VoteList.SelectionChanged += VoteList_SelectionChanged;

            if (data.CanVote && data.IsAvailable)
            {
                SendVote.IsEnabled = true;
                VoteTitle.Text = "投票(开放中)";
            }
            else
            {
                SendVote.IsEnabled = false;
                VoteList.IsEnabled = false;
                if (data.IsAvailable)
                {
                    VoteTitle.Text = "投票(已投票)";
                }
                else
                {
                    VoteTitle.Text = "投票(已过期)";
                }
            }
            votetime.Text = $"过期时间:{data.ExpiredTime}";
            voteinfo.Text = $"参与人数:{data.VoteUserCount},票数限制:{data.MaxVoteCount}";


        }
    }

    private void VoteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SendVote.IsEnabled = VoteList.SelectedItems.Count <= _voteMaxCount;
    }
    private async void StartVote_Click(object sender, RoutedEventArgs e)
    {
        await InitializeVote();
        VotePanel.IsOpen = true;
    }



    private void VotePanel_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        VoteList.ItemsSource = null;
    }

    private async void SendVote_Click(object sender, RoutedEventArgs e)
    {
        if (VoteList.SelectedItems.Count > 0)
        {
            var list = VoteList.SelectedItems.Select(g => VoteList.Items.IndexOf(g) + 1).ToList();
            var r = await SendVoteResult(TopicId, list);
            if (r == "1")
            {
                Flower.Play(FlowStatus.Success, "投票完成");
                await InitializeVote();
            }
            else
            {
                Flower.Play(FlowStatus.Fail, "投票失败");
            }
        }
        else
        {
            Flower.Play(FlowStatus.Info, "选择至少一项");
        }
    }
    //TODO:需要改成返回一个bool值，表示是否成功。现在的字符串返回值不够语义化。
    public  async Task<string> SendVoteResult(int id, List<int> list)
    {
        var url = ApiEndpoints.Topic.Vote(id);
        var post = new Dictionary<string, object>
        {
            { "items", list }
        };
        var postText = SerializationHelper.TrySerialize(post);
        var requestBody = new StringContent(postText, Encoding.UTF8, "application/json");
        var r = await ApiService.Submit<string>(url, requestBody);
        if (r.IsSuccess) return "1";
        return "0";
    }
    private async void Person_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        try
        {
            var h = sender as HyperlinkButton;
            ProfileViewer.Target = h;
            if (h?.Tag is not Reply t || t.IsAnonymous) return;
            var profileUrl = ApiEndpoints.User.UserProfile(false, t.UserId ?? 0);
            var profileResult = await ApiService.Fetch<UserInfo>(profileUrl);
            if (!profileResult.IsSuccess || profileResult.Data == null)
            {
                return;
            }
            var data = profileResult.Data;
            Profile.Name = data.Name;
            Profile.Id = data.Id;
            Profile.Popularity = data.Popularity;
            Profile.FanCount = data.FanCount;
            Profile.PortraitUrl = data.PortraitUrl;
            Profile.SignatureCode = data.SignatureCode;
            Profile.PostCount = data.PostCount;
            ProfileViewer.IsOpen = true;
        }
        catch (Exception ex)
        {
            //await App.Logger.WriteAsync("Topic", "加载用户信息预览失败", ex.Message);
        }
        finally
        {
            args.Handled = true;
        }
    }




    private static string ExtractImageUrl(UbbNode node)
    {
        var src = "";
        if (node is TagNode tagNode)
        {
            var value = tagNode.GetAttribute("value");
            if (!value.IsValidUrl() || value == "1")
            {
                // 尝试从子节点获取URL（对于 [img]url[/img] 格式）
                var first = node.FirstChild;
                if (first is TextNode textNode)
                {
                    src = textNode.Content;
                }
            }
            else
            {
                src = value;
            }
        }
        return src;
    }

    private void ProfileViewer_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        ProfileViewer.Target = null;  // 关键：清理 Target 引用
        ProfileViewer.Tag = null;
    }


    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as HyperlinkButton;
        // 显示附加的 Flyout
        FlyoutBase.ShowAttachedFlyout(button);
    }

    
}