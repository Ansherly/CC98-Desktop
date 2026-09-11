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
                await Launcher.LaunchUriAsync(new Uri(authorizeUrl));

                await Task.Delay(200);
                App.Current.Exit();
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
