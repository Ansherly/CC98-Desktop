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

        // mpv 原生库必须在第一次播放前把路径注册好(找不到就降级为不可播放)
        CC98.Kernel.Mpv.MpvEnvironment.Initialize();

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

        var e = AppInstance.GetActivatedEventArgs();
        if (e is ProtocolActivatedEventArgs protocol)
        {
            var redirectUrl = protocol.Uri.ToString();
            bool success = await AuthFromProtocol(redirectUrl);
            if (success)
            {
                AppSettings.Current.IsActive = true;
                AppSettings.Current.ActiveMode = (int)ActiveMode.OpenId;
                AppMainWindow = new MainWindow();
                AppMainWindow.Activate();
            }
            return;
        }

        bool isActive = AppSettings.Current.IsActive;
        if (!isActive)
        {
            LoginWindow = new LoginWindow();
            LoginWindow.Activate();
        }
        else
        {
            AppMainWindow = new MainWindow();
            AppMainWindow.Activate();
        }
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
    /// 完成回调过程
    /// </summary>
    /// <param name="e"></param>
    /// <returns></returns>
    private async Task<bool> AuthFromProtocol(string callback)
    {
        var clientState = PasswordManager.RetrievePassword("OpenIdState");
        PasswordManager.RemovePassword("OpenIdState");
    
        var verifier = PasswordManager.RetrievePassword("OpenIdCodeVerifier");
        PasswordManager.RemovePassword("OpenIdCodeVerifier");

        if(string.IsNullOrEmpty(clientState)||string.IsNullOrEmpty(verifier))
        {
            throw new Exception("缺少必要的认证参数");
        }

        try
        {
            var res = await LoginService.LoginWithCodeAsync(callback, verifier, clientState);
            if (res == null)
            {
                ShowAppNotification("登录失败", "请检查网络后重试");
                return false;
            }
            if (res.IsError)
            {
                ShowAppNotification("登录失败", res.ErrorDescription ?? res.Error?? "未知错误");
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


    private static void ShowAppNotification(string title, string subtitle = "", string message = "")
    {
        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(subtitle)
            .AddText(message)
            .BuildNotification();
        AppNotificationManager.Default.Show(notification);
    }

    #endregion
}