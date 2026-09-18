using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using CC98.Kernel;
using CC98.Objects;
using CC98.Services;
using CC98.Services.Extensions;
using CC98.Services.Helpers;
using CC98.Controls.Primitives;
using CC98.Controls.UbbTextBlock.Common.Events;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Web;
using System.Linq;
using System.Net.WebSockets;

namespace CC98.Views;

/// <summary>
/// 显示版面信息。
/// </summary>
public sealed partial class BoardPage
{
    public ObservableCollection<SimpleTopicInfo> Topics = [];
    public ObservableCollection<SearchTopicInfo> searchResultTopics= [];
    
    private ApiService ApiService = App.Current.GetService<ApiService>();

    public int BoardId { get; set; } = 0;
    private string searchKeyword = string.Empty;

    public BoardData BoardData { get; } = new();

    public BoardTopicFilterType FilterType { get; set; } = BoardTopicFilterType.Latest;
    public Increment Increment { get; } = new(20);
    public Increment searchIncrement=new(20);


    public BoardPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var args = e.TryGetParameter<int>();

        // 本页设置了 NavigationCacheMode="Required",页面实例会被缓存:
        // 返回同一版面(如从主题页返回)、或再次进入当前版面时,
        // 筛选标签、已加载列表、滚动位置与搜索面板都还在,直接沿用即可,不要重新加载。
        // 「再次进入当前版面」按产品决策等价于无操作;只有 NavigationMode.Refresh 才重新加载。
        // 注意:这里比较的是"本次传入的版面"与"当前实例正在展示的版面",
        // 因此返回到了另一个版面时会正确地走下面的重置逻辑。
        if (args == BoardId && e.NavigationMode != NavigationMode.Refresh)
        {
            // 缓存实例重新挂载时滚动位置可能被重置,这里显式放回离开时的位置
            RestoreScrollOffset();
            return;
        }

        // 前进进入,或返回到了另一个版面:完全重置为默认的"最新"标签。
        BoardId = args;
        BoardSymbol.Symbol = BoardIconHelper.GetSymbol(BoardId, "");
        _savedScrollOffset = 0; // 换版面后旧偏移无意义

        // 换版面:代次自增以作废在途请求的结果,并允许新的加载会话立即开始
        // (否则旧版面在途的响应会把数据追加到新版面的列表里)。
        _boardGeneration++;
        _isLoading = false;

        // 搜索面板是"版面级"状态,必须随版面一起复位,
        // 否则从新版面打开搜索面板会看到上一个版面的搜索结果。
        searchKeyword = string.Empty;
        BoardSearchBox.Text = string.Empty;
        searchIncrement.Clear();
        searchResultTopics.Clear();
        SearchProgressRing.IsActive = false;
        SearchView.IsPaneOpen = false;

        // 程序化复位选中项同样会触发 TypeSelectorBar_SelectionChanged,这里抑制它,
        // 统一由下面的 RefreshAsync 加载一次,避免重复加载。
        _suppressTypeChange = true;
        TypeSelectorBar.SelectedItem = TypeSelectorBar.Items[0];
        _suppressTypeChange = false;

        FilterType = BoardTopicFilterType.Latest;
        Increment.Clear();
        Topics.Clear();

