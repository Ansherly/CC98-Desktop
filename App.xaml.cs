using CC98.Kernel;
using CC98.Kernel.Authorize;
using CC98.Kernel.Bilibili;
using CC98.Kernel.Network;
using CC98.Objects;
using CC98.Services;
using CC98.Services.Helpers;
using CC98.Views;
using DevWinUI;
using Duende.IdentityModel.OidcClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 获取应用程序的当前实例。
    /// </summary>
    public new static App Current => (App)Application.Current;
    public Window AppMainWindow { get; set; }
    public Window LoginWindow { get; set; }

    public LoginService LoginService =>Current.GetService<LoginService>();

    /// <summary>
    /// 内存缓存服务。
    /// </summary>
    public IMemoryCache MemoryCache { get; } = new MemoryCache(new MemoryCacheOptions());

    public static event Action<ElementTheme>? ThemeChanged;

    public static void RaiseThemeChanged(ElementTheme theme)
    {
        ThemeChanged?.Invoke(theme);
    }
    public App()
    {
        InitializeComponent();

        try
        {
            RegisterServices();
        }
        catch(Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }




    #region 应用启动
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        //先判断本次启动是否由 cc98:// 协议回调唤起(细节见 TryGetProtocolRedirectUrl)
        var redirectUrl = TryGetProtocolRedirectUrl();
        if (redirectUrl is not null)
        {
            bool success = await AuthFromProtocol(redirectUrl);
            if (!success)
            {
                //认证失败/参数缺失时不能直接 return:那样进程既没有窗口也没有提示,
                //用户只会看到"浏览器认证完什么都没发生"。一律退回登录窗口让用户重来。
                ShowLoginWindow();
                return;
            }

            AppSettings.Current.IsActive = true;
            AppSettings.Current.ActiveMode = (int)ActiveMode.OpenId;
            AppMainWindow = new MainWindow();
            AppMainWindow.Activate();
            return;
        }

        if (!AppSettings.Current.IsActive)
        {
            ShowLoginWindow();
        }
        else
        {
            AppMainWindow = new MainWindow();
            AppMainWindow.Activate();
        }
    }

    /// <summary>
    /// 取出 cc98://callback 的重定向地址;本次启动不是协议激活时返回 null。
    /// </summary>
    /// <remarks>
    /// 为什么不再写 <c>e is ProtocolActivatedEventArgs</c>:实测(AOT 打包版 / IL 版对照)
    /// PublishAot=True 时 CsWinRT 不会把 AppInstance.GetActivatedEventArgs() 的返回值投影成具体的
    /// 运行时类,而是退化成裸 WinRT.IInspectable,于是 <c>is ProtocolActivatedEventArgs</c> 恒为 false,
    /// 回调分支永远进不去——这正是"打包版浏览器认证后又被拉回登录窗口、VS 调试却正常"的原因。
    /// 与裁剪无关:只开 PublishTrimmed + TrimMode=full 的 IL 版本投影仍然正常,只有 AOT 会退化。
    /// Kind 枚举与接口投影(IProtocolActivatedEventArgs)在 AOT 下都正常,命令行也能拿到同一个 URI,
    /// 所以这里判 Kind + 接口投影,再用命令行兜底。
    /// </remarks>
    private static string? TryGetProtocolRedirectUrl()
    {
        try
        {
            var e = AppInstance.GetActivatedEventArgs();
            if (e is { Kind: ActivationKind.Protocol } && e is IProtocolActivatedEventArgs protocol)
            {
                var uri = protocol.Uri?.ToString();
                if (!string.IsNullOrEmpty(uri)) return uri;
            }
        }
        catch (Exception ex)
        {
            //读不到激活参数不应该影响正常启动
            Debug.WriteLine($"读取激活参数失败:{ex.Message}");
        }

        //兜底:打包(full-trust)应用的协议激活会把 URI 作为第一个命令行参数传进来,
        //实测形如 "CC98 Desktop.exe" cc98://callback/?code=...&state=...
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith(AppConfig.OpenIdCallbackUrL, StringComparison.OrdinalIgnoreCase))
                return arg;
        }

        return null;
    }

    /// <summary>
    /// 显示登录窗口(必须在 UI 线程调用)。
    /// </summary>
    private void ShowLoginWindow()
    {
        LoginWindow = new LoginWindow();
        LoginWindow.Activate();
    }

    //浏览器认证后退出流程的状态,见 QuitAfterBrowserLogin
    private bool _isQuitting;
    private DispatcherQueueTimer? _quitTimer;

    /// <summary>
    /// 浏览器接管 OpenID 认证后,把本进程安全地退掉。
    /// </summary>
    /// <remarks>
    /// 登录页不能自己写 <c>await Task.Delay(200); App.Current.Exit();</c>:
    /// Launcher.LaunchUriAsync 的 COM 调用可能还在收尾,此时拆掉 UI 调度器会在事件日志里
    /// 留下 CoreMessagingXP 的 stowed exception(0xc000027b),表现为进程崩溃。
    /// 这里把 Exit 延后到调度器队列上执行,并兜住异常——最差情况只是没退出,而不是崩掉。
    /// </remarks>
    public void QuitAfterBrowserLogin()
    {
        if (_isQuitting) return;
        _isQuitting = true;

        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null)
        {
            //没有调度器(极端情况):直接退出
            try { Exit(); } catch (Exception ex) { Debug.WriteLine($"退出失败:{ex.Message}"); }
            return;
        }

        //DispatcherQueueTimer 必须存字段,否则会被 GC 回收,回调永远等不到
        _quitTimer = queue.CreateTimer();
        _quitTimer.Interval = TimeSpan.FromMilliseconds(300);
        _quitTimer.IsRepeating = false;
        _quitTimer.Tick += (_, _) =>
        {
            _quitTimer?.Stop();
            _quitTimer = null;
            try
            {
                Exit();
            }
            catch (Exception ex)
            {
                //退出失败也不能把异常抛到调度器之外
                Debug.WriteLine($"退出失败:{ex.Message}");
                //允许用户重新发起登录再试一次
                _isQuitting = false;
            }
        };
        _quitTimer.Start();
    }


    private void TokenService_AuthenticationFailed(object? sender, AuthenticationFailedEventArgs e)
    {
        AppSettings.Current.IsActive = false;
        ShowAppNotification("需要重新登录", UserFriendlyExceptionMessage(e.Reason), e.Exception?.Message ?? "");
        try
        {
            AppMainWindow.Close();
            //释放对已关闭主窗口的引用:否则旧窗口连同 ContentFrame 后退栈与全部缓存页面被本属性钉住
            AppMainWindow = null;
            LoginWindow = new LoginWindow();
            LoginWindow.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }
    private static string UserFriendlyExceptionMessage(string reason)
    {
        return reason switch
        {
            "invalid_grant" => "令牌已过期",
            "invalid_client" => "客户端凭证错误,客户端配置可能被98官方改动",
            _ => reason
        };
    }

    #endregion

    #region 依赖注入
   
    //必须是实例成员。
    private IServiceProvider Services;
    private void RegisterServices()
    {
        //直接使用 ServiceCollection 构建容器,避免 Host 额外加载 Logging/Configuration 等程序集,加快冷启动。
        var services = new ServiceCollection();
        services.AddSingleton<AppConfig>();
        //Cookie容器
        var cookieContainer = new CookieContainer();
        services.AddSingleton(cookieContainer);
        //VPN服务和委托处理器
        services.AddSingleton<ICookieService, CookieService>();
        //Token服务
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IVpnService, VpnService>();
        services.AddTransient<VpnMessageHandler>();
        services.AddTransient<TokenHandler>();
        services.AddTransient<MirrorVpnMessageHandler>();

        //HttpClient,用于VPN连接
        services.AddHttpClient("VpnClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.BaseAddress = new Uri("https://webvpn.zju.edu.cn");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            client.DefaultRequestHeaders.Connection.ParseAdd("keep-alive");
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            return new HttpClientHandler
            {
                CookieContainer = cookieContainer,
            };
        });

        //用于论坛业务
        services.AddHttpClient("ForumClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .AddHttpMessageHandler<VpnMessageHandler>()
        .AddHttpMessageHandler<TokenHandler>()
        .AddStandardResilienceHandler();
        //用于认证
        services.AddHttpClient("IdentityClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .AddHttpMessageHandler<VpnMessageHandler>()
        .AddStandardResilienceHandler();
        //始终连接VPN的HttpClient,用于镜像站网络检测
        services.AddHttpClient("MirrorClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .AddHttpMessageHandler<MirrorVpnMessageHandler>()
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            return new HttpClientHandler
            {
                AllowAutoRedirect=false
            };
        });

        services.AddHttpClient("DefaultClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        //访问 B 站开放接口(UBB [bili] 内嵌播放)。刻意独立于论坛链路:
        //既不能带上 CC98 的令牌,VPN 开启时也不能把 B 站域名改写进 webvpn。
        //超时交给每个请求自己的取消令牌(媒体动辄几十 MB,不能被客户端超时掐断)。
        services.AddHttpClient("BiliClient", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Safari/537.36 Edg/116.0.1938.69");
            client.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            //Cookie 由 BilibiliApi 逐请求显式写入(buvid3/buvid4),不让 handler 自建容器
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
        });

        //论坛登录服务
        services.AddSingleton<LoginService>();
        //主业务服务
        services.AddSingleton<ApiService>();
        //B 站匿名接口(UBB [bili] 内嵌播放)
        services.AddSingleton<BilibiliApi>();
        //镜像站服务
        services.AddSingleton<MirrorService>();

        Services = services.BuildServiceProvider();
        var tokenService = GetService<ITokenService>();
        tokenService.AuthenticationFailed += TokenService_AuthenticationFailed;
    }

    /// <summary>
    /// 公开方法。允许外部获取服务实例。在发布前确保所有服务都已注册。该方法必须通过App.Current.GetService<T>()的形式调用。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T GetService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    #endregion

    #region 认证

    /// <summary>
    /// 用回调 URL 完成令牌交换。失败返回 false,由调用方决定后续界面。
    /// </summary>
    /// <param name="callback">cc98://callback?code=...&state=... 形式的回调地址</param>
    /// <returns>是否登录成功</returns>
    private async Task<bool> AuthFromProtocol(string callback)
    {
        //注意:OnLaunched 是 async void,从这里逃逸的异常会直接杀掉进程
        //(事件日志表现为 CoreMessagingXP 0xc000027b 的 MoAppCrash)。
        //所以取凭据、参数校验、换令牌全部放在 try 里,任何一步失败都只返回 false。
        try
        {
            var clientState = PasswordManager.RetrievePassword("OpenIdState");
            PasswordManager.RemovePassword("OpenIdState");

            var verifier = PasswordManager.RetrievePassword("OpenIdCodeVerifier");
            PasswordManager.RemovePassword("OpenIdCodeVerifier");

            if (string.IsNullOrEmpty(clientState) || string.IsNullOrEmpty(verifier))
            {
                //常见原因:用户在浏览器里放弃授权;或上次发起的登录残留已被清理。
                //以前这里直接 throw,会把整个进程带走(只留一条 WER 记录),
                //现在改为提示后回登录窗口,让用户重新发起。
                ShowAppNotification("登录未完成", "缺少必要的认证参数,请在应用内重新发起登录");
                return false;
            }

            var res = await LoginService.LoginWithCodeAsync(callback, verifier, clientState);
            if (res == null)
            {
                ShowAppNotification("登录失败", "请检查网络后重试");
                return false;
            }
            if (res.IsError)
            {
                ShowAppNotification("登录失败", res.ErrorDescription ?? res.Error ?? "未知错误");
                return false;
            }
            ShowAppNotification("登录成功", $"欢迎回家~{res.User.Identity?.Name}前辈");
            return true;
        }
        catch (Exception ex)
        {
            ShowAppNotification("登录失败", "发生异常", ex.Message);
            return false;
        }
    }



    #endregion

    

    #region 其他


    public static string GetValue(NameValueCollection collection, string key)
    {
        if (collection.AllKeys.Contains(key))
        {
            var value = collection[key];
            if (value is string str) return str;
        }

        return "0";
    }


    /// <summary>
    /// 弹一条应用通知。
    /// </summary>
    /// <remarks>
    /// 通知本身可能失败(未注册/通道不可用等),而调用方都是启动/回调这类
    /// 不容许抛异常出去的路径,所以失败只记日志,不影响业务流程。
    /// </remarks>
    private static void ShowAppNotification(string title, string subtitle = "", string message = "")
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(subtitle)
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"通知发送失败:{title} / {ex.Message}");
        }
    }

    #endregion
}