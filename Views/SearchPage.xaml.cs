using CC98.Kernel;
using CC98.Objects;
using CC98.Services;
using CC98.Services.Extensions;
using CC98.Services.Helpers;
using DevWinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Windows.Storage;
using static Microsoft.Extensions.Logging.EventSource.LoggingEventSource;

namespace CC98.Views;


/// <summary>
/// 搜索页面:AutoSuggestBox 实时搜索(300ms 防抖)、四状态切换、历史搜索缓存、
/// 下拉推荐(话题/用户/版面/主题ID)、Increment 翻页、结果排序。
/// </summary>
public sealed partial class SearchPage : Page
{
    /// <summary>搜索结果集合(结果卡片绑定)。</summary>
    public ObservableCollection<TopicInfo> SearchResults { get; } = [];
    public ObservableCollection<SearchSuggestion> Suggestions { get; } = [];

    /// <summary>历史搜索关键词。</summary>
    public ObservableCollection<string> SearchHistory { get; } = [];
    /// <summary>当前搜索关键词(供结果卡片高亮绑定)。</summary>
    public string SearchKeyword { get; set; } = "";

    private ApiService ApiService { get; } = App.Current.GetService<ApiService>();

    /// <summary>历史搜索缓存(JsonFileCache 持久化)。</summary>
    private JsonFileCache<List<string>> HistoryCache { get; } = new(
        Path.Combine(ApplicationData.Current.LocalFolder.Path, "SearchHistory.json"));

    /// <summary>话题搜索分页器(PageSize 与 API size=20 一致)。</summary>
    private Increment Pager { get; } = new(20);

    private const int HistoryLimit = 12;
    private string _currentKeyword = "";

    /// <summary>当前排序模式(页面内全局,切换关键词/翻页后保持)。</summary>
    private SearchSortMode _sortMode = SearchSortMode.Default;
    /// <summary>版面缓存(懒加载,用于下拉建议的版面匹配)。</summary>
    private SectionInfo[]? _sections;


    public SearchPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadHistoryAsync();

        try
        {
            var args = e.TryGetParameter<SearchNavigationInfo>();
            if (args != null && !string.IsNullOrWhiteSpace(args.Key))
            {
                SearchBox.Text = args.Key;
                await PerformSearchAsync(args.Key);
            }
            else
            {
                ShowEmptyState();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
        SearchBox.Focus(FocusState.Programmatic);
    }

    #region 历史搜索

    private async Task LoadHistoryAsync()
    {
        var history = await HistoryCache.GetDataAsync();
        if (history == null || history.Count == 0) return;
        foreach (var k in history) SearchHistory.Add(k);
        HistoryLabel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 保存历史搜索:去重、最新在前、限制条数,写 JsonFileCache。
    /// </summary>
    private async Task SaveHistoryAsync(string keyword)
    {
        var list = new List<string> { keyword };
        foreach (var k in SearchHistory.Where(k => !string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase)))
            list.Add(k);
        if (list.Count > HistoryLimit) list = list.Take(HistoryLimit).ToList();

        SearchHistory.Clear();
        foreach (var k in list) SearchHistory.Add(k);
        HistoryLabel.Visibility = Visibility.Visible;
        await HistoryCache.UpdateDataAsync(list);
    }

    #endregion

    #region 搜索

    /// <summary>
    /// 输入变化:仅生成下拉建议并清除旧结果,不自动搜索。
    /// 搜索只在用户按 Enter(QuerySubmitted)或点击下拉"搜索话题"建议(SuggestionChosen)时触发。
    /// </summary>
    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var keyword = sender.Text.Trim();

        if (string.IsNullOrEmpty(keyword))
        {
            return;
        }
        Suggestions.Clear();
        await BuildQuickSuggestions(keyword);

    }
    /// <summary>
    /// 回车提交:立即搜索话题(取消防抖)。
    /// </summary>
    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // 点击建议项会同时触发 SuggestionChosen 与 QuerySubmitted(此时 ChosenSuggestion 非空)。
        // 建议已在 SuggestionChosen 中处理,这里跳过,避免重复发起话题搜索。
        if (args.ChosenSuggestion != null) return;

