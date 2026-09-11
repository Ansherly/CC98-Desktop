using CC98.Controls.Primitives;
using CC98.Controls.UbbTextBlock;
using CC98.Controls.UbbTextBlock.Common.Events;
using CC98.Controls.UbbTextBlock.Parser;
using CC98.Kernel;
using CC98.Objects;
using CC98.Services;
using CC98.Services.Extensions;
using CC98.Services.Helpers;
using DevWinUI;
using FluentIcons.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
/// 用户资料页面。
/// </summary>
public sealed partial class ProfilePage : Page
{
    public ObservableCollection<SimpleTopicInfo> RecentTopics = [];
    public ApplicationDataContainer Set = ApplicationData.Current.LocalSettings;
    public ApiService ApiService=App.Current.GetService<ApiService>();
    public UserInfo UserProfile { get; } = new()
    {
        Id = 0,
        Name = "未知用户",
        PortraitUrl = "",
        IsFollowing = false,
    };
    public bool IsMe = false;
    public int UserId = 0;
    public Increment Increment = new();
    public ProfilePage()
    {
        InitializeComponent();
    }
    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 获取传递的参数
        var args = e.TryGetParameter<ProfileNavigationInfo>();
        if (args != null)
        {
            UserId = args.UserId;
            IsMe = args.IsMe;
            // 用户资料与最近主题互不依赖,并行加载
            await Task.WhenAll(LoadUserProfile(), LoadRecentTopic());
            if (IsMe) await SignIn();
        }
    }
    private async Task SignIn()
    {
        //此方法本可以使用GET进行判断再签到，会额外多一次请求
        //从规范的角度讲，其实服务器应该合并GET/POST到一个请求
        var url = ApiEndpoints.User.SignIn();
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var result = await ApiService.Submit<JsonElement>(url, content);
        if (result.IsSuccess)
        {
            SignStatus.Text = "已签到";
            SignStatusIcon.IconVariant = IconVariant.Filled;
            var element = result.Data;
            if (element.ValueKind == JsonValueKind.Number)
            {
                var wealth = element.GetRawText();
                Flower.Play(FlowStatus.Success, $"签到成功，获得财富值: {wealth}");
            }
            //理论上不会出现非数字的情况
            else
            {
                Flower.Play(FlowStatus.Success, "签到成功");
            }
            return;
        }
        //此信息在errorContent中，由result.Message传递
        if (result.StatusCode == (int)HttpStatusCode.BadRequest)
        {
            var info = result.Message;
            if (info == "has_signed_in_today")
            {
                SignStatus.Text = "已签到";
                SignStatusIcon.IconVariant = IconVariant.Filled;
                return;
            }
        }
        SignStatus.Text = "签到失败";
        SignStatusIcon.IconVariant = IconVariant.Regular;
    }
    //用户个人页面检查跳转参数
    private async Task LoadUserProfile()
    {
        var UserProfileUrl = ApiEndpoints.User.UserProfile(IsMe, UserId);
        var UserProfileResult = await ApiService.Fetch<UserInfo>(UserProfileUrl);
        if (!UserProfileResult.IsSuccess || UserProfileResult.Data == null)
        {
            return;
        }
        var data = UserProfileResult.Data;
        UserProfile.Name = data.Name;
        UserProfile.Id = data.Id;
        UserProfile.Popularity = data.Popularity;
        UserProfile.FanCount = data.FanCount;
        UserProfile.FollowCount = data.FollowCount;
        UserProfile.LastLogOnTime = data.LastLogOnTime;
        UserProfile.PortraitUrl = data.PortraitUrl;
        UserProfile.SignatureCode = data.SignatureCode;
        UserProfile.PostCount = data.PostCount;
        UserProfile.Wealth = data.Wealth;
        UserProfile.RegisterTime = data.RegisterTime;
        UserProfile.IsFollowing = data.IsFollowing;
        UserProfile.IsMe = IsMe;
        try
        {
            var source = await Services.Helpers.ImageHelper.LoadWebImageAsync(UserProfile.PortraitUrl);
            MyProfile.ProfilePicture = source;
        }
        catch (Exception ex)
        {
            Flower.Play(FlowStatus.Fail, $"加载主页失败: {ex.Message}"); 
        }
    }
    private async Task<bool> LoadRecentTopic()
    {
        var recentTopicUrl = ApiEndpoints.Topic.RecentTopic(IsMe, UserId, Increment.StartIndex);
        var recentTopicResult = await ApiService.Fetch<List<SimpleTopicInfo>>(recentTopicUrl);
        if (!recentTopicResult.IsSuccess || recentTopicResult.Data == null)
        {
            Flower.Play(FlowStatus.Fail, recentTopicResult.Message);
            return false;
        }
        var data = recentTopicResult.Data;
        //移除末尾
        if (data.Count == 11) data.RemoveAt(10);
        Increment.HasMore = data.Count == Increment.PageSize;
        RecentTopics.AddRange(data);
        ProfileEmptyState.Visibility = RecentTopics.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        return true;
    }

    private void STileButton_Click(object sender, RoutedEventArgs e)
    {

        if (sender is not HyperlinkButton button || button.Tag is not int topicId) return;
        var param = new TopicNavigationInfo { TopicId = topicId };
        Frame.Navigate(typeof(TopicPage), param);
    }





    private void FollowList_Click(object sender, RoutedEventArgs e)
    {
        if (!IsMe) return;
        var h = sender as HyperlinkButton;
        if (h?.Tag is not string tag) return;
        Frame.Navigate(typeof(FollowPage), tag);
    }



    private void StartChat_Click(object sender, RoutedEventArgs e)
    {
        var c = new ChatInfo { UserId = UserProfile.Id, Name = UserProfile.Name, PortraitUrl = UserProfile.PortraitUrl };
        var param = new ChatNavigationInfo { ChatUserInfo = c, HasTarget = true };
        Frame.Navigate(typeof(ChatPage), param);
    }

    private async void Follow_Click(object sender, RoutedEventArgs e)
    {
        Follow.IsEnabled = false;
        var isFollowing = UserProfile.IsFollowing;
        var url = ApiEndpoints.User.EditFollowee(UserProfile.Id);
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var result = isFollowing ? await ApiService.Delete(url) : await ApiService.Put(url, content);
        if (result.IsSuccess)
        {
            UserProfile.IsFollowing = !UserProfile.IsFollowing;
            Flower.Play(FlowStatus.Success, isFollowing ? "已取消关注" : "已关注");
        }
        Follow.IsEnabled = true;
    }

    private async void RecentTopicRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        await Increment.LoadMore(args.Index, LoadRecentTopic);
    }

    private async void UbbTextBlock_MediaClicked(object sender, MediaClickEventArgs e)
    {
        var context = new LinkContext
        {
            Frame = Frame,
            CurrentTopicId = null,
            JumpToFloor = null,
            ImageList = null,
            Flower = Flower
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
}