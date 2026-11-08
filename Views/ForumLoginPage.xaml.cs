using CC98.Kernel;
using CC98.Kernel.Authorize;
using CC98.Kernel.Network;
using CC98.Objects;
using CC98.Services;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using CC98.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ForumLoginPage : Page
    {
        public ForumLoginPage()
        {
            InitializeComponent();
        }
        //用于标记checkbox的操作类型
        private bool isUserOperated = true;
        public static ApiService ApiService => App.Current.GetService<ApiService>();
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }
      



        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var mirrorService = App.Current.GetService<MirrorService>();
            var status = await mirrorService.CheckNetworkAsync(AppSettings.Current.IsVpnEnabled);
            if (status != NetworkStatus.InCampus)
            {
                AppSettings.Current.IsVpnEnabled = false;
                isUserOperated = false;
                EnableVpn.IsChecked = false;
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = MirrorService.FriendlyStatus(status) });
                return;
            }
            
            string userName = UserNameBox.Text;
            string password = PasswordBox.Password;
            if (string.IsNullOrEmpty(userName))
            {
                //
                UserNameBox.PlaceholderText = "用户名和密码不可为空";
                return;
            }
            if(string.IsNullOrEmpty(password))
            {
                PasswordBox.PlaceholderText = "用户名和密码不可为空";
                return;
            }
            
            VisualStateManager.GoToState(this, "Logging", true);
            await LoginAsync(userName, password);
        }
        private async Task LoginAsync(string userName, string password)
        {
            try
            {
                var loginService = App.Current.GetService<LoginService>();
                var appConfig = App.Current.GetService<AppConfig>();
                var res = await loginService.LoginWithPasswordAsync(userName, password);
                if (res == null || res.IsError)
                {
                    //
                    WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = res?.ErrorDescription?? res?.Error ?? "登录失败" });
                    VisualStateManager.GoToState(this, "Fail", true);
                    return;
                }
                //登录成功
                AppSettings.Current.ActiveMode = (int)ActiveMode.Password;
                AppSettings.Current.IsActive = true;
                LaunchApp();

            }
            catch (Exception ex)
            {
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = ex.Message });
                VisualStateManager.GoToState(this, "Fail", true);
                //记录异常
            }
        }
        private static void LaunchApp()
        {
            try
            {
                App.Current.AppMainWindow = new MainWindow();
                App.Current.AppMainWindow.Activate();
                App.Current.LoginWindow.Close();
                //释放对已关闭登录窗口的引用
                App.Current.LoginWindow = null;
            }
            catch(Exception ex)
            {
                //记录异常
                Debug.WriteLine($"启动主窗口失败: {ex}");
            }
        }

        

        private void OpenIdLoginButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(OpenIdLoginPage));
        }

        private async void CheckNetworkButton_Click(object sender, RoutedEventArgs e)
        {
            var mirrorService = App.Current.GetService<MirrorService>();
            var status = await mirrorService.CheckNetworkAsync(AppSettings.Current.IsVpnEnabled);
            WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Information, Message = MirrorService.FriendlyStatus(status) });
        }
        
        private async void EnableVpn_Checked(object sender, RoutedEventArgs e)
        {
            if (!isUserOperated)
            {
                isUserOperated = true;
                return;
            }
            EnableVpn.IsEnabled = false;
            await CheckVpnStatus();
            EnableVpn.IsEnabled = true;
        }
        private async void VPNConfigSave_Click(object sender, RoutedEventArgs e)
        {
            var userName = VPNUsername.Text;
            var password = VPNPassword.Password;
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            {
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = "用户名和密码不可为空" });
                return;
            }
            var result = await LoginVpnAsync(userName, password);
            if (result)
            {
                PasswordManager.SavePassword(userName, "VpnUserName");
                PasswordManager.SavePassword(password, "VpnPassWord");
                AppSettings.Current.IsVpnEnabled = true;
                isUserOperated = false;
                EnableVpn.IsChecked = true;
                VPNConfigDialog.Hide();
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Success, Message = "已保存凭据并启用VPN" });
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = "登录失败" });
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
        private async Task<bool> LoginVpnAsync(string userName, string password)
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
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = "VPN确认失败，请重试" });
                AppSettings.Current.IsVpnEnabled = false;
                return false;
            }
            if (confirmResult == null || !confirmResult.Success)
            {
                //可以肯定此时账户密码均正确
                //需要重试
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = "VPN确认顶号失败，请重试" });
                AppSettings.Current.IsVpnEnabled = false;

                return false;
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Success, Message = "VPN连接成功" });
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
            if (isStartup && !AppSettings.Current.IsVpnEnabled)
            {
                return;
            }
            //没有配置过VPN，或者配置过但是凭据不完整，则需要登录
            var isVpnUsable = GlobalService.IsVpnConfigured;
            if (!isVpnUsable)
            {
                VPNConfigDialog.XamlRoot = RootGrid.XamlRoot;
                try
                {
                    isUserOperated = false;
                    EnableVpn.IsChecked = false;
                    await VPNConfigDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Warning, Message = ex.Message });
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
                    isUserOperated = false;
                    EnableVpn.IsChecked = true;
                    WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Success, Message = "VPN连接成功" });
                }
                else if (networkStatus == NetworkStatus.VpnCookieExpired)
                {
                    await ReloginVpn();
                }
                else
                {
                    //其他错误，提示用户
                    AppSettings.Current.IsVpnEnabled = false;
                    isUserOperated = false;
                    EnableVpn.IsChecked = false;
                    WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = $"VPN连接失败{networkStatus}" });
                }
            }

        }
        //VPN的登录分成两种情况，一种是首次登录，另一种是凭据过期后的重新登录。此方法处理凭据过期后的重新登录。
        //重新登录是静默的，一旦弹出需要验证码，就认为凭据过期，清除凭据，要求用户重新登录。
        private async Task ReloginVpn()
        {
            var userName = PasswordManager.RetrievePassword("VpnUserName");
            var password = PasswordManager.RetrievePassword("VpnPassWord");
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            {
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = "VPN凭据不完整，请重新配置" });
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
                isUserOperated = false;
                EnableVpn.IsChecked = false;
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = "VPN连接失败，请检查网络后重试" });
                return;
            }
            if (res == null)
            {
                //请用户重试
                AppSettings.Current.IsVpnEnabled = false;
                isUserOperated = false;
                EnableVpn.IsChecked = false;
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = "VPN连接失败，请检查网络后重试" });
                return;
            }

            if (res.Status == VpnLoginStatus.Success)
            {
                //通知连接成功
                AppSettings.Current.IsVpnEnabled = true;
                isUserOperated = false;
                EnableVpn.IsChecked = true;
                WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Success, Message = "VPN连接成功" });
                return;
            }
            if (res.Status == VpnLoginStatus.NeedConfirm)
            {
                isUserOperated = false;
                var success = await VpnConfirmAsync();
                EnableVpn.IsChecked = success;
                AppSettings.Current.IsVpnEnabled = success;
                return;
            }
            //密码有问题，清理旧密码，要求重新登录
            PasswordManager.RemovePassword("VpnUserName");
            PasswordManager.RemovePassword("VpnPassWord");

            AppSettings.Current.IsVpnEnabled = false;
            isUserOperated = false;
            EnableVpn.IsChecked = false;
            WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Error, Message = "VPN套餐过期或密码已错误，请重新登录" });
        }

        private void EnableVpn_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!isUserOperated)
            {
                isUserOperated = true;
                return;
            }
            AppSettings.Current.IsVpnEnabled = false;
            WeakReferenceMessenger.Default.Send(new TipMessage { Type = InfoType.Information, Message = "WebVPN已禁用" });
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if(AppSettings.Current.IsVpnEnabled)
            {
                EnableVpn.IsChecked = true;
            }
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
}
