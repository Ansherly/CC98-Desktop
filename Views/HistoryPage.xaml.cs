using CC98.Kernel;
using CC98.Objects;
using DevWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using static CC98.Kernel.ApiEndpoints;
using CC98.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class HistoryPage : Page
{
    public Increment Increment = new(10);
    public ObservableCollection<SimpleTopicInfo> HistoryTopics = [];
    public ApiService ApiService = App.Current.GetService<ApiService>();
    public bool IsRecordEnabled = true;
    public HistoryPage()
    {
        InitializeComponent();
    }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await Task.WhenAll(GetRecordStatus(), GetHistoryTopic());
    }

    private async Task GetRecordStatus()
    {
        var endpoint = ApiEndpoints.User.UserProfile(true);
        var userProfileResult = await ApiService.Fetch<UserInfo>(endpoint);
        if (userProfileResult?.Data is UserInfo info)
        {
            EnableRecordButton.Visibility=Visibility.Visible;
            IsRecordEnabled = info.BrowsingHistoryEnabled;
            EnableRecordButton.Content = info.BrowsingHistoryEnabled ? "关闭浏览记录" : "开启浏览记录";
            return;
        }
        EnableRecordButton.Visibility = Visibility.Collapsed;
    }
    private async Task<bool> GetHistoryTopic()
    {
        var endpoint = ApiEndpoints.User.BrowseHistory(Increment.StartIndex);
        var historyTopicResult = await ApiService.Fetch<BrowsingRecord>(endpoint);
        if (!historyTopicResult.IsSuccess || historyTopicResult.Data?.Data is not List<SimpleTopicInfo> data)
        {
            //
            if (historyTopicResult.StatusCode == (int)HttpStatusCode.Forbidden)
            {
                Tip.Show(InfoType.Error, "请求频率过快");
                return false;
            }
            Tip.Show(InfoType.Error, "获取历史记录失败");
            return false;
        }
        // 接口约定:返回 PageSize+1 条表示还有更多。先按原始数量判定,再截断多余的第 PageSize+1 条
        var hasMore = data.Count > Increment.PageSize;
        if (hasMore) data.RemoveAt(Increment.PageSize);
        Increment.HasMore = hasMore;
        HistoryTopics.AddRange(data);
        return true;
    }
    private async void HistoryTopicRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        //接口速率限制。由于只有10条，很容易触发403，必须减速。
        await Task.Delay(1500);
        await Increment.LoadMore(args.Index, GetHistoryTopic);
    }

    private void Content_Click(object sender, RoutedEventArgs e)
    {
        if(sender is HyperlinkButton hyperlinkButton && hyperlinkButton.Tag is int topicId)
        {
            var param = new TopicNavigationInfo { TopicId = topicId };
            Frame.Navigate(typeof(TopicPage), param);
        }
    }

 
    private async void EnableRecordButton_Click(object sender, RoutedEventArgs e)
    {
        EnableRecordButton.IsEnabled = false;
        var endpoint = ApiEndpoints.User.EnableBrowseHistory(!IsRecordEnabled);
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var result = await ApiService.Put(endpoint, content);
        if (result.IsSuccess)
        {
            IsRecordEnabled = !IsRecordEnabled;
            EnableRecordButton.Content = IsRecordEnabled ? "关闭浏览记录" : "开启浏览记录";
            Tip.Show(InfoType.Success, IsRecordEnabled ? "已开启浏览记录" : "已关闭浏览记录");
        }
        else
        {
            Tip.Show(InfoType.Error, IsRecordEnabled ? "关闭浏览记录失败" : "开启浏览记录失败");
        }
        EnableRecordButton.IsEnabled = true;
    }
}
