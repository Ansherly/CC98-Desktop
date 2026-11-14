using CC98.Objects;
using CC98.Services;
using CC98.Services.Extensions;
using DevWinUI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Views;

/// <summary>
///     设置页面。
/// </summary>
public sealed partial class SettingPage : Page
{
    public int lastEffect = 0;
    public ObservableCollection<ThemePicture> ThemePictures = [];
    public ApplicationDataContainer Set = ApplicationData.Current.LocalSettings;
    public SettingPage()
    {
        InitializeComponent();
        LoadSettings();
        LoadThemePicture();
    }

    //第一次进入时，初始化设置项。
    //如果项存在且有值，为选项赋值。


    private void LoadSettings()
    {
        var effect=AppSettings.Current.Effect;
        lastEffect = effect;
        switch (effect)
        {
            case 0:
                Mica.IsChecked = true;
                break;
            case 1:
                MicaAlt.IsChecked = true;
                break;
            case 2:
                AcrylicBase.IsChecked = true;
                break;
            case 3:
                AcrylicThin.IsChecked = true;
                break;
            default:
                Mica.IsChecked = true;
                break;
        }
      
        var theme = AppSettings.Current.Theme;
        switch (theme)
        {
            case 0:
                Follow.IsChecked = true;
                break;
            case 1:
                Light.IsChecked = true;
                break;
            case 2:
                Dark.IsChecked = true;
                break;
            default:
                Follow.IsChecked = true;
                break;
        }

    }

    private void ToFeedBack_Click(object sender, RoutedEventArgs e)
    {
        //指向开发记录楼
        var param = new TopicNavigationInfo
        {
            IsJumpingMode = false,
            TopicId = 6173309
        };
        Frame.Navigate(typeof(TopicPage), param);
    }


    private void Effect_Checked(object sender, RoutedEventArgs e)
    {
        if(sender is not RadioButton button) return;
        var effect = button.Tag.ToInt();
        if (effect == lastEffect) return; //当选项与原设置不同时，才进行设置。
        AppSettings.Current.Effect = effect;
        lastEffect = effect;
        App.Current.AppMainWindow.SystemBackdrop = effect switch
        {
            0 => new MicaSystemBackdrop(),
            1 => new MicaSystemBackdrop(MicaKind.BaseAlt),
            2 => new AcrylicSystemBackdrop(),
            3 => new AcrylicSystemBackdrop(DesktopAcrylicKind.Thin),
            4 => null,
            _ => new MicaSystemBackdrop()
        };
    }

    private void Light_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button) return;
        var theme = button.Tag.ToInt();
        AppSettings.Current.Theme = theme;
        var elementTheme = theme switch
        {
            0 => ElementTheme.Default,
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        App.RaiseThemeChanged(elementTheme);
    }


    private void LoadThemePicture()
    {
        var themesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Themes");
        var files = Directory.GetFiles(themesPath, "*.jpg", SearchOption.AllDirectories);
        ThemePictures.Clear();
        foreach (var file in files)
        {
            var filename = Path.GetFileName(file);
            ThemePictures.Add(new() { FileName = filename, FilePath = file });
        }

        ThemesGrid.ItemsSource = ThemePictures;
    }

    private void ThemesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemesGrid.SelectedItem == null) return;
        var selected = ThemePictures[ThemesGrid.SelectedIndex];
        AppSettings.Current.ThemePicture = selected.FilePath;
    }


    private void LocalSetManager_Expanded(object sender, EventArgs e)
    {
        var settings = "";
        foreach (var item in Set.Values)
        {
            //不显示收藏夹，美化打印太长
            if (item.Key == "FavoriteGroups") continue;
            settings += $"{item.Key}:{item.Value}\n";
        }

        LocalSet.Text = settings;

        LocalSetManager.IsExpanded = true;
    }

    private async void SwitchUser_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.IsActive = false;
        await ClearAppCacheAsync();
        PasswordManager.Logout();
        //重启应用
        AppInstance.Restart("");
    }
    /// <summary>
    /// 清理应用缓存，包括本地缓存文件夹中的所有文件和子文件夹。防止生成多份头像。
    /// </summary>
    /// <returns></returns>
    public async Task ClearAppCacheAsync()
    {
        try
        {
            StorageFolder localCacheFolder = ApplicationData.Current.LocalCacheFolder;
            var items = await localCacheFolder.GetItemsAsync();
            foreach (var item in items)
            {
                if (item is StorageFile file)
                {
                    await file.DeleteAsync();
                }
                else if (item is StorageFolder folder)
                {
                    await folder.DeleteAsync();
                }
            }

            // 头像缓存就在这个目录里:清掉文件后必须同时清掉记录,
            // 否则 LocalPortraitUrl 会留下一个指向已删除文件的悬空路径。
            AppSettings.Current.LocalPortraitUrl = string.Empty;

            Debug.WriteLine("应用缓存已成功清理。");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"清理缓存时发生错误: {ex.Message}");
            // 处理异常，例如文件正在使用中
        }
    }

    private void ToContributor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton button) return;
        var userId = button.Tag.ToInt();
        Frame.Navigate(typeof(ProfilePage), new ProfileNavigationInfo { IsMe = AppSettings.Current.UserId == userId, UserId = userId });
    }
}

