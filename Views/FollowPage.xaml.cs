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
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     关注页面。
/// </summary>
public sealed partial class FollowPage : Page
{
    public ObservableCollection<Friend> Friends = [];
    public Increment Increment = new();
    public string Type = "follower";
    public ApiService ApiService = App.Current.GetService<ApiService>();
    public FollowPage()
    {
        InitializeComponent();
    }


    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 本页设置了 NavigationCacheMode="Required",页面实例会被缓存:
        // 返回时 Type 与已加载的列表都还在,直接沿用即可,不要重新加载。
        if (e.NavigationMode == NavigationMode.Back) return;

        // 前进进入:按导航参数重新设定类型(同一种页面会由不同入口用不同参数打开)。
        Type = e.TryGetParameter<string>() ?? "follower";
        FriendType.Text = Type == "follower" ? "粉丝" : "关注";

        // 复用缓存实例时必须先清空,否则 LoadFriend 会往旧集合里追加、并从非 0 页开始取数据。
        Increment.Clear();
        Friends.Clear();

        await LoadFriend();
    }


    private async Task<bool> LoadFriend()
    {
        //获取好友ID列表
        var friendListUrl = ApiEndpoints.User.FreiendList(Type, Increment.StartIndex);
        var friendIdsResult = await ApiService.Fetch<List<int>>(friendListUrl);
        //处理第一层异常
        if (!friendIdsResult.IsSuccess || friendIdsResult.Data == null)
        {
            //
            Flower.Play(FlowStatus.Fail, "加载好友Id列表失败");
            //await App.Logger.WriteAsync("Follow", "加载好友Id列表失败", friendIdsResult.Message);
            return false;
        }

        var ids = friendIdsResult.Data;
        Increment.HasMore = ids.Count == Increment.PageSize;
        if (!Increment.HasMore) Flower.Play(FlowStatus.Info, "已全部加载");
        var param = string.Join("&", ids.Select(id => $"id={id}"));
        var userInfoUrl = ApiEndpoints.User.UserInfoList(param);

        // 获取好友详情
        var friendsResult = await ApiService.Fetch<List<Friend>>(userInfoUrl);
        if (!friendsResult.IsSuccess || friendsResult.Data == null)
        {
            //
            Flower.Play(FlowStatus.Fail, "加载好友信息失败");
            //await App.Logger.WriteAsync("NoticeMsg", "加载好友信息失败", friendsResult.Message);
            return false;
        }
        var data = friendsResult.Data;
        foreach (var friend in data)
        {
            friend.IsFollowee = Type == "followee";
        }
        Friends.AddRange(data);
        FollowEmptyState.Visibility = Friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        return true;
    }

    private void TileContent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton button || button.Tag is not int userId) return;
        var param = new ProfileNavigationInfo { IsMe = false, UserId = userId };
        Frame.Navigate(typeof(ProfilePage), param);
    }


    private async void UnFollow_Click(object sender, RoutedEventArgs e)
    {
        var m = sender as MenuFlyoutItem;
        if (m?.DataContext is not Friend friend) return;
        var url = ApiEndpoints.User.EditFollowee(friend.Id);
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var result = await ApiService.Put(url, content);
        if(result.IsSuccess)
        {
            Friends.Remove(friend);
            Flower.Play(FlowStatus.Success, $"已取关用户:{friend.Name}");
        }
        else
        {
            Flower.Play(FlowStatus.Fail, "取消关注失败");
        }
    }

    private void Chat_Click(object sender, RoutedEventArgs e)
    {
        var m = sender as MenuFlyoutItem;
        if (m?.DataContext is not Friend friend) return;
        var c = new ChatInfo { UserId = friend.Id, Name = friend.Name, PortraitUrl = friend.PortraitUrl };
        var param = new ChatNavigationInfo { HasTarget = true, ChatUserInfo = c };
        Frame.Navigate(typeof(ChatPage), param);
    }

    private async void FriendRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        await Increment.LoadMore(args.Index, LoadFriend);
    }
}