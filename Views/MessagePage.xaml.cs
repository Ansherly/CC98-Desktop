using CC98.Kernel;
using CC98.Objects;
using CC98.Services;
using CC98.Services.Extensions;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
/// 消息页面。
/// </summary>
public sealed partial class MessagePage
{
    public GlobalService GlobalService = GlobalService.Instance;
    public Increment Increment = new();
    public ObservableCollection<Notice> Notices = [];
    public NoticeType noticeType = NoticeType.System;
    public ApiService ApiService = App.Current.GetService<ApiService>();
    public MessagePage()
    {
        InitializeComponent();
    }
    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 本页设置了 NavigationCacheMode="Required",页面实例会被缓存。
        if (e.NavigationMode == NavigationMode.Back)
        {
            // 返回时选中标签与已加载列表都还在,一般直接沿用;
            // 但"私信"标签只是跳转入口(选中即导航到 ChatPage),不承载列表,
            // 从 ChatPage 返回后若仍停在它上面,界面会与列表内容不符,故回到第一个内容标签。
            if ((NaviBar.SelectedItem as SelectorBarItem)?.Tag is "Chat")
                NaviBar.SelectedItem = NaviBar.Items[0]; // 触发 SelectionChanged 完成加载
            return;
        }

        // 前进进入:回到第一个内容标签。选中项变化时由 NaviBar_SelectionChanged 完成加载;
        // 若本来就停在第一个标签(缓存实例),事件不会触发,需显式重新加载。
        if (!ReferenceEquals(NaviBar.SelectedItem, NaviBar.Items[0]))
        {
            NaviBar.SelectedItem = NaviBar.Items[0];
            return;
        }

        await ReloadNoticesAsync();
    }

    private async void NaviBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var bar = NaviBar.SelectedItem;
        if (bar?.Tag is "Chat")
        {
            // "私信"标签是跳转入口:选中即进入聊天页,本页不加载列表。
            Frame.Navigate(typeof(ChatPage));
            return;
        }

        await ReloadNoticesAsync();
    }

    /// <summary>
    /// 按当前选中的标签设置通知类型并重新加载列表。
    /// 缓存实例上会残留上次的集合与分页游标,故加载前必须清空。
    /// </summary>
    private async Task ReloadNoticesAsync()
    {
        var bar = NaviBar.SelectedItem;
        if (bar?.Tag is not string tag) return;

        switch (tag)
        {
            case "System":
                noticeType = NoticeType.System;
                MsgCount.Text = $"{GlobalService.SystemCount}条未读信息";
                break;
            case "Reply":
                noticeType = NoticeType.Reply;
                MsgCount.Text = $"{GlobalService.ReplyCount}条未读信息";
                break;
            case "At":
                noticeType = NoticeType.At;
                MsgCount.Text = $"{GlobalService.AtCount}条未读信息";
                break;
            case "Chat":
                return; // 跳转入口,不在此加载
        }

        Increment.Clear();
        Notices.Clear();
        await GetNotice();
    }

    public static string GetTypeName(NoticeType type)
    {
        return type switch
        {
            NoticeType.System => "system",
            NoticeType.At => "at",
            NoticeType.Reply => "reply",
            _ => ""
        };
    }
    private async Task<bool> GetNotice()
    {
        var url = ApiEndpoints.User.SystemNotice(GetTypeName(noticeType), Increment.StartIndex);
        var result = await ApiService.Fetch<List<Notice>>(url);
        if (!result.IsSuccess || result.Data == null)
        {
            //
            Flower.Play(FlowStatus.Fail, "加载通知失败");
            //await App.Logger.WriteAsync("NoticeMsg", "加载通知失败", result.Message);
            return false;
        }

        var data = result.Data;
        Increment.HasMore = data.Count == Increment.PageSize;
        if (!Increment.HasMore) Flower.Play(FlowStatus.Info, "没有更多通知了");
        if (noticeType == NoticeType.Reply || noticeType == NoticeType.At)
        {
            var topicIds = data.Where(n => n.TopicId.HasValue).Select(n => n.TopicId!.Value).Distinct().ToList();
            var topicInfos = await GetBasicTopicInfo(topicIds);
            foreach (var notice in data)
            {
                var info = topicInfos.FirstOrDefault(t => t.Id == notice.TopicId);
                if (info != null)
                {
                    var operation = noticeType == NoticeType.Reply ? "回复" : "@";
                    var content = $"在帖子《{info.Title}》的{notice.PostBasicInfo?.Floor}L{operation}了你。";
                    notice.Content = content;
                }
            }
        }

        Notices.AddRange(data);
        MessageEmptyState.Visibility = Notices.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        return true;
    }


    private async Task<List<BasicTopicInfo>> GetBasicTopicInfo(List<int> topicIds)
    {
        var param = string.Join("&", topicIds.Select(id => $"id={id}"));
        var url = ApiEndpoints.Topic.BasicTopicInfoList(param);
        var result = await ApiService.Fetch<List<BasicTopicInfo>>(url);
        if (!result.IsSuccess || result.Data == null)
        {
            //
            Flower.Play(FlowStatus.Fail, "获取帖子基本信息失败");
            //await App.Logger.WriteAsync("NoticeMsg", "获取帖子基本信息失败", result.Message);
            return [];
        }

        var data = result.Data;
        return data;
    }

    private async void NoticeRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        await Increment.LoadMore(args.Index, GetNotice);
    }

    private void NoticeCard_Click(object sender, RoutedEventArgs e)
    {
        var h = sender as HyperlinkButton;
        if (h?.DataContext is not Notice n) return;
        if (n.TopicId is not int topicId || n.PostBasicInfo == null) return;
        if (n.PostBasicInfo.IsDeleted)
        {
            // 已删除的帖子不再跳转
            Flower.Play(FlowStatus.Info, "该帖子已被删除");
            return;
        }
        var param = new TopicNavigationInfo
        {
            IsJumpingMode = true,
            TargetFloor = n.PostBasicInfo.Floor,
            TopicId = topicId
        };
        Frame.Navigate(typeof(TopicPage), param);
    }
}