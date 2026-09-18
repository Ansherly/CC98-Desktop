using CC98.Kernel;
using CC98.Kernel.Authorize;
using CC98.Kernel.Network;
using CC98.Objects;
using CC98.Services;
using CC98.Services.Extensions;
using CC98.Services.Helpers;
using ColorCode.Compilation.Languages;
using CommunityToolkit.WinUI.Converters;
using CSharpMath;
using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.BadgeNotifications;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage;
using static CC98.Kernel.ApiEndpoints;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Symbol = FluentIcons.Common.Symbol;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public ObservableCollection<string> Collections = [];
    public GlobalService GlobalService = GlobalService.Instance;
    //需要迁移到新的存取关注版面的方式
    public Dictionary<int, string> Memory = [];

    public ObservableCollection<CategoryBase> MenuItems { get; } = [];
    public ObservableCollection<CategoryBase> FooterMenuItems { get; } = [];
    public Frame RootFrame => ContentFrame; //用于在嵌套的Frame中导航
    public NavigationView NavigationView => Navi;
    public int UnreadCount { get; set; }
    public ApiService ApiService = App.Current.GetService<ApiService>();
    private DispatcherTimer? SyncTimer { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        // 初始化复用的头像缩放动画(XAML 解析完成后才能绑定 Target)
        Storyboard.SetTarget(_portScaleX, PortScaleTransform);
        Storyboard.SetTargetProperty(_portScaleX, "ScaleX");
        Storyboard.SetTarget(_portScaleY, PortScaleTransform);
        Storyboard.SetTargetProperty(_portScaleY, "ScaleY");
        _portScaleStoryboard.Children.Add(_portScaleX);
        _portScaleStoryboard.Children.Add(_portScaleY);
        App.ThemeChanged += OnAppThemeChanged;
        //设置窗口状态
        SetWindowState();
        //加载自定义设置
        LoadSettings();
        //同步部分:填充导航菜单并导航到初始页面。均无网络请求,不阻塞窗口显示。
        LoadMenuItem();
        LoadIndex();
        //网络相关的初始化延迟到窗口显示(首帧渲染)之后再执行,避免阻塞主窗口显示。
        //RootGrid_Loaded 已在 XAML 中挂接。
    }
    

    private void SetWindowState()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(UserArea);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "cc98.ico");
        AppWindow.SetIcon(iconPath);
        AppWindow.SetTaskbarIcon(iconPath);
        Messenger.Instance.NavigationItemAdded += OnNavigationItemAdded;
    }
    

    private void LoadSettings()
    {
        int effect = AppSettings.Current.Effect;
        SystemBackdrop = effect switch
        {
            0 => new MicaSystemBackdrop(),
            1 => new MicaSystemBackdrop(MicaKind.BaseAlt),
            2 => new AcrylicSystemBackdrop(),
            3 => new AcrylicSystemBackdrop(DesktopAcrylicKind.Thin),
            4 => null,
            _ => new MicaSystemBackdrop()
        };

        int theme = AppSettings.Current.Theme;
        RootGrid.RequestedTheme = theme switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    /// <summary>
    /// 窗口首帧渲染完成后触发,在此启动网络相关的初始化。
    /// </summary>
    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        //低优先级排队,确保首帧渲染完成后再发起网络请求
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () => await LoadStartupDataAsync());
    }

    /// <summary>
    /// 启动阶段需要联网的任务。互不依赖,并行执行以缩短整体耗时。
    /// VPN 检查独立于核心数据:其网络慢或需用户交互时,不阻塞头像等资源加载。
    /// </summary>
    private async Task LoadStartupDataAsync()
    {
        // 定时器与网络加载无依赖,立即启动
        InitializeTimer();
        // VPN 检查 fire-and-forget:异常在内部捕获,失败/缓慢不影响其他任务
        _ = CheckVpnStatusSafelyAsync();
        await Task.WhenAll(
            RefreshMessage(),
            LoadUserProfile(),
            GetFavorites());
    }
    /// <summary>
    /// VPN 启动检查的安全包装:任何异常都只记录,不传播到调用链。
    /// </summary>
    private async Task CheckVpnStatusSafelyAsync()
    {
        try
        {
            await CheckVpnStatus(isStartup: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VPN 启动检查异常: {ex.Message}");
        }
    }

    private async Task LoadUserProfile()
    {
        try
        {
            var profileUrl = ApiEndpoints.User.UserProfile(true);
            var profileResult = await ApiService.Fetch<UserInfo>(profileUrl);
            var customBoards = SerializationHelper.TryDeserialize<List<int>>(AppSettings.Current.CustomBoards);
            if (!profileResult.IsSuccess || profileResult.Data == null)
            {
                Flower.Play(FlowStatus.Fail, $"更新用户信息失败: {profileResult.Message}");
                if(customBoards!=null) await LoadFocusBoards(customBoards);
                return;
            }
            else
            {
                var data = profileResult.Data;
                AppSettings.Current.UserId = data.Id;
                var refreshTask = RefreshPortraitIfNeeded(data.PortraitUrl);
                var loadBoardsTask = LoadFocusBoardsIfNeeded(data, customBoards);
                await Task.WhenAll(refreshTask, loadBoardsTask);
               
            }
            
        }
        catch(Exception ex)
        {
            Debug.WriteLine($"加载头像失败: {ex.Message}");
            Flower.Play(FlowStatus.Fail, $"更新头像出错: {ex.Message}");
        }  
    }
    private async Task RefreshPortraitIfNeeded(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // 命中缓存的充要条件:远端地址没变【且本地缓存文件确实还在】。
        // 只比较地址是不够的:清除缓存 / 切换用户会删掉 LocalCache 里的头像文件,却不会动这两项设置,
        // 于是 LocalPortraitUrl 变成指向已删除文件的悬空路径;而只看地址的旧逻辑会直接返回,
        // 头像便再也下载不回来 —— 表现为设置里有路径、缓存目录里没有文件、头像空白。
        if (AppSettings.Current.PortraitUrl == url && File.Exists(AppSettings.Current.LocalPortraitUrl)) return;

        // 下载期间先不写 PortraitUrl:若失败时已经写入,下次启动会因地址相同而跳过重试
        var portraitPath = await Downloader.DownloadCacheFileAsync(url, ApplicationData.Current.LocalCacheFolder.Path);
        if (portraitPath == null)
        {
            Debug.WriteLine("下载头像失败");
            return;
        }

        AppSettings.Current.LocalPortraitUrl = portraitPath;
        AppSettings.Current.PortraitUrl = url;
    }

    private async Task LoadFocusBoardsIfNeeded(UserInfo data, List<int>? customBoards)
    {
        if (customBoards == null || !customBoards.ToHashSet().SetEquals(data.CustomBoards.ToHashSet()))
        {
            AppSettings.Current.CustomBoards = SerializationHelper.TrySerialize(data.CustomBoards);
            await LoadFocusBoards(data.CustomBoards);
        }
        else
        {
            await LoadFocusBoards(customBoards);
        }
    }
    private async Task LoadFocusBoards(IEnumerable<int> boardIds)
    {
        var sections = await BoardCacheManager.Instance.GetSectionDataAsync();
        if (sections == null)
        {
            //
            Flower.Play(FlowStatus.Fail, "版面信息加载失败");
            return;
        }
        //构建查找字典
        var boardDict = sections
            .SelectMany(section => section.Boards)
            .ToDictionary(board => board.Id);

        var boardItems = boardIds.Select(id =>
        {
            boardDict.TryGetValue(id, out var board);
            string name = board?.Name ?? "未知版面";

            return new NavigationItem
            {
                Tag = id.ToString(),
                Name = name,
                IconSymbol = BoardIconHelper.GetSymbol(id, name),
                IsEditable = true
            };
        });

        MenuItems.AddRange(boardItems);
    }

    private void OnNavigationItemAdded(NavigationItem item)
    {
        //检查导航栏中是否已经存在相同Tag的项，如果存在则不添加
        if (MenuItems.OfType<NavigationItem>().Select(i => i.Tag).Contains(item.Tag))return;
        MenuItems.Add(item);
    }

    private void LoadMenuItem()
    {
        var favorite = new NavigationGroup { Name = "集锦", IsEditable = false };
        var pinnedGroup = new NavigationGroup { Name = "关注", IsEditable = true };
        MenuItems.Add(new NavigationItem
            { Name = "今日话题", IconSymbol = Symbol.Grid, Tag = "Index", IsEditable = false });
        MenuItems.Add(new NavigationItem
            { Name = "全部版面", IconSymbol = Symbol.Board, Tag = "Section", IsEditable = false });
        MenuItems.Add(new NavigationItem
            { Name = "新帖", IconSymbol = Symbol.DesignIdeas, Tag = "Discover", IsEditable = false });
        MenuItems.Add(favorite);
        MenuItems.Add(new NavigationItem { Name = "动态", IconSymbol = Symbol.Home, Tag = "Focus", IsEditable = false });
        MenuItems.Add(new NavigationItem
            { Name = "收藏集", IconSymbol = Symbol.StarLineHorizontal3, Tag = "Favorite", IsEditable = false });
        MenuItems.Add(new NavigationItem { Name = "历史", IconSymbol = Symbol.AnimalPawPrint, Tag = "History", IsEditable = false });
        MenuItems.Add(pinnedGroup);
        FooterMenuItems.Add(new NavigationItem
            { Name = "消息", IconSymbol = Symbol.MailRead, Tag = "Message", IsEditable = false });
        FooterMenuItems.Add(new NavigationItem
            { Name = "设置", IconSymbol = Symbol.StarSettings, Tag = "Setting", IsEditable = false });
    }

    private async void PinOff_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is not string boardIdStr) return;
        var url = ApiEndpoints.Board.EditFocusBoards(int.Parse(boardIdStr));
        var result = await ApiService.Delete(url);
        if (!result.IsSuccess)
        {
            //
            Flower.Play(FlowStatus.Fail, $"取消关注失败: {result.Message}");
            return;
        }
        var customBoards = AppSettings.Current.CustomBoards;
        if (string.IsNullOrEmpty(customBoards))
        {
            var boardInfo = SerializationHelper.TryDeserialize<Dictionary<int, string>>(customBoards);
            boardInfo?.Remove(int.Parse(boardIdStr));
        }

        var item = MenuItems.OfType<NavigationItem>().FirstOrDefault(g => g.Tag == boardIdStr);
        if (item != null) MenuItems.Remove(item);
    }

    

    

    private void LoadIndex()
    {
        var index = AppSettings.Current.TitlePage;
        switch (index)
        {
            case 1:
                var param = new ProfileNavigationInfo { IsMe = true };
                ContentFrame.Navigate(typeof(ProfilePage), param);
                break;
            case 2:
                ContentFrame.Navigate(typeof(DiscoverPage));
                break;
            default:
                ContentFrame.Navigate(typeof(IndexPage));
                break;
        }
      
    }

    private void InitializeTimer()
    {
        SyncTimer = new()
        {
            Interval = TimeSpan.FromSeconds(240)
        };
        SyncTimer.Tick += async (s, e) => 
        {
            // 刷新首页缓存与刷新未读数互不依赖,并行执行
            await Task.WhenAll(FetchIndex(), RefreshMessage());
        };
        SyncTimer.Start();
    }


    private static async Task<bool> FetchIndex()
    {
        var url = ApiEndpoints.Forum.Index;
        return await IndexDataService.Instance.RefreshFromApiAsync(url);
    }

    

    private void OnAppThemeChanged(ElementTheme theme)
    {
        // 更新 RootGrid 的主题
        RootGrid.RequestedTheme = theme;
    }



    private async Task RefreshMessage()
    {
        var url = ApiEndpoints.User.UnreadMessage();
        var result = await ApiService.Fetch<UnreadMessageInfo>(url);
        if (!result.IsSuccess || result.Data == null)
        {
            //await App.Logger.WriteAsync("MainWindow", "刷新未读消息失败", $"{result.StatusCode}:{result.Message}");
            return;
        }

        var data = result.Data;
        GlobalService.AtCount = data.AtCount;
        GlobalService.ReplyCount = data.ReplyCount;
        GlobalService.SystemCount = data.SystemCount;
        GlobalService.MessageCount = data.MessageCount;
        UnreadCount = data.MessageCount + data.AtCount + data.ReplyCount + data.SystemCount;
        BadgeNotificationManager.Current.SetBadgeAsCount((uint)UnreadCount);
    }


    private async Task<bool> GetFavorites()
    {
        var favoritesInfoUrl = ApiEndpoints.User.FavoritesList();
        var favoritesInfoResult = await ApiService.Fetch<FavoritesInfo>(favoritesInfoUrl);
        if (!favoritesInfoResult.IsSuccess || favoritesInfoResult.Data == null)
            //
            return false;
        var data = favoritesInfoResult.Data;
        var groups = data.FavoriteTopicGroups;
        if (groups.IsNonEmpty())
        {
            //存储收藏夹列表
            var favoJson = SerializationHelper.TrySerialize(groups);
            AppSettings.Current.FavoriteGroups = favoJson ?? "";
            return true;
        }

        Flower.Play(FlowStatus.Info, "暂无收藏夹");
        return false;
    }


    private void Back_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimatedIcon.SetState(BackIcon, "PointerOver");
    }

    private void Back_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimatedIcon.SetState(BackIcon, "Normal");
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void Navi_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is not string tag) return;
        var item = args.InvokedItem as NavigationViewItem;
        switch (tag)
        {
            case "Index":
                ContentFrame.Navigate(typeof(IndexPage));
                break;
            case "Section":
                ContentFrame.Navigate(typeof(SectionPage));
                break;
            case "Discover":
                ContentFrame.Navigate(typeof(DiscoverPage));
                break;
            case "Favorite":
                ContentFrame.Navigate(typeof(FavoritePage));
                break;
            case "Setting":
                ContentFrame.Navigate(typeof(SettingPage));
                break;
            case "Message":
                var param = new ChatNavigationInfo { HasTarget = false };
                ContentFrame.Navigate(typeof(MessagePage), param);
                break;
            case "Focus":
                ContentFrame.Navigate(typeof(FocusPage));
                break;
            case "History":
                ContentFrame.Navigate(typeof(HistoryPage));
                break;
            default:
                if (tag.All(char.IsDigit))
                {
                    try
                    {
                        ContentFrame.Navigate(typeof(BoardPage), int.Parse(tag));
                    }
                    catch
                    {
                        Flower.Play(FlowStatus.Fail, "无效的版块ID");
                    }
                }
                break;
        }
    }

    private void PaneExpand_Click(object sender, RoutedEventArgs e)
    {
        Navi.IsPaneOpen = !Navi.IsPaneOpen;
    }

    private void PaneExpand_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimatedIcon.SetState(NaviIcon, "PointerOver");
    }

    private void PaneExpand_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimatedIcon.SetState(NaviIcon, "Normal");
    }


    private void Me_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateButton(PortScaleTransform, 0.95, 0.95);
    }

    private void Me_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateButton(PortScaleTransform, 1, 1);
    }

    // 复用的头像缩放动画:避免每次指针事件都 new Storyboard/DoubleAnimation
    private readonly Storyboard _portScaleStoryboard = new();
    private readonly DoubleAnimation _portScaleX = new()
    {
        Duration = TimeSpan.FromSeconds(0.2),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };
    private readonly DoubleAnimation _portScaleY = new()
    {
        Duration = TimeSpan.FromSeconds(0.2),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    private void AnimateButton(ScaleTransform transform, double x, double y)
    {
        // 复用同一个 Storyboard,避免高频指针事件下持续分配动画对象
        _portScaleStoryboard.Stop();
        _portScaleX.To = x;
        _portScaleY.To = y;
        _portScaleStoryboard.Begin();
    }

    private void Me_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        AnimateButton(PortScaleTransform, 0.85, 0.85);
    }

    private void Me_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        AnimateButton(PortScaleTransform, 1, 1);
    }

    private void Me_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        AnimateButton(PortScaleTransform, 1, 1);
    }

    private void Me_Click(object sender, RoutedEventArgs e)
    {
        var param = new ProfileNavigationInfo { IsMe = true };
        ContentFrame.Navigate(typeof(ProfilePage), param);
    }
    

    private async void VPNConfigSave_Click(object sender, RoutedEventArgs e)
    {
        var userName = VPNUsername.Text;
        var password = VPNPassword.Password;
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
        {
            Flower.Play(FlowStatus.Fail, "用户名或密码不能为空");
            return;
        }
        var result= await LoginAsync(userName, password);
        if(result)
        {
            PasswordManager.SavePassword(userName, "VpnUserName");
            PasswordManager.SavePassword(password, "VpnPassWord");
            AppSettings.Current.IsVpnEnabled=true;
            VPNConfigDialog.Hide();
            Flower.Play(FlowStatus.Success, "已保存凭据并启用VPN");
        }
        else
        {
            Flower.Play(FlowStatus.Fail, "VPN登录失败，请检查用户名和密码");
        }
    }
    private void VPNConfigCancel_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.IsVpnEnabled = false;
        VPNConfigDialog.Hide();
    }

    /// <summary>
    /// 只负责在VPN登录时调用VPN服务的登录方法，并处理返回结果。不会直接更改UI状态。
    /// </summary>
    /// <param name="userName"></param>
    /// <param name="password"></param>
    /// <returns></returns>
    private async Task<bool> LoginAsync(string userName, string password)
    {
        try
        {
            Debug.WriteLine("开始登录");
            var vpnService = App.Current.GetService<IVpnService>();
            var res = await vpnService.LoginAsync(userName, password);
            if (res == null)
            {
                Debug.WriteLine("返回空");
                return false;
            }
            if (res.Success)
            {
                Debug.WriteLine("登录成功");
                return true;
            }
            if (res.Status == VpnLoginStatus.AccoutInvalid || res.Status == VpnLoginStatus.CaptchaFail)
            {
                //显示图形验证码和验证码框
                if (VPNPassword.IsLoaded && res.Status == VpnLoginStatus.AccoutInvalid)
                {
                    VPNPassword.Password = "";
                    VPNPassword.PlaceholderText = "输入正确凭据";
                }
                Debug.WriteLine("显示验证码输入框");
                if (CaptchaBox.IsLoaded)
                {
                    CaptchaBox.Visibility = Visibility.Visible;
                    if (res.Status == VpnLoginStatus.CaptchaFail)
                    {
                        CaptchaBox.Text = "";
                        CaptchaBox.PlaceholderText = "验证码错误";
                    }
                }
                var captchaUrl = $"https://webvpn.zju.edu.cn/captcha/{vpnService.ParameterGroup.LastCaptchaId}.png?reload={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                Debug.WriteLine("显示验证码图片");
                if (CaptchaImage.IsLoaded)
                {
                    CaptchaImage.Source = new BitmapImage(new Uri(captchaUrl));
                    CaptchaImage.Visibility = Visibility.Visible;
                }
                Debug.WriteLine("需要验证码");
                return false;
            }
            else if (res.Status == VpnLoginStatus.NeedConfirm)
            {
                //确认
                Debug.WriteLine("正在进行确认");
                return await VpnConfirmAsync();
            }
            Debug.WriteLine($"状态：{res.Status}");
            return false;

        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            return false;
            //记录异常
        }
    }
    private async Task<bool> VpnConfirmAsync()
    {
        var vpnService = App.Current.GetService<IVpnService>();
        VpnLoginResult? confirmResult;
        try
        {
            confirmResult = await vpnService.ConfirmAsync();
        }
        catch (Exception ex)
        {
            // 网络异常:避免异常逃逸出 async void 调用链导致进程崩溃
            Debug.WriteLine($"VPN确认失败: {ex.Message}");
            Flower.Play(FlowStatus.Fail, "VPN确认失败，请重试");
            AppSettings.Current.IsVpnEnabled = false;
            VPNConfigButton.Visibility = Visibility.Visible;
            DisconnectButton.Visibility = Visibility.Collapsed;
            return false;
        }
        if (confirmResult == null || !confirmResult.Success)
        {
            //可以肯定此时账户密码均正确
            //需要重试
            Flower.Play(FlowStatus.Fail, "VPN确认顶号失败，请重试");
            AppSettings.Current.IsVpnEnabled = false;
            VPNConfigButton.Visibility = Visibility.Visible;
            DisconnectButton.Visibility = Visibility.Collapsed;
            return false;
        }
        else
        {
            Flower.Play(FlowStatus.Success, "VPN连接成功");
            return true;
        }
    }
    /// <summary>
    /// 如果是启动时调用，那么，如果VPN暂未启用，就不做任何操作。
    /// </summary>
    /// <param name="isStartup"></param>
    /// <returns></returns>
    private async Task CheckVpnStatus(bool isStartup = false)
    {
        // 启动时:未启用 VPN 或凭据缺失都静默跳过——不弹配置对话框,不发起网络请求,
        // 仅保证按钮状态正确。用户点击 VPN 按钮时才进入完整检查/配置流程。
        if (isStartup && (!AppSettings.Current.IsVpnEnabled || !GlobalService.IsVpnConfigured))
        {
            VPNConfigButton.Visibility = Visibility.Visible;
            DisconnectButton.Visibility = Visibility.Collapsed;
            return;
        }
        //没有配置过VPN，或者配置过但是凭据不完整，则需要登录
        var isVpnUsable = GlobalService.IsVpnConfigured;
        if (!isVpnUsable)
        {
            VPNConfigDialog.XamlRoot = RootGrid.XamlRoot;
            try
            {
                await VPNConfigDialog.ShowAsync();
            }
            catch(Exception ex)
            {
                Flower.Play(FlowStatus.Warning, ex.Message);
            }
            //等待用户登录VPN
        }
        else
        {
            //尝试使用Cookie
            var mirrorService = App.Current.GetService<MirrorService>();
            var networkStatus = await mirrorService.CheckNetworkAsync(useVpn: true);
            //如果有效，通知连接成功
            if (networkStatus == NetworkStatus.InCampus)
            {
                //
                AppSettings.Current.IsVpnEnabled = true;
                VPNConfigButton.Visibility = Visibility.Collapsed;
                DisconnectButton.Visibility = Visibility.Visible;
                Flower.Play(FlowStatus.Success, "VPN连接成功");
            }
            else if (networkStatus == NetworkStatus.VpnCookieExpired)
            {
                await ReloginVpn();
            }
            else
            {
                //其他错误，提示用户
                AppSettings.Current.IsVpnEnabled = false;
                VPNConfigButton.Visibility = Visibility.Visible;
                DisconnectButton.Visibility = Visibility.Collapsed;
                Flower.Play(FlowStatus.Fail, $"VPN连接失败{networkStatus}");
            }
        }
        
    }
    //VPN的登录分成两种情况，一种是首次登录，另一种是凭据过期后的重新登录。此方法处理凭据过期后的重新登录。
    //重新登录是静默的，一旦弹出需要验证码，就认为凭据过期，清除凭据，要求用户重新登录。
    private async Task ReloginVpn()
    {
        var userName = PasswordManager.RetrievePassword("VpnUserName");
        var password = PasswordManager.RetrievePassword("VpnPassWord");
        if(string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
        {
            Flower.Play(FlowStatus.Fail, "VPN凭据不完整，请重新配置");
            return;
        }
        var vpnService = App.Current.GetService<IVpnService>();
        VpnLoginResult? res;
        try
        {
            res = await vpnService.LoginAsync(userName, password);
        }
        catch (Exception ex)
        {
            // 网络异常:避免异常逃逸出 async void 调用链导致进程崩溃
            Debug.WriteLine($"VPN重新登录失败: {ex.Message}");
            AppSettings.Current.IsVpnEnabled = false;
            VPNConfigButton.Visibility = Visibility.Visible;
            DisconnectButton.Visibility = Visibility.Collapsed;
            Flower.Play(FlowStatus.Fail, "VPN连接失败，请检查网络后重试");
            return;
        }
        if (res == null)
        {
            //请用户重试
            AppSettings.Current.IsVpnEnabled=false;
            VPNConfigButton.Visibility = Visibility.Visible;
            DisconnectButton.Visibility = Visibility.Collapsed;
            Flower.Play(FlowStatus.Fail, "VPN连接失败，请检查网络后重试");
            return;
        }
        
        if (res.Status == VpnLoginStatus.Success)
        {
            //通知连接成功
            AppSettings.Current.IsVpnEnabled = true;
            VPNConfigButton.Visibility = Visibility.Collapsed;
            DisconnectButton.Visibility = Visibility.Visible;
            Flower.Play(FlowStatus.Success, "VPN连接成功");
            return;
        }
        if (res.Status == VpnLoginStatus.NeedConfirm)
        {
            var success=await VpnConfirmAsync();
            AppSettings.Current.IsVpnEnabled = true;
            VPNConfigButton.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
            DisconnectButton.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
            Flower.Play(FlowStatus.Success, "VPN连接成功");
            return;
        }
        if (res.Status == VpnLoginStatus.AccoutInvalid || res.Status == VpnLoginStatus.Fail)
        {
            //密码有问题，清理旧密码，要求重新登录
            PasswordManager.RemovePassword("VpnUserName");
            PasswordManager.RemovePassword("VpnPassWord");
            VPNConfigButton.Visibility = Visibility.Visible;
            DisconnectButton.Visibility = Visibility.Collapsed;
            AppSettings.Current.IsVpnEnabled=false;
            Flower.Play(FlowStatus.Fail, "VPN套餐过期或密码已错误，请重新登录");
        }
    }

    private async void VPNConfig_Click(object sender, RoutedEventArgs e)
    {
        VPNConfigButton.Visibility=Visibility.Collapsed;
        await CheckVpnStatus();
    }

    private async void VpnPanel_Opening(object sender, object e)
    {
        string statusText;
        var mirrorService = App.Current.GetService<MirrorService>();
        if (AppSettings.Current.IsVpnEnabled)
        {
            statusText = "正在检查网络状态...";
            var res = await mirrorService.CheckNetworkAsync(true);
            statusText = MirrorService.FriendlyStatus(res);
            VPNConfigButton.Visibility = (res == NetworkStatus.InCampus) ? Visibility.Collapsed : Visibility.Visible;
            DisconnectButton.Visibility = (res == NetworkStatus.InCampus) ? Visibility.Visible : Visibility.Collapsed; ;
        }
        else
        {
            statusText = "VPN未启用";
            VPNConfigButton.Visibility = Visibility.Visible;  
        }
        VpnStatusText.Text = statusText;
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.IsVpnEnabled = false;
        Flower.Play(FlowStatus.Success, "VPN连接已断开");
        DisconnectButton.Visibility = Visibility.Collapsed;
        VPNConfigButton.Visibility = Visibility.Visible;
    }

    private void CaptchaBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var vpnService = App.Current.GetService<IVpnService>();
        vpnService.ParameterGroup.CaptchaValue = CaptchaBox.Text;
    }

    private void VPNConfigDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        VPNPassword.Password = "";
        CaptchaBox.Text = "";
        CaptchaImage.Source = null;
    }
}