using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Storage;
using CC98.Kernel;
using CC98.Objects;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Card = CC98.Objects.Card;
using CC98.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     游戏页面。
/// </summary>
public sealed partial class GamePage
{
    public ObservableCollection<Card> Cards = [];
    public ObservableCollection<GachaInfo> GachaInfo1 = [];
    public ObservableCollection<GachaInfo> GachaInfo2 = [];
    public ApplicationDataContainer Set = ApplicationData.Current.LocalSettings;
    public ApiService ApiService = App.Current.GetService<ApiService>();
    public GamePage()
    {
        InitializeComponent();
        DisplayOdd();
        RefreshStat();
        CardList.ItemsSource = Cards;
    }

    public CardStat CardDrawStat { get; set; } = new()
        { Wealth = 0, CardCount = 0, DrawCount = 0, TotalBonus = 0, TotalCost = 0 };

    private async void RefreshStat()
    {
        var profileUrl = ApiEndpoints.User.UserProfile(true, 0);
        var statUrl = ApiEndpoints.Forum.CardStat;
        // 两个请求互不依赖,并行发起
        var profileTask = ApiService.Fetch<UserInfo>(profileUrl);
        var statTask = ApiService.Fetch<CardStat>(statUrl);

        var profileResult = await profileTask;
        if (!profileResult.IsSuccess || profileResult.Data == null) return;
        var data = profileResult.Data;
        CardDrawStat.Wealth = data.Wealth;
        var statResult = await statTask;
        if (!statResult.IsSuccess || statResult.Data == null)
            //
            return;
        var stat = statResult.Data;
        CardDrawStat.DrawCount = stat.DrawCount;
        CardDrawStat.TotalCost = stat.TotalCost;
        CardDrawStat.TotalBonus = stat.TotalBonus;
        CardDrawStat.CardCount = stat.CardCount;
    }

    private void DisplayOdd()
    {
        GachaInfo1.Add(new() { Rank = "Mystery", Probability = "0.03%" });
        GachaInfo1.Add(new() { Rank = "SSR", Probability = "1.50%" });
        GachaInfo1.Add(new() { Rank = "SR", Probability = "15.00%" });
        GachaInfo1.Add(new() { Rank = "R", Probability = "29.99%" });
        GachaInfo1.Add(new() { Rank = "N", Probability = "53.48%" });


        GachaInfo2.Add(new() { Rank = "Mystery", Probability = "0.01%" });
        GachaInfo2.Add(new() { Rank = "SSR", Probability = "1.50%" });
        GachaInfo2.Add(new() { Rank = "SR", Probability = "15.00%" });
        GachaInfo2.Add(new() { Rank = "R", Probability = "30.00%" });
        GachaInfo2.Add(new() { Rank = "N", Probability = "53.49%" });
    }

    // 抽卡防重入:每次抽卡都是真实扣费的 API 请求,禁止连点并发
    private bool _isDrawing;

    private async Task StartDraw(int rule)
    {
        Cards.Clear();
        var drawUrl = ApiEndpoints.Forum.DrawCard(rule);
        var drawResult = await ApiService.Submit<List<Card>>(drawUrl, null);
        if (!drawResult.IsSuccess || drawResult.Data == null)
        {
            //
            Tip.Show(InfoType.Error, drawResult.Message);
            return;
        }

        var data = drawResult.Data;
        foreach (var card in data)
        {
            // 防御空串,并正确补全图片地址。
            // API 返回的 imageUri 形如 "~/Image/xxx.webp"(以 ~ 开头),去掉 ~ 后拼资源域名(与原逻辑 Substring(1) 一致):
            //   "~/Image/xxx.webp" -> "https://card.cc98.org/Image/xxx.webp"
            var uri = card.ImageUri;
            if (string.IsNullOrEmpty(uri)) continue;
            if (uri.StartsWith("http://") || uri.StartsWith("https://"))
                card.ImageUri = uri; // 完整URL原样使用
            else if (uri.StartsWith('~'))
                card.ImageUri = "https://card.cc98.org" + uri[1..];
            else if (uri.StartsWith('/'))
                card.ImageUri = "https://card.cc98.org" + uri;
            else
                card.ImageUri = "https://card.cc98.org/" + uri;
        }
        Cards.AddRange(data);
    }


    private void FlipSide_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var flipper = sender as FlipSide;
        if (flipper != null) flipper.IsFlipped = !flipper.IsFlipped;
    }

    private async void Draw_Click(object sender, RoutedEventArgs e)
    {
        if (_isDrawing) return;
        _isDrawing = true;
        BusyIndicator.Visibility = Visibility.Visible;
        ResultViewer.Visibility = Visibility.Collapsed;
        try
        {
            await StartDraw(1);
            await Task.Delay(1000);
        }
        finally
        {
            _isDrawing = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
            ResultViewer.Visibility = Visibility.Visible;
        }
    }

    private async void Drawn_Click(object sender, RoutedEventArgs e)
    {
        if (_isDrawing) return;
        _isDrawing = true;
        BusyIndicator.Visibility = Visibility.Visible;
        ResultViewer.Visibility = Visibility.Collapsed;
        try
        {
            await StartDraw(2);
            await Task.Delay(1500);
        }
        finally
        {
            _isDrawing = false;
            BusyIndicator.Visibility = Visibility.Collapsed;
            ResultViewer.Visibility = Visibility.Visible;
        }
    }

    private void Unfold_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in Cards) c.IsFlipped = true;
    }


    private async void Operate_Click(object sender, RoutedEventArgs e)
    {
        var b = sender as Button;
        if (b == null || b.Tag is not string tag) return;

        switch (tag)
        {
            case "single-more":
                if (SingleProperList.ItemsSource == null)
                    SingleProperList.ItemsSource = GachaInfo1;
                else
                    SingleProperList.ItemsSource = null;
                break;
            case "multi-more":
                // 连抽(11张)使用连抽概率集合,而非单抽概率
                if (MultiProperList.ItemsSource == null)
                    MultiProperList.ItemsSource = GachaInfo2;
                else
                    MultiProperList.ItemsSource = null;
                break;
            case "destroy-all":
                DestroyCardDialog.XamlRoot = XamlRoot;
                var r = await DestroyCardDialog.ShowAsync();
                if (r == ContentDialogResult.Primary)
                {
                    var url = "https://card.cc98.org/api/collection/all-rest";
                    var result = await ApiService.Delete(url);
                    if (!result.IsSuccess)
                    {
                        //
                        Tip.Show(InfoType.Error, "分解失败");
                        return;
                    }

                    RefreshStat();
                    Tip.Show(InfoType.Success, "分解成功");
                }

                break;
            case "ref-stat":
                RefreshStat();
                Tip.Show(InfoType.Information, "正在刷新数据");
                break;
        }
    }
}