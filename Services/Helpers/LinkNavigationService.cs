using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using CC98.Controls;
using CC98.Controls.Primitives;
using CC98.Kernel;
using CC98.Objects;
using CC98.Services.Extensions;
using CC98.Views;

using DevWinUI;

using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace CC98.Services.Helpers;

/// <summary>
/// 链接导航上下文：提供导航所需的页面信息。
/// </summary>
public record LinkContext
{
    /// <summary>当前页面的 Frame，用于导航跳转。</summary>
    public required Frame Frame { get; init; }

    /// <summary>当前正在浏览的主题 ID（用于锚点判断），无主题时为 null。</summary>
    public int? CurrentTopicId { get; init; }

    /// <summary>帖子中的所有图片 URL（用于画廊模式），无图片时为 null。</summary>
    public List<string>? ImageList { get; init; }

    /// <summary>用于显示通知的 Flower 控件。</summary>
    public required InfoFlower Flower { get; init; }


    /// <summary>跳转到指定楼层的方法（用于锚点跳转）。</summary>
    public Func<int, Task>? JumpToFloor { get; init; }
}

/// <summary>
/// 统一的链接导航服务：处理 UBB 和 Markdown 控件中的链接点击，实现应用内跳转。
/// </summary>
public static class LinkNavigationService
{
    /// <summary>
    /// 启动图片预览器。有画廊列表时按列表浏览,否则单图预览。
    /// </summary>
    /// <param name="url">被点击的图片 URL。</param>
    /// <param name="gallery">同帖图片列表(可选,用于画廊模式)。</param>
    public static void ShowImageViewer(string url, List<string>? gallery = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var urls = gallery ?? [url];
        var index = urls.IndexOf(url);
        if (index < 0) index = 0;
        var info = new ViewerNavigationInfo
        {
            Type = MediaType.Image,
            Urls = urls,
            CurrentIndex = index
        };
        var viewer = new MediaViewer(info);
        viewer.Activate();
    }

    /// <summary>
    /// 下载文件到用户下载目录。
    /// </summary>
    public static async Task DownloadFileAsync(string url, LinkContext context)
    {
        var fileRes = await Downloader.DownloadFileAsync(url);
        if (fileRes == null)
            context.Flower.Play(FlowStatus.Fail, "下载失败");
        else
            context.Flower.Play(FlowStatus.Success, $"已下载到{fileRes}");
    }

    /// <summary>
    /// 处理链接点击：根据 URL 类型执行应用内跳转、下载、或交给系统处理。
    /// </summary>
    public static async Task HandleLinkAsync(string url, LinkContext context)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        // 1. 主题链接（含锚点）
        var topicInfo = url.ExtractTopicInfo();
        if (topicInfo.HasValue)
        {
            await HandleTopicLinkAsync(topicInfo.Value, context);
            return;
        }

        // 2. 用户链接（按名字）
        var userNameMatch = UrlEx.CC98UserNameUrlRegex.Match(url);
        if (userNameMatch.Success)
        {
            var userName = Uri.UnescapeDataString(userNameMatch.Groups[2].Value);
            await HandleAtUserAsync(userName, context);
            return;
        }

        // 3. 用户链接（按 ID）
        var userIdMatch = UrlEx.CC98UserIdUrlRegex.Match(url);
        if (userIdMatch.Success)
        {
            var userId = int.Parse(userIdMatch.Groups[1].Value);
            await NavigateToUserByIdAsync(userId, context);
            return;
        }

        // 4. 版面链接
        var boardMatch = UrlEx.BoardRegex.Match(url);
        if (boardMatch.Success)
        {
            var boardId = int.Parse(boardMatch.Groups[1].ValueSpan);
            context.Frame.Navigate(typeof(BoardPage), boardId);
            return;
        }

        // 5. 文件链接
        if (url.IsCC98FileUrl)
        {
            if (url.IsCC98ImageUrl)
            {
                // 图片：优先使用帖子中的图片列表（画廊模式），否则单图
                ShowImageViewer(url, context.ImageList);
            }
            else
            {
                // 非图片文件：下载
                await DownloadFileAsync(url, context);
            }
            return;
        }

        // 6. 非 CC98 链接：交给系统处理（浏览器/邮件客户端等）
        try
        {
            await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"启动外部链接失败: {ex.Message}");
            // 回退：复制到剪贴板
            var package = new DataPackage();
            package.SetText(url);
            Clipboard.SetContent(package);
            context.Flower.Play(FlowStatus.Success, "已复制外部链接");
        }
    }

    

    /// <summary>
    /// 处理主题链接：同主题锚点跳转或跨主题导航。
    /// </summary>
    private static async Task HandleTopicLinkAsync(
        (int TopicId, int? Page, int? Anchor) topicInfo,
        LinkContext context)
    {
        // 是否有明确的页码/楼层目标(纯主题链接如 /topic/123 无目标,直接打开第一页)
        var hasTarget = topicInfo.Page.HasValue || topicInfo.Anchor.HasValue;

        // 有目标时才计算目标楼层
        int targetFloor = 0;
        if (topicInfo.Page.HasValue && topicInfo.Anchor.HasValue)
            targetFloor = (topicInfo.Page.Value - 1) * 10 + topicInfo.Anchor.Value;
        else if (topicInfo.Page.HasValue)
            targetFloor = (topicInfo.Page.Value - 1) * 10;

        // 同主题且已加载目标楼层：当前页面内跳转
        if (hasTarget && topicInfo.TopicId == context.CurrentTopicId  && context.JumpToFloor != null)
        {
            await context.JumpToFloor(targetFloor);
            return;
        }
        // 有楼层目标：以跳转模式导航,加载后定位到目标楼层
        if (hasTarget)
        {
            var param = new TopicNavigationInfo
            {
                IsJumpingMode = true,
                TopicId = topicInfo.TopicId,
                TargetFloor = targetFloor
            };
            context.Frame.Navigate(typeof(TopicPage), param);
            return;
        }

        // 纯主题链接:直接打开主题(普通路径,显示第一页,不触发楼层跳转)
        context.Frame.Navigate(typeof(TopicPage), new TopicNavigationInfo { TopicId = topicInfo.TopicId });
    }

    /// <summary>
    /// 处理 @用户名 点击：搜索用户并导航到个人资料页。
    /// </summary>
    public static async Task HandleAtUserAsync(string userName, LinkContext context)
    {
        var apiService = App.Current.GetService<ApiService>();
        var url = ApiEndpoints.User.SearchUserByName(userName);
        var result = await apiService.Fetch<UserInfo>(url);
        if (!result.IsSuccess || result.Data == null)
        {
            context.Flower.Play(FlowStatus.Fail, "未找到用户");
            return;
        }

        var user = result.Data;
        await NavigateToUserByIdAsync(user.Id, context);
    }

    /// <summary>
    /// 按用户 ID 直接导航到个人资料页。
    /// </summary>
    private static async Task NavigateToUserByIdAsync(int userId, LinkContext context)
    {
        var info = new ProfileNavigationInfo
        {
            IsMe = userId == AppSettings.Current.UserId,
            UserId = userId
        };
        context.Frame.Navigate(typeof(ProfilePage), info);
    }
}
