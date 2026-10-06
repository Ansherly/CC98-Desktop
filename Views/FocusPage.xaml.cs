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
using Microsoft.UI.Dispatching;
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

    // 离开本页时的滚动位置(本页是 NavigationCacheMode=Required,返回时要放回原位)
    private double _savedScrollOffset;

    public FocusPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 本页设置了 NavigationCacheMode="Required",页面实例会被缓存:
        // 返回时选中的标签与已加载的数据都还在,直接沿用即可,不要重新加载。
        if (e.NavigationMode == NavigationMode.Back)
        {
            // 缓存实例重新挂载时滚动位置可能被重置,这里显式放回离开时的位置
            RestoreScrollOffset();
            return;
        }

        // 前进进入(例如从导航菜单再次点击"动态"):缓存实例上残留着上次的标签与集合状态。
        // 选中状态有两份,必须一起复位:
        //   · SelectorBarItem.IsSelected —— 下划线/视觉;
        //   · SelectorBar.SelectedItem —— 逻辑选中项,TypeChoice_SelectionChanged 读的是它。
        // 只改其中一个就会错位成"下划线停在上次那个标签、内容却已经回到第一项"。
        // 两个属性都改完之后由这里显式加载,不再依赖赋值 SelectedItem 的副作用。
        _savedScrollOffset = 0;
        SelectFirstTab();

        Increment.Clear();
        Topics.Clear();
        TopicIds.Clear();
        Mode = FocusContentType.Followee;
        await GetMoments();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // 记录离开时的滚动位置,供返回本页时恢复(与 DiscoverPage 的处理一致)
        _savedScrollOffset = ContentScrollViewer.VerticalOffset;
    }

    /// <summary>
    ///     恢复离开本页前的滚动位置。需等重新挂载后的布局完成,否则设置的偏移会被钳制为 0;
    ///     若框架本身就保留了偏移,这里等价于原地不动。
    /// </summary>
    private void RestoreScrollOffset()
    {
        if (_savedScrollOffset <= 0) return;

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            ContentScrollViewer.ChangeView(null, _savedScrollOffset, null, true);
        });
    }

    /// <summary>
    ///     把标签复位到第一项。选中状态有两份,必须一起改:<c>SelectorBarItem.IsSelected</c> 决定下划线的位置,
    ///     <c>SelectedItem</c> 是逻辑选中项(事件处理器读的是它)。只改一个就会出现错位。
    ///     置抑制标志是为了让这次程序化改动不触发 <see cref="TypeChoice_SelectionChanged" /> —— 加载由调用方显式完成。
    /// </summary>
    private void SelectFirstTab()
    {
        _suppressTypeChange = true;
        Navibar.Items[0].IsSelected = true;
        Navibar.SelectedItem = Navibar.Items[0];
        _suppressTypeChange = false;
    }

    // 程序化复位标签时置位,用于抑制 TypeChoice_SelectionChanged 触发的加载
    private bool _suppressTypeChange;

    private async Task<bool> GetMoments()
    {
        // Required 缓存页伴生命全周期:达到保留上限后停止增量加载(不要头部移除,
        // 那会使 ItemsRepeater 索引位移,触发元素批量重新 Prepare 与加载风暴)
        if (Topics.Count >= MaxTopicsKept)
        {
            Increment.HasMore = false;
            return true;
        }

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

    // Required 缓存页伴生命全周期:常驻条数上限(到达后停止增量加载)
    private const int MaxTopicsKept = 300;


    private async void TypeChoice_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // 程序化复位(SelectFirstTab)不会走到这里
        if (_suppressTypeChange) return;

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