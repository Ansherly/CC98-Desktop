using CC98.Kernel;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace CC98.Services;

/// <summary>
///     应用设置单例。将设置持久化到 ApplicationData.Current.LocalSettings。
///     可在 XAML 中作为 StaticResource 或绑定源使用，支持 PropertyChanged 通知。
/// </summary>
public sealed partial class AppSettings : INotifyPropertyChanged
{
    //需要迁移的设置项

    private ApplicationDataContainer LocalSettings => ApplicationData.Current.LocalSettings;
    public static AppSettings Current { get; } = new();

    private T? GetValue<T>(string key, T? defaultValue = default)
    {
        if (LocalSettings.Values.TryGetValue(key, out var value) && value is T propertyValue)
        {
            return propertyValue;
        }
        else
        {
            
            return defaultValue;
        }
    }
    private bool SetValue(string key, object value)
    {
        try
        {
            if (LocalSettings.Values.TryGetValue(key, out var existing))
            {
                if (object.Equals(existing, value))
                {
                    return true; // 值未变化，不触发通知
                }
            }

            LocalSettings.Values[key] = value;
            //传入属性名
            OnPropertyChanged(key);
            return true;
        }
        catch
        {
            // 忽略存储异常（例如权限等），不触发通知
            return false;
        }
    }
    public bool HideImage
    {
        get => GetValue<bool>(nameof(HideImage));
        set => SetValue(nameof(HideImage), value);
    }
    public bool ShowBigPaper
    {
        get => GetValue<bool>(nameof(ShowBigPaper), true);
        set=> SetValue(nameof(ShowBigPaper), value);
    }
    public bool IsActive
    {
        get => GetValue<bool>(nameof(IsActive));
        set => SetValue(nameof(IsActive), value);
    }
    public int ActiveMode
    {
        get => GetValue<int>(nameof(ActiveMode));
        set => SetValue(nameof(ActiveMode), value);
    }
    public string UserName
    {
        get => GetValue<string>(nameof(UserName)) ?? string.Empty;
        set => SetValue(nameof(UserName), value);
    }
    public int UserId
    {
        get => GetValue<int>(nameof(UserId));
        set => SetValue(nameof(UserId), value);
    }
    public int TitlePage
    {
        get => GetValue<int>(nameof(TitlePage));
        set => SetValue(nameof(TitlePage), value);
    }
    public int Theme 
    {         
        get => GetValue<int>(nameof(Theme));
        set => SetValue(nameof(Theme), value);
    }
    public int Effect
    {
        get => GetValue<int>(nameof(Effect));
        set => SetValue(nameof(Effect), value);
    }
    public double ImageMaxWidth
    {
        get=> GetValue<double>(nameof(ImageMaxWidth),400);
        set=> SetValue(nameof(ImageMaxWidth), value);
    }
    public bool IsTailVisible
    {
        get => GetValue<bool>(nameof(IsTailVisible));
        set => SetValue(nameof(IsTailVisible), value);
    }
    public string CustomBoards
    {
        get => GetValue<string>(nameof(CustomBoards)) ?? string.Empty;
        set => SetValue(nameof(CustomBoards), value);
    }
    public string FavoriteGroups
    {
        get => GetValue<string>(nameof(FavoriteGroups)) ?? string.Empty;
        set => SetValue(nameof(FavoriteGroups), value);
    }
    public string ThemePicture
    {
        get
        {
            var defaultThemePicture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Themes", "new_year_2026.jpg");
            return GetValue<string>(nameof(ThemePicture),defaultThemePicture) ?? defaultThemePicture;
        }
        set => SetValue(nameof(ThemePicture), value);
    }
    public string PortraitUrl
    {
        get => GetValue<string>(nameof(PortraitUrl)) ?? string.Empty;
        set => SetValue(nameof(PortraitUrl), value);
    }
    public string LocalPortraitUrl
    {
        get => GetValue<string>(nameof(LocalPortraitUrl)) ?? string.Empty;
        set => SetValue(nameof(LocalPortraitUrl), value);
    }
    public bool IsVpnEnabled
    {
        get => GetValue<bool>(nameof(IsVpnEnabled));
        set => SetValue(nameof(IsVpnEnabled), value);
    }
    public DateTime TokenExpireAt
    {
        get => GetValue<DateTime>(nameof(TokenExpireAt));
        set => SetValue(nameof(TokenExpireAt), value);
    }
    public string LittleTail
    {
        get => GetValue<string>(nameof(LittleTail), AppConfig.DefaultLittleTail)??"";
        set => SetValue(nameof(LittleTail), value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    //[调用成员名]属性只有在get;set中起作用。如果在SetValue中使用，不手动指定属性名，UI将不会正常刷新
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}