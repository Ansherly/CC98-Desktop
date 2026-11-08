using CC98.Kernel;
using CC98.Kernel.Authorize;
using CC98.Objects;
using CC98.Services;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI.WindowManagement;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     登录窗口。
/// </summary>
public sealed partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(GridTitleBar);
        RootGrid.RequestedTheme = ElementTheme.Light;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "cc98.ico");
        AppWindow.SetIcon(iconPath);
        AppWindow.SetTaskbarIcon(iconPath);
        SetWindowState();
    }
    /// <summary>
    /// 适应高分屏，设置窗口大小为基于2560分辨率的缩放值，并居中显示。
    /// </summary>
    private void SetWindowState()
    {
        const int baseWidth = 1440;
        const int baseHeight = 920;

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(true, true);
        AppWindow.SetPresenter(presenter);

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        int screenWidth = displayArea.WorkArea.Width;  // 物理分辨率宽度

        // 计算 scale（基于2560基准）
        double scale = screenWidth / 2560.0;
        // 可选：限制范围，避免窗口过小或过大
        scale = Math.Clamp(scale, 0.6, 1.5);
        // 计算最终想要的内容区大小（逻辑像素）
        int desiredClientWidth = (int)(baseWidth * scale);
        int desiredClientHeight = (int)(baseHeight * scale);
        AppWindow.ResizeClient(new SizeInt32(desiredClientWidth, desiredClientHeight));

        //居中
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)?.WorkArea;
        if (area == null) return;
        AppWindow.Move(new((area.Value.Width - AppWindow.Size.Width) / 2,(area.Value.Height - AppWindow.Size.Height) / 2));

        WeakReferenceMessenger.Default.Register<TipMessage>(this, (r, message) =>
        {
            this.DispatcherQueue.TryEnqueue(() => Tip.Show(message.Type, message.Message));
        });
        //关闭时注销 Messenger 注册,避免在 WeakReferenceMessenger 中残留死条目
        Closed += (_, _) => WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoginFrame.Navigate(typeof(OpenIdLoginPage));
        }
        catch(Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
        
    }

    private void GuideButton_Click(object sender, RoutedEventArgs e)
    {
        LoginFrame.Navigate(typeof(GuidePage));
    }
}