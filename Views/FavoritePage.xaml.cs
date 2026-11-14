using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Storage;
using CC98.Kernel;
using CC98.Objects;
using DevWinUI;
using Microsoft.UI.Xaml;
using CC98.Services.Extensions;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using FluentIcons.Common;
using Symbol = FluentIcons.Common.Symbol;
using CC98.Services;
using CC98.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     收藏页面。
/// </summary>
public sealed partial class FavoritePage : Page
{
    public PostOrder CurrenOrder = PostOrder.Mark;
    public ObservableCollection<Favorites> FavoritesList = [];
    public int GroupId;
    public Increment Increment = new();

    public int SortId;
    public ObservableCollection<SimpleTopicInfo> Topics = [];
    public ApiService ApiService = App.Current.GetService<ApiService>();
    public FavoritePage()
    {
        InitializeComponent();
    }

    public Favorites? SelectedFavorites { get; set; }


    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        LoadFavorites();
        await GetFavoriteTopic();
    }

    private void LoadFavorites()
    {
        var favoriteJson = AppSettings.Current.FavoriteGroups;
        try
        {
            var data = SerializationHelper.TryDeserialize<List<Favorites>>(favoriteJson);
            if (data == null) return;
            FavoritesList.Clear();
            FavoritesList.AddRange(data);
        }
        catch
        {
            Tip.Show(InfoType.Error, "加载收藏分组失败");
        }
    }

    private async Task<bool> GetFavoriteTopic()
    {
        var favoriteTopicUrl = ApiEndpoints.Topic.FavoriteTopicList(Increment.StartIndex, (int)CurrenOrder, GroupId);
        var favoriteTopicResult = await ApiService.Fetch<List<SimpleTopicInfo>>(favoriteTopicUrl);
        if (!favoriteTopicResult.IsSuccess || favoriteTopicResult.Data == null)
            //
            return false;
        var data = favoriteTopicResult.Data;
        // 接口约定:返回 PageSize+1 条表示还有更多。先按原始数量判定,再截断多余的第 PageSize+1 条
        var hasMore = data.Count > Increment.PageSize;
        if (hasMore) data.RemoveAt(Increment.PageSize);
        Increment.HasMore = hasMore;
        Topics.AddRange(data);
        return true;
    }


    private void Content_Click(object sender, RoutedEventArgs e)
    {
        var h = sender as HyperlinkButton;
        if (h?.DataContext is not SimpleTopicInfo t) return;
        var param = new TopicNavigationInfo { TopicId = t.Id };
        Frame.Navigate(typeof(TopicPage), param);
    }


    private async void ChangeSort_Click(object sender, RoutedEventArgs e)
    {
        CurrenOrder = (PostOrder)(((int)CurrenOrder + 1) % 3);
        Topics.Clear();
        Increment.Clear();

        await GetFavoriteTopic();
        string sortMethod;
        if (CurrenOrder == PostOrder.Time)
        {
            sortMethod = "发帖时间";
            SortIcon.Symbol = Symbol.History;
        }
        else if (CurrenOrder == PostOrder.LastReply)
        {
            sortMethod = "最后回复";
            SortIcon.Symbol = Symbol.ArrowReply;
        }
        else
        {
            sortMethod = "收藏顺序";
            SortIcon.Symbol = Symbol.StarAdd;
        }

        Tip.Show(InfoType.Information, "切换为" + sortMethod + "排序");
    }

    private async void FavoriteBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedFavorites != null)
        {
            Topics.Clear();
            Increment.Clear();
            SortId = 0;
            GroupId = SelectedFavorites.Id;
            de.Text = SelectedFavorites.Name;
            await GetFavoriteTopic();
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var m = sender as MenuFlyoutItem;
        if (m?.DataContext is not SimpleTopicInfo info) return;
        var endpoint = ApiEndpoints.Topic.DeleteFavoriteTopic(info.Id);
        var res = await ApiService.Delete(endpoint);
        if (res == null || !res.IsSuccess)
        {
            Tip.Show(InfoType.Error, $"取消收藏失败:{res?.Message}");
        }
        else
        {
            Topics.Clear();
            Increment.Clear();
            SortId = 0;
            await GetFavoriteTopic();
            Tip.Show(InfoType.Success, "已取消收藏");
        }
       

    }

    private async void FavoriteTopicRepeater_ElementPrepared(ItemsRepeater sender,
        ItemsRepeaterElementPreparedEventArgs args)
    {
        await Increment.LoadMore(args.Index, GetFavoriteTopic);
    }
}