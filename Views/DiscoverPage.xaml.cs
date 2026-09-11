using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Windows.Storage;
using CC98.Kernel;
using CC98.Objects;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Dispatching;
using CC98.Services.Extensions;
using CC98.Services.Helpers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     发现页面。
/// </summary>
public sealed partial class DiscoverPage : Page
{
    public Increment Increment = new(20);
    public ObservableCollection<SimpleTopicInfo> RandomTopics = [];
    public ApplicationDataContainer Set = ApplicationData.Current.LocalSettings;
    public HashSet<int> TopicIds = [];
    public ObservableCollection<TopicInfo> Topics = [];
    public ApiService ApiService = App.Current.GetService<ApiService>();

    // 记录离开本页时的垂直滚动偏移,用于从主题页返回时恢复位置
    private double _savedScrollOffset;
    public DiscoverPage()
    {
        InitializeComponent();
        SizeChanged += Discover_SizeChanged;
    }

    private void Discover_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var size = e.NewSize;
        var isWidthEnough = size.Width > 800;
        RefRandomTiles.Visibility = isWidthEnough ? Visibility.Visible : Visibility.Collapsed;
        RandomTopicViewer.Visibility = isWidthEnough ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(NewTopicViewer, isWidthEnough ? 1 : 2);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SizeChanged -= Discover_SizeChanged;
        SizeChanged += Discover_SizeChanged;

        // 从主题页返回时:页面实例已被缓存(NavigationCacheMode=Required),已加载的数据仍在。
        // 此处不能清空列表——清空会让 ItemsRepeater 内容归零、滚动区域高度塌缩、偏移被钳制为 0,
        // 且重新加载只取第一页,导致无法回到原来的滚动位置。故仅恢复滚动位置后直接返回。
        if (e.NavigationMode == NavigationMode.Back && Topics.Count > 0)
        {
            RestoreScrollOffset();
            return;
        }

        // 新进入页面时才刷新数据,避免 RandomTopics/Topics 无界增长(随机接口还会返回重复项)。
        RandomTopics.Clear();
        Topics.Clear();
        TopicIds.Clear();
        Increment.Clear();
        await Task.WhenAll(GetNewTopic(), GetRandomTile());
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SizeChanged -= Discover_SizeChanged;
        // 记录离开时的滚动位置,供返回时恢复
        _savedScrollOffset = NewTopicViewer.VerticalOffset;
    }

    /// <summary>
    /// 恢复离开本页前的垂直滚动位置。
    /// 需等待重新挂载后的布局完成,否则 ExtentHeight 仍为 0,设置的偏移会被钳制为 0。
    /// </summary>
    private void RestoreScrollOffset()
    {
        if (_savedScrollOffset <= 0) return;

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            NewTopicViewer.ChangeView(null, _savedScrollOffset, null, true);
        });
    }

    private async Task<bool> GetNewTopic()
    {
        var newTopicUrl = ApiEndpoints.Topic.NewTopicList(Increment.StartIndex);
        var newTopicResult = await ApiService.Fetch<List<TopicInfo>>(newTopicUrl);
        if (!newTopicResult.IsSuccess || newTopicResult.Data == null)
        {
            //忽略加载过快报错
            if (newTopicResult.StatusCode == (int)HttpStatusCode.Forbidden) return false;
            Flower.Play(FlowStatus.Fail, newTopicResult.Message);
            //await App.Logger.WriteAsync("Discover", "加载新帖失败", newTopicResult.Message);
            return false;
        }

        var data = newTopicResult.Data;
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
        TopicIds.AddRange(data.Select(x => x.Id));
        DiscoverEmptyState.Visibility = Topics.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        return true;
    }


    private async Task GetRandomTile()
    {
        var randomTopicUrl = ApiEndpoints.Topic.RandomTopicList();
        var randomTopicResult = await ApiService.Fetch<List<SimpleTopicInfo>>(randomTopicUrl);
        if (!randomTopicResult.IsSuccess || randomTopicResult.Data == null) return;
        var data = randomTopicResult.Data;
        RandomTopics.AddRange(data);
    }

    private async void RefRandomTiles_Click(object sender, RoutedEventArgs e)
    {
        RandomTopics.Clear();
        await GetRandomTile();
    }


    private void RandomPost_Click(object sender, RoutedEventArgs e)
    {
        var h = sender as HyperlinkButton;
        if (h != null)
        {
            var p = h?.DataContext as SimpleTopicInfo;
            if (p != null)
            {
                var param = new TopicNavigationInfo { TopicId = p.Id };
                Frame.Navigate(typeof(TopicPage), param);
            }
        }
    }


    private async void ItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        await Increment.LoadMore(args.Index, GetNewTopic);
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

    private void ContentCard_Loaded(object sender, RoutedEventArgs e)
    {
    }
}