        var keyword = (args.QueryText ?? sender.Text).Trim();
        if (string.IsNullOrEmpty(keyword)) return;
        await PerformSearchAsync(keyword);
    }

    /// <summary>
    /// 选择下拉建议:按建议类型执行跳转/搜索。
    /// </summary>
    private async void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not SearchSuggestion s) return;
        switch (s.Type)
        {
            case SearchSuggestionType.Topic:
                // 搜索话题:与回车一致
                await PerformSearchAsync(SearchBox.Text.Trim());
                break;
            case SearchSuggestionType.User:
                await OpenUserByNameAsync(s.Parameter ?? SearchBox.Text.Trim());
                break;
            case SearchSuggestionType.UserId:
                if (int.TryParse(s.Parameter, out var uid))
                    Frame.Navigate(typeof(ProfilePage), new ProfileNavigationInfo {IsMe = uid == AppSettings.Current.UserId, UserId = uid });
                break;
            case SearchSuggestionType.Board:
                if (int.TryParse(s.Parameter, out var bid))
                    Frame.Navigate(typeof(BoardPage), bid);
                break;
            case SearchSuggestionType.TopicId:
                if (int.TryParse(s.Parameter, out var tid))
                    Frame.Navigate(typeof(TopicPage), new TopicNavigationInfo { TopicId = tid });
                break;
        }
        Suggestions.Clear();
    }

    /// <summary>
    /// 执行话题搜索(第一页):重置分页器、填充结果、加载头像、更新状态。
    /// </summary>
    private async Task PerformSearchAsync(string keyword, CancellationToken ct = default)
    {
        SearchKeyword = keyword;
        _currentKeyword = keyword;
       
        Pager.Clear();
        SearchResults.Clear();
        ShowLoadingState();

        var url = ApiEndpoints.Topic.SearchTopic(HttpUtility.UrlEncode(keyword), Pager.StartIndex);
        var result = await ApiService.Fetch<List<TopicInfo>>(url, ct);
        if (ct.IsCancellationRequested) return; // 已被更新的输入取消

        if (!result.IsSuccess || result.Data == null)
        {
            Flower.Play(FlowStatus.Fail, $"搜索失败:{result.Message}");
            ShowEmptyState();
            return;
        }

        var data = SortData(result.Data);
        Pager.HasMore = data.Count == Pager.PageSize;
        var param = string.Join("&", data.Where(x => !x.IsAnonymous && x.UserId.HasValue).Select(x => $"id={x.UserId}").Distinct());
        var userInfoUrl = ApiEndpoints.User.BasicUserInfoList(param);
        var userInfoResult = await ApiService.Fetch<List<BasicUserInfo>>(userInfoUrl);
        if (!userInfoResult.IsSuccess || userInfoResult.Data == null)
        {
            //报错
            Flower.Play(FlowStatus.Fail, $"加载用户头像失败：{userInfoResult.Message}");
        }

        var userInfoList = userInfoResult.Data;

        foreach (var topic in data) 
        { 
            topic.SearchKeyword = keyword; 
            if (topic.IsAnonymous)
            {
                topic.PortraitUrl = "ms-appx:///Assets/hide.gif";
            }
            else
            {
                var user = userInfoList?.FirstOrDefault(x => x.Id == topic.UserId);
                if (user != null)
                {
                    topic.PortraitUrl = user.PortraitUrl;
                }
            }
            SearchResults.Add(topic);
        }

        if (data.Count > 0)
        {
            ResultCountText.Text = $"找到 {data.Count}+个结果";
            // 新搜索:清空所有话题浏览 tab,更新搜索 tab 标题并选中
            ClearTopicTabs();
            SearchTab.Header = $"搜索:{keyword}";
            ResultTabView.SelectedItem = SearchTab;
            ShowResultState();
            FadeInContent();
        }
        else
        {
            ShowNoResultState();
        }
        UpdatePagerButtons();

        await SaveHistoryAsync(keyword);
    }

    /// <summary>
    /// 翻页加载回调(供 Increment.LoadNextPage / LoadLastPage 使用)。
    /// </summary>
    private async Task<bool> LoadSearchPageAsync()
    {
        SearchResults.Clear();

        var url = ApiEndpoints.Topic.SearchTopic(HttpUtility.UrlEncode(_currentKeyword), Pager.StartIndex);
        var result = await ApiService.Fetch<List<TopicInfo>>(url);
        if (!result.IsSuccess || result.Data == null) return false;
        var data = SortData(result.Data);
        Pager.HasMore = data.Count == Pager.PageSize;

        var param = string.Join("&", data.Where(x => !x.IsAnonymous && x.UserId.HasValue).Select(x => $"id={x.UserId}").Distinct());
        var userInfoUrl = ApiEndpoints.User.BasicUserInfoList(param);
        var userInfoResult = await ApiService.Fetch<List<BasicUserInfo>>(userInfoUrl);
        if (!userInfoResult.IsSuccess || userInfoResult.Data == null)
        {
            //报错
            Flower.Play(FlowStatus.Fail, $"加载用户头像失败：{userInfoResult.Message}");
        }

        var userInfoList = userInfoResult.Data;

        foreach (var topic in data)
        {
            topic.SearchKeyword = _currentKeyword;
            if (topic.IsAnonymous)
            {
                topic.PortraitUrl = "ms-appx:///Assets/hide.gif";
            }
            else
            {
                var user = userInfoList?.FirstOrDefault(x => x.Id == topic.UserId);
                if (user != null)
                {
                    topic.PortraitUrl = user.PortraitUrl;
                }
            }
            SearchResults.Add(topic);
        }
        ResultCountText.Text = $"找到 {data.Count} 个结果";
        UpdatePagerButtons();
        return true;
    }

    /// <summary>
    /// 按当前排序模式对搜索结果排序。默认模式保持 API 顺序(发帖时间)。
    /// </summary>
    private List<TopicInfo> SortData(List<TopicInfo> data) => _sortMode switch
    {
        SearchSortMode.MostReplies => data.OrderByDescending(t => t.ReplyCount).ToList(),
        SearchSortMode.MostHits => data.OrderByDescending(t => t.HitCount).ToList(),
        _ => data
    };

    /// <summary>
    /// 点击排序按钮:循环切换排序模式,并按新排序重新加载当前关键词第一页。
    /// 排序为页面级全局状态,切换关键词/翻页后保持。
    /// </summary>
    private async void SortButton_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = _sortMode switch
        {
            SearchSortMode.Default => SearchSortMode.MostReplies,
            SearchSortMode.MostReplies => SearchSortMode.MostHits,
            _ => SearchSortMode.Default
        };
        UpdateSortButton();

        // 已有搜索时,按新排序重新加载第一页
        if (!string.IsNullOrEmpty(_currentKeyword))
            await PerformSearchAsync(_currentKeyword);
    }

    /// <summary>更新排序按钮文本。</summary>
    private void UpdateSortButton()
    {
        SortButtonText.Text = _sortMode switch
        {
            SearchSortMode.MostReplies => "排序:回复",
            SearchSortMode.MostHits => "排序:点击",
            _ => "排序:时间"
        };
    }

    /// <summary>
    /// 按用户名搜索用户并打开主页。
    /// </summary>
    private async Task OpenUserByNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var url = ApiEndpoints.User.SearchUserByName(name);
        var result = await ApiService.Fetch<UserInfo>(url);
        if (!result.IsSuccess || result.Data == null || result.Data.Id == 0)
        {
            Flower.Play(FlowStatus.Fail, $"未找到用户:{name}");
            return;
        }
        var user = result.Data;
        var info = new ProfileNavigationInfo
        {
            IsMe = user.Id == AppSettings.Current.UserId,
            UserId = user.Id
        };
        Frame.Navigate(typeof(ProfilePage), info);
    }

  

    #endregion

    #region 下拉建议

    /// <summary>
    /// 同步构建快速建议:搜索话题 / 搜索用户 / 数字ID浏览主题或用户。
    /// </summary>
    private async Task BuildQuickSuggestions(string keyword)
    {
        Suggestions.Clear();
        var list = new List<SearchSuggestion>
        {
            new()
            {
                Type = SearchSuggestionType.Topic,
                Title = $"搜索话题:{keyword}"
            },
            new()
            {
                Type = SearchSuggestionType.User,
                Title = $"搜索用户:{keyword}",
                Parameter = keyword
            }
        };

        // 纯数字:直接浏览主题 / 查看用户
        if (int.TryParse(keyword, out var id))
        {
            list.Add(new SearchSuggestion
            {
                Type = SearchSuggestionType.TopicId,
                Title = $"浏览主题 #{id}",
                Parameter = id.ToString()
            });
            list.Add(new SearchSuggestion
            {
                Type = SearchSuggestionType.UserId,
                Title = $"查看用户 #{id}",
                Parameter = id.ToString()
            });
        }
        Suggestions.AddRange(list);
        await AppendBoardSuggestionsAsync(keyword);
    }

    private async Task AppendBoardSuggestionsAsync(string keyword)
    {
        var sections = await GetSectionsAsync();
        if (sections == null) return;

        foreach (var section in sections)
        {
            foreach (var board in section.Boards)
            {
                if (board.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    Suggestions.Add(new SearchSuggestion
                    {
                        Type = SearchSuggestionType.Board,
                        Title = $"版面:{board.Name}",
                        Subtitle = section.Name,
                        Parameter = board.Id.ToString()
                    });
                    if (Suggestions.Count >= 10) break;
                }
            }
            if (Suggestions.Count >= 10) break;
        }
    }


    /// <summary>
    /// 懒加载版面缓存(仅首次,失败返回 null)。
    /// </summary>
    private async Task<SectionInfo[]?> GetSectionsAsync()
    {
        if (_sections != null) return _sections;
        try
        {
            _sections = await BoardCacheManager.Instance.GetSectionDataAsync();
        }
        catch
        {
            _sections = null;
        }
        return _sections;
    }

    #endregion

    #region 翻页

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (Pager.CurrentPage <= 0) return;
        ShowLoadingState();
        await Pager.LoadLastPage(LoadSearchPageAsync);
        ShowResultState();
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        ShowLoadingState();
        await Pager.LoadNextPage(LoadSearchPageAsync);
        ShowResultState();
    }

    private void UpdatePagerButtons()
    {
        PrevPageButton.IsEnabled = Pager.CurrentPage > 0;
        NextPageButton.IsEnabled = Pager.HasMore;
        PageText.Text = $"第 {Pager.CurrentPage + 1} 页";
    }

    #endregion

    #region 状态切换

    private void ShowEmptyState()
    {
        EmptyState.Visibility = Visibility.Visible;
        LoadingState.Visibility = Visibility.Collapsed;
        NoResultState.Visibility = Visibility.Collapsed;
        ResultTabView.Visibility = Visibility.Collapsed;
    }

    private void ShowLoadingState()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Visible;
        NoResultState.Visibility = Visibility.Collapsed;
        ResultTabView.Visibility = Visibility.Collapsed;
    }

    private void ShowNoResultState()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Collapsed;
        NoResultState.Visibility = Visibility.Visible;
        ResultTabView.Visibility = Visibility.Collapsed;
    }

    private void ShowResultState()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Collapsed;
        NoResultState.Visibility = Visibility.Collapsed;
        ResultTabView.Visibility = Visibility.Visible;
    }

    /// <summary>结果出现时的淡入动画。</summary>
    private void FadeInContent()
    {
        if (Resources["ContentFadeIn"] is Storyboard storyboard)
            storyboard.Begin();
    }

    #endregion

    #region 交互

    /// <summary>点击历史 Token:填入关键词并立即搜索,随后取消选中以便重复点击。</summary>
    private void HistoryTokenView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryTokenView.SelectedItem is not string keyword || string.IsNullOrWhiteSpace(keyword)) return;
        SearchBox.Text = keyword;
        _ = PerformSearchAsync(keyword);
        // 取消选中,允许再次点击同一历史项
        HistoryTokenView.SelectedItem = null;
    }

    
    private void ResultCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        //超绝模式匹配语法
        if (sender is Grid { Tag: int topicId })
        {
            AddTopicTab(topicId);
        }
    }

    /// <summary>
    /// 在 TabView 中新增一个话题浏览 tab(Frame 承载 TopicPage),并切换到该 tab。
    /// </summary>
    private void AddTopicTab(int topicId)
    {
        var tab = new TabViewItem
        {
            Header = $"主题 #{topicId}",
            IsClosable = true
        };
        // 每个 tab 自带一个嵌套 Frame。注意 MainWindow 的后退栈修剪只作用于 ContentFrame,
        // 这里的内嵌后退栈不修剪 —— 不修剪是安全的:本页是 NavigationCacheMode=Disabled,
        // 离开搜索页时整棵树(含所有 tab 与它们的 Frame)一起丢弃;且首次导航不产生后退记录。
        var frame = new Frame();
        frame.Navigate(typeof(TopicPage), new TopicNavigationInfo { TopicId = topicId });
        tab.Content = frame;
        ResultTabView.TabItems.Add(tab);
        ResultTabView.SelectedItem = tab;
    }

    /// <summary>
    /// 清空所有话题浏览 tab(保留搜索 tab),用于新搜索时重置。
    /// </summary>
    private void ClearTopicTabs()
    {
        for (var i = ResultTabView.TabItems.Count - 1; i >= 0; i--)
        {
            if (ResultTabView.TabItems[i] as TabViewItem != SearchTab)
                ResultTabView.TabItems.RemoveAt(i);
        }
    }

    /// <summary>
    /// 关闭单个 tab:移除对应 TabViewItem;关闭搜索 tab 时清空结果回到空状态。
    /// </summary>
    private void ResultTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        var tabItem = args.Tab;
        ResultTabView.TabItems.Remove(tabItem);

        // 关闭搜索 tab:清空结果并回到空状态
        if (tabItem == SearchTab)
        {
            SearchResults.Clear();
            ShowEmptyState();
        }
    }


    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        // 无当前关键词时无需刷新
        if (string.IsNullOrEmpty(_currentKeyword)) return;
        // 重新加载第一页(PerformSearchAsync 内部会重置分页器并清空话题 tab)
        await PerformSearchAsync(_currentKeyword);
    }

    /// <summary>历史浮出层打开时绑定历史关键词集合。</summary>
    private void HistoryFlyout_Opening(object sender, object e)
    {
        HistoryList.ItemsSource = SearchHistory;
    }

    /// <summary>点击历史关键词:填入搜索框并立即搜索,关闭浮出层。</summary>
    private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not string keyword || string.IsNullOrWhiteSpace(keyword)) return;
        SearchBox.Text = keyword;
        _ = PerformSearchAsync(keyword);
        HistoryFlyout.Hide();
    }
}
    #endregion