using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using CC98.Kernel;
using CC98.Objects;
using CC98.Services;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using CC98.Services.Extensions;
using Microsoft.UI.Xaml.Navigation;
using CC98.Services.Helpers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     关注页面。
/// </summary>
public sealed partial class FocusPage : Page
{
    public Increment Increment = new(20, 0, true);
    public FocusContentType Mode = FocusContentType.Followee;
    public ApplicationDataContainer Set = ApplicationData.Current.LocalSettings;
    public HashSet<int> TopicIds = [];
    public ObservableCollection<TopicInfo> Topics = [];
    public ApiService ApiService = App.Current.GetService<ApiService>();
    public FocusPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 本页设置了 NavigationCacheMode="Required",页面实例会被缓存:
        // 返回时选中的标签与已加载的数据都还在,直接沿用即可,不要重新加载。
        if (e.NavigationMode == NavigationMode.Back) return;

        // 前进进入(例如从导航菜单再次点击"动态"):缓存实例上会残留上次的集合与分页游标,
        // 必须先重置,否则 GetMoments 会往旧集合里追加、并从非 0 页开始取数据。
        Mode = FocusContentType.Followee;
        if (!ReferenceEquals(Navibar.SelectedItem, Navibar.Items[0]))
        {
            // 切换选中项会触发 TypeChoice_SelectionChanged,由它完成清空与加载,
            // 这里直接返回以避免重复请求。
            Navibar.SelectedItem = Navibar.Items[0];
            return;
        }

        Increment.Clear();
        Topics.Clear();
        TopicIds.Clear();
        await GetMoments();
    }


    private async Task<bool> GetMoments()
    {
        var url = Mode == FocusContentType.Followee
            ? ApiEndpoints.User.Moment(Increment.StartIndex)
            : ApiEndpoints.User.FavoriteTopicUpdate(Increment.StartIndex);
        var result = await ApiService.Fetch<List<TopicInfo>>(url);
        if (!result.IsSuccess || result.Data == null)
        {
            //
            Flower.Play(FlowStatus.Fail, "加载动态失败");
            //await App.Logger.WriteAsync("Focus", "加载动态失败", result.Message);
            return false;
        }

        var data = result.Data;
        Increment.HasMore = data.Count == Increment.PageSize;

        var param = string.Join("&",
            data.Where(x => !x.IsAnonymous && x.UserId.HasValue).Select(x => $"id={x.UserId}").Distinct());
        var userInfoUrl = ApiEndpoints.User.BasicUserInfoList(param);
        var userInfoResult = await ApiService.Fetch<List<BasicUserInfo>>(userInfoUrl);
        if (!userInfoResult.IsSuccess || userInfoResult.Data == null)
            //报错
            Flower.Play(FlowStatus.Fail, "获取用户头像出错");
        var userInfoList = userInfoResult.Data;
        foreach (var topic in data)
        {
            if (topic.IsAnonymous)
            {
                topic.PortraitUrl = "ms-appx:///Assets/hide.gif";
                //跳过
                continue;
            }

            var user = userInfoList?.FirstOrDefault(x => x.Id == topic.UserId);
            if (user != null) topic.PortraitUrl = user.PortraitUrl;
        }

        data = [.. data.Where(x => !TopicIds.Contains(x.Id))];
        Topics.AddRange(data);
        FocusEmptyState.Visibility = Topics.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        TopicIds.AddRange(data.Select(x => x.Id));
        return true;
    }


    private async void TypeChoice_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var s = Navibar.SelectedItem;
        if (s?.Tag is not string tag) return;
        Mode = tag == "0" ? FocusContentType.Followee : FocusContentType.FavoriteUpdate;
        Increment.Clear();
        Topics.Clear();
        TopicIds.Clear();
        await GetMoments();
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        var h = sender as HyperlinkButton;
        if (h?.DataContext is not TopicInfo s) return;
        var param = new TopicNavigationInfo { TopicId = s.Id };
        Frame.Navigate(typeof(TopicPage), param);
    }

    private async void MomentRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        await Increment.LoadMore(args.Index, GetMoments);
    }

    private void ContentCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var h = sender as Grid;
        var translate = h?.RenderTransform as TranslateTransform;
        AnimationHelper.AnimateCard(translate!, 0, -5); // 向上方移动
    }

    private void ContentCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        var h = sender as Grid;
        var translate = h?.RenderTransform as TranslateTransform;
        AnimationHelper.AnimateCard(translate!, 0, 0); // 恢复原位
    }

    private void ContentCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var h = sender as Grid;
        var tag = h?.Tag;
        if (tag == null) return;
        var param = new TopicNavigationInfo { TopicId = tag.ToInt() };
        Frame.Navigate(typeof(TopicPage), param);
    }
}