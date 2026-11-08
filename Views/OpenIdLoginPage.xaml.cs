using CC98.Kernel;
using CC98.Kernel.Authorize;
using CC98.Objects;
using CC98.Services;
using CommunityToolkit.Mvvm.Messaging;
using Duende.IdentityModel.OidcClient;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class OpenIdLoginPage : Page
    {
        public OpenIdLoginPage()
        {
            InitializeComponent();
        }

        private void PasswordLoginButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ForumLoginPage));
        }

        private async void OpenIdLoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not HyperlinkButton button) return;
            button.IsEnabled = false;
            button.Content="启动浏览器…";
            try
            {
                var oidcClient = new OidcClient(new OidcClientOptions
                {
                    Authority = ApiEndpoints.OpenId.Endpoint,
                    ClientId = AppConfig.DesktopClientId,
                    RedirectUri = "cc98://callback",
                    Scope = "openid profile cc98-api cc98-card.all offline_access",
                });

                var loginState = await oidcClient.PrepareLoginAsync();
                var authorizeUrl = loginState.StartUrl;
                PasswordManager.SavePassword(loginState.State, "OpenIdState");
                PasswordManager.SavePassword(loginState.CodeVerifier, "OpenIdCodeVerifier");
                bool launched = await Launcher.LaunchUriAsync(new Uri(authorizeUrl));
                if (!launched)
                {
                    // 没有默认浏览器 / 被策略拦截:留在登录页让用户重试,不要退出
                    AppNotification notification = new AppNotificationBuilder()
                        .AddText("无法打开浏览器")
                        .AddText("请检查默认浏览器设置后重试")
                        .BuildNotification();
                    AppNotificationManager.Default.Show(notification);
                    return; // finally 里会恢复按钮
                }

                // 浏览器已接管认证。本进程必须退掉:否则 cc98://callback 会拉起第二个进程,
                // 变成"登录窗口 + 主窗口"两个进程并存。
                // 但不要在这里直接写 Task.Delay + App.Current.Exit():
                // Launcher 的 COM 调用可能还在收尾,此时拆掉 UI 调度器会抛 CoreMessagingXP 的
                // stowed exception(事件日志 0xc000027b),表现为进程崩溃。
                // 统一交给 App 做"延后 + 兜异常"的安全退出。
                App.Current.QuitAfterBrowserLogin();
            }
            catch (Exception ex)
            {
                // 网络不可达/超时/协议错误:避免异常逃逸出 async void 导致进程崩溃,恢复按钮供重试
                AppNotification notification = new AppNotificationBuilder()
                .AddText("OpenID拉起失败")
                .AddText(ex.Message)
                .BuildNotification();
                AppNotificationManager.Default.Show(notification);
                Debug.WriteLine($"OpenID 登录失败: {ex.Message}"); 
            }
            finally
            {
                button.IsEnabled = true;
                button.Content = "一键登录";
            }
        }
    }
}
