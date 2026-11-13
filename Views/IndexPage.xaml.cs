using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using CC98.Kernel;
using CC98.Objects;
using CC98.Services;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CC98.Services.Extensions;
using CC98.Services.Helpers;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;
using CC98.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class IndexPage
{
    private readonly IndexDataService _indexService = IndexDataService.Instance;
    public ObservableCollection<FlipTopic> FlipTopics = [];
    public ObservableCollection<SectionCard> Sections = [];

    public IndexPage()
    {
        InitializeComponent();
    }


    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        //首次进入时先展示加载环,避免瀑布流渲染前出现空白
        ShowLoadingIndicator();
        var url = ApiEndpoints.Forum.Index;
        _ = IndexDataService.Instance.RefreshFromApiAsync(url);
        try
        {
            await LoadFromCacheAsync();
        }
        finally
        {
            //数据未就绪或加载失败时也要收起加载环,不能让它一直转
            HideLoadingIndicator();
        }

        if (!Sections.Any())
        {
            Tip.Show(InfoType.Error, "首页加载失败,请点击刷新重试");
        }
    }

    /// <summary>
    ///     显示加载指示器。首次进入或手动刷新时,在瀑布流内容渲染完成前展示,避免空白期。
    /// </summary>
    private void ShowLoadingIndicator()
    {
        LoadingRing.IsActive = true;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>
    ///     隐藏加载指示器。先强制完成一次布局,确保瀑布流内容已经真正生成并绘制,再移除加载环,
    ///     避免"加载环已消失但内容还没画出来"的短暂空白。
    /// </summary>
    private void HideLoadingIndicator()
    {
        RootGrid.UpdateLayout();
        LoadingRing.IsActive = false;
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task LoadFromCacheAsync()
    {
        //只从缓存中读取。
        Sections.Clear();
        FlipTopics.Clear();
        var sections = await IndexDataService.GetSectionsAsync();
        var recommendations = await IndexDataService.GetRecommendationReadingAsync();
        if (sections.Any() && recommendations.Any())
        {
            Sections.AddRange(sections);
            AddRecommendations(recommendations);
            Pips.NumberOfPages = recommendations.Count();
            return;
        }

        //缓存为空或推荐列表为空:只刷新一次并直接渲染,避免无限递归。
        var url = ApiEndpoints.Forum.Index;
        var success = await IndexDataService.Instance.RefreshFromApiAsync(url);
        if (!success) return;
        var freshSections = await IndexDataService.GetSectionsAsync();
        var freshRecommendations = await IndexDataService.GetRecommendationReadingAsync();
        Sections.AddRange(freshSections);
        AddRecommendations(freshRecommendations);
        Pips.NumberOfPages = freshRecommendations.Count();
    }

    /// <summary>
    /// 复制推荐项并加上协议前缀。不能直接修改缓存对象:JsonFileCache 使用 NeverRemove 的
    /// MemoryCache,对象跨导航共享,直接加前缀会导致二次进入时前缀叠加(cc98:/cc98://...)。
    /// </summary>
    private void AddRecommendations(IEnumerable<FlipTopic> recommendations)
    {
        foreach (var item in recommendations)
        {
            FlipTopics.Add(new FlipTopic
            {
                Title = item.Title,
                Content = item.Content,
                Time = item.Time,
                Url = string.IsNullOrEmpty(item.Url) ? item.Url : $"cc98:/{item.Url}"
            });
        }
    }


    private void ContentCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var h = sender as HyperlinkButton;
        var translate = h?.RenderTransform as TranslateTransform;
        AnimationHelper.AnimateCard(translate, 0, -5); // 向上方移动
    }

    private void ContentCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        var h = sender as HyperlinkButton;
        var translate = h?.RenderTransform as TranslateTransform;
        AnimationHelper.AnimateCard(translate, 0, 0); // 恢复原位
    }

    private void TopicItem_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as HyperlinkButton;
        var tag = button?.Tag;
        if (tag == null) return;
        var param = new TopicNavigationInfo { TopicId = tag.ToInt() };
        Frame.Navigate(typeof(TopicPage), param);
    }


    private void RecomHyperlink_Click(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        if (sender.NavigateUri == null) return;
        var url = sender.NavigateUri.ToString();
        //兼容 cc98://topic/123 与历史遗留的 cc98:/cc98://topic/123 前缀叠加形式,取末尾数字,避免 int.Parse 抛异常
        var match = System.Text.RegularExpressions.Regex.Match(url, @"(\d+)\s*$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var topicId)) return;
        var param = new TopicNavigationInfo { TopicId = topicId };
        Frame.Navigate(typeof(TopicPage), param);
    }

    private async void IndexAction_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as AppBarButton;
        if (button == null) return;
        if (button.Tag is not string tag) return;
        switch (tag)
        {
            case "refresh":
                ShowLoadingIndicator();
                try
                {
                    var url = ApiEndpoints.Forum.Index;
                    var success = await IndexDataService.Instance.RefreshFromApiAsync(url);
                    if (success)
                        await LoadFromCacheAsync();
                    else
                        Tip.Show(InfoType.Error, "刷新首页失败");
                }
                finally
                {
                    HideLoadingIndicator();
                }

                break;
            case "search":
                Frame.Navigate(typeof(SearchPage));
                break;
            case "appcenter":
                await Launcher.LaunchUriAsync(new(ApiEndpoints.Forum.AppCenter));
                break;
            case "lottery":
                //网页端OpenID未注册权限，不支持抽卡
                if (AppSettings.Current.ActiveMode == (int)ActiveMode.Password)
                {
                    Tip.Show(InfoType.Information, "当前登录方式不支持抽卡");
                    return;
                }

                Frame.Navigate(typeof(GamePage));
                break;
            case "stat":
                ForumStat.XamlRoot = RootGrid.XamlRoot;
                ForumStat.IsOpen = true;
                await LoadForumStat();
                break;
        }
    }

    private async Task LoadForumStat()
    {
        var data = await IndexDataService.LoadFromCacheAsync();
        if (data == null) return;
        ForumStatList.ItemsSource = new List<CardStatInfoPair>
        {
            new() { StatItem = "今日帖数", Value = data.TodayCount },
            new() { StatItem = "今日主题数", Value = data.TodayTopicCount },
            new() { StatItem = "全站帖数", Value = data.PostCount },
            new() { StatItem = "全站话题", Value = data.TopicCount },
            new() { StatItem = "在线用户", Value = data.OnlineUserCount },
            new() { StatItem = "全站用户", Value = data.UserCount }
        };
        welcome.Text = $"欢迎新用户 {data.LastUserName}";
    }

    private void ForumStat_Unloaded(object sender, RoutedEventArgs e)
    {
        welcome.Text = "";
        ForumStatList.ItemsSource = null;
        ForumStat.Target = null;
    }
}