        // 版面信息与主题列表互不依赖,并行加载
        await RefreshAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // 记录离开时的滚动位置,供返回本页时恢复(与 DiscoverPage 的处理一致)
        _savedScrollOffset = TopicScrollViewer.VerticalOffset;
    }

    /// <summary>
    ///     恢复离开本页前的滚动位置。
    ///     需等重新挂载后的布局完成,否则 ExtentHeight 仍为 0、设置的偏移会被钳制为 0;
    ///     若框架本身就保留了偏移,这里等价于原地不动。
    /// </summary>
    private void RestoreScrollOffset()
    {
        if (_savedScrollOffset <= 0) return;

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            TopicScrollViewer.ChangeView(null, _savedScrollOffset, null, true);
        });
    }



    /// <summary>
    ///     版面数据加载。
    /// </summary>
    /// <param name="generation">发起时的版面代次;响应回来时若版面已切换则丢弃结果。</param>
    private async Task GetData(int generation)
    {
        var boardDataUrl = ApiEndpoints.Board.BoardInfo(BoardId);
        var boardDataResult = await ApiService.Fetch<BoardData>(boardDataUrl);
        if (generation != _boardGeneration) return; // 版面已切换,丢弃过期响应

        if (!boardDataResult.IsSuccess || boardDataResult.Data == null)
        {
            Flower.Play(FlowStatus.Fail, boardDataResult.Message);
            return;
        }

        var data = boardDataResult.Data;
        BoardData.Id = data.Id;
        BoardData.Name = data.Name;
        BoardData.Description = data.Description;
        BoardData.BigPaper = data.BigPaper;
        BoardData.BoardMasters = data.BoardMasters;
        BoardData.TopicCount = data.TopicCount;
        BoardData.TodayCount = data.TodayCount;
    }

    /// <summary>
    ///     主题列表加载(每页一组)。
    /// </summary>
    /// <param name="generation">发起时的版面代次;响应回来时若版面已切换则丢弃结果。</param>
    private async Task<bool> LoadTopics(int generation)
    {
        var topicUrl = ApiEndpoints.Board.TopicList((int)FilterType, BoardId, Increment.StartIndex);

        if (FilterType == BoardTopicFilterType.Best)
        {
            var result = await ApiService.Fetch<BoardBest>(topicUrl);
            if (generation != _boardGeneration) return false; // 版面已切换,丢弃过期响应
            if (result.IsNotValid)
            {
                Flower.Play(FlowStatus.Fail, result.Message);
                return false;
            }
            var bests = result.Data?.Topics;
            Increment.HasMore = bests.Count == Increment.PageSize;
            Topics.AddRange(bests);
            BoardEmptyState.Visibility = Topics.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            return true;
        }

        var topicResult = await ApiService.Fetch<List<SimpleTopicInfo>>(topicUrl);
        if (generation != _boardGeneration) return false; // 版面已切换,丢弃过期响应
        if (topicResult.IsNotValid)
        {
            Flower.Play(FlowStatus.Fail, topicResult.Message);
            return false;
        }

        var data = topicResult.Data;
        Increment.HasMore = data.Count == Increment.PageSize;
        Topics.AddRange(data);
        BoardEmptyState.Visibility = Topics.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        return true;
    }


    private void TileContent_Click(object sender, RoutedEventArgs e)
    {
        var h = sender as HyperlinkButton;
        if (h?.DataContext is not SimpleTopicInfo t) return;
        var param = new TopicNavigationInfo { TopicId = t.Id };
        Frame.Navigate(typeof(TopicPage), param);
    }




    private async void TopicRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        // 取"当前代次"再加载:若期间换了版面,LoadTopics 内部会丢弃这次响应
        await Increment.LoadMore(args.Index, () => LoadTopics(_boardGeneration));
    }

    private async void BoardAction_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as AppBarButton;
        if (button == null) return;
        if (button.Tag is not string tag) return;
        switch (tag)
        {
            case "browse":
                await Launcher.LaunchUriAsync(new(ApiEndpoints.Board.WebUrl(BoardId)));
                break;
            case "refresh":
                Increment.Clear();
                Topics.Clear();
                await RefreshAsync();
                Flower.Play(FlowStatus.Success, "刷新成功");
                break;
            case "pin":
                await Pin();
                break;
            case "draft":
                Flower.Play(FlowStatus.Info, "暂不支持发主题");
                break;
            case "vote":
                var param2 = new SketchNavigationInfo
                {
                    EditorMode = EditorMode.Vote,
                    BoardId = BoardId
                };
                Frame.Navigate(typeof(SketchPage), param2);
                break;

            case "search":
                SearchView.IsPaneOpen = true;
                BoardSearchBox.Focus(FocusState.Programmatic);
                break;
        }
    }

    private async Task Pin()
    {
        var url = ApiEndpoints.Board.EditFocusBoards(BoardId);
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var result = await ApiService.Put(url, content);
        if (!result.IsSuccess)
        {
            //
            Flower.Play(FlowStatus.Fail, result.Message);
            //await App.Logger.WriteAsync("Board", "关注版面失败", result.Message);
            return;
        }

        var i = new NavigationItem
        {
            IconSymbol = BoardIconHelper.GetSymbol(BoardId, BoardData.Name),
            Name = BoardData.Name,
            IsEditable = true,
            Tag = BoardId.ToString()
        };
        Messenger.Instance.AddNavigationItem(i);
        Flower.Play(FlowStatus.Success, "已关注");
    }

    private async void TypeSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // 程序化复位(OnNavigatedTo 中换版面/重新进入)不触发加载
        if (_suppressTypeChange) return;
        // 防重入:加载期间忽略重复触发,避免"重复加载两次帖子"的问题
        if (_isLoading) return;
        var item = sender as SelectorBar;
        if (item?.SelectedItem?.Tag is not string tag) return;
        Increment.Clear();
        Topics.Clear();
        //切换时，清除已有列表，重置增量更新，修改当前筛选类型
        FilterType = tag switch
        {
            "latest" => BoardTopicFilterType.Latest,
            "top" => BoardTopicFilterType.Top,
            _ => BoardTopicFilterType.Best,
        };
        await RefreshAsync();
    }

    // 版面数据加载防重入标志
    private bool _isLoading;

    // 版面代次:每次"换版面"自增,用于判断在途请求的响应是否已经过期
    private int _boardGeneration;

    // 离开本页时的滚动位置(本页是 NavigationCacheMode=Required,返回时要放回原位)
    private double _savedScrollOffset;

    // 程序化复位筛选标签时置位,用于抑制 TypeSelectorBar_SelectionChanged 触发的加载
    private bool _suppressTypeChange;

    /// <summary>
    /// 版面信息与主题列表并行加载,并防止同一版面内的并发触发。
    /// </summary>
    private async Task RefreshAsync()
    {
        if (_isLoading) return;

        var generation = _boardGeneration;
        _isLoading = true;
        try
        {
            await Task.WhenAll(GetData(generation), LoadTopics(generation));
        }
        finally
        {
            // 只有"当前代次"的会话才有资格复位标志,避免旧会话提前清掉新会话的加载状态
            if (generation == _boardGeneration) _isLoading = false;
        }
    }

    /// <summary>
    /// 版面大字报(BigPaper)中的链接点击:按媒体类型显式处理。
    /// </summary>
    private async void Banner_MediaClicked(object sender, MediaClickEventArgs e)
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

 
    private async void BoardSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(searchKeyword))
        {
            searchIncrement.Clear();
            await Search();
        }
    }
    private async Task<bool> Search()
    {
        var generation = _boardGeneration;
        SearchProgressRing.IsActive = true;
        searchResultTopics.Clear();
        try
        {
            var searchUrl = ApiEndpoints.Topic.SearchTopicInBoard(BoardId, HttpUtility.UrlEncode(searchKeyword), searchIncrement.StartIndex);
            var result = await ApiService.Fetch<List<SearchTopicInfo>>(searchUrl);
            if (generation != _boardGeneration) return false; // 版面已切换,丢弃过期结果
            if (!result.IsSuccess || result.Data == null)
            {
                Flower.Play(FlowStatus.Fail, result?.Message ?? "搜索失败");
                return false;
            }
            var data = result.Data;
            searchIncrement.HasMore = data.Count == searchIncrement.PageSize;
            foreach (var topic in data)
            {
                if (topic.IsAnonymous)
                {
                    topic.UserName = $"匿名{topic.UserName.ToUpper()}";
                }
                topic.Keyword = searchKeyword;
            }
            searchResultTopics.AddRange(data);
            return true;
        }
        finally
        {
            // 失败路径也要收掉进度环;换过版面则交给新会话管理
            if (generation == _boardGeneration) SearchProgressRing.IsActive = false;
        }
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        await searchIncrement.LoadNextPage(Search);
    }

    private async void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        await searchIncrement.LoadLastPage(Search);
    }

    private void BoardSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        searchKeyword=BoardSearchBox.Text;
    }

    private void SearchResultCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int topicId)
        {
            Frame.Navigate(typeof(TopicPage), new TopicNavigationInfo { TopicId = topicId });
        }
    }

    
}