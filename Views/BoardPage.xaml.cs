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
    
    public ApiService ApiService = App.Current.GetService<ApiService>();

    //是否精华帖
    public bool IsBest { get; } = false;

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
        // 返回同一版面(如从主题页返回)时,筛选标签、已加载列表、滚动位置与搜索面板都还在,
        // 直接沿用即可,不要重新加载。
        // 注意:这里比较的是"本次传入的版面"与"当前实例正在展示的版面",
        // 因此返回到了另一个版面时会正确地走下面的重置逻辑。
        if (e.NavigationMode == NavigationMode.Back && args == BoardId) return;

        // 前进进入,或返回到了另一个版面:完全重置为默认的"最新"标签。
        BoardId = args;
        BoardSymbol.Symbol = BoardIconHelper.GetSymbol(BoardId, "");

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



    private async Task GetData()
    {
        var boardDataUrl = ApiEndpoints.Board.BoardInfo(BoardId);
        var boardDataResult = await ApiService.Fetch<BoardData>(boardDataUrl);
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

    private async Task<bool> LoadTopics()
    {
        var topicUrl = ApiEndpoints.Board.TopicList((int)FilterType, BoardId, Increment.StartIndex);

        if (FilterType == BoardTopicFilterType.Best)
        {
            var result = await ApiService.Fetch<BoardBest>(topicUrl);
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
        await Increment.LoadMore(args.Index, LoadTopics);
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

    // 程序化复位筛选标签时置位,用于抑制 TypeSelectorBar_SelectionChanged 触发的加载
    private bool _suppressTypeChange;

    /// <summary>
    /// 版面信息与主题列表并行加载,并防止并发触发。
    /// </summary>
    private async Task RefreshAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            await Task.WhenAll(GetData(), LoadTopics());
        }
        finally
        {
            _isLoading = false;
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
        SearchProgressRing.IsActive = true;
        searchResultTopics.Clear();
        var searchUrl = ApiEndpoints.Topic.SearchTopicInBoard(BoardId, HttpUtility.UrlEncode(searchKeyword), searchIncrement.StartIndex);
        var result = await ApiService.Fetch<List<SearchTopicInfo>>(searchUrl);
        if(!result.IsSuccess||result.Data==null)
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
        SearchProgressRing.IsActive = false;
        return true;
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