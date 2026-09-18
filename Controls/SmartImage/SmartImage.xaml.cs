using System;
using CC98.Services.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Controls.SmartImage;

public sealed partial class SmartImage : UserControl
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(object), typeof(SmartImage),
            new(null, OnSourceChanged));

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(SmartImage),
            new(null));

    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(
            nameof(Stretch),
            typeof(Stretch),
            typeof(SmartImage),
            new(Stretch.Uniform));

    public static readonly DependencyProperty UseWebVpnProperty =
        DependencyProperty.Register(
            nameof(UseWebVpn),
            typeof(bool),
            typeof(SmartImage),
            new(true));


    public SmartImage()
    {
        InitializeComponent();
    }

    public object Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ImageSource? ImageSource
    {
        get => GetValue(ImageSourceProperty) as ImageSource;
        set => SetValue(ImageSourceProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public bool UseWebVpn
    {
        get => (bool)GetValue(UseWebVpnProperty);
        set => SetValue(UseWebVpnProperty, value);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SmartImage control) control.LoadImage();
    }

    // 加载代次:Source 快速变化/控件复用时,旧请求完成不得覆盖新请求的结果
    private int _loadGeneration;

    private async void LoadImage()
    {
        var generation = ++_loadGeneration;
        if (Source == null)
        {
            InnerImage.Source = null;
            return;
        }

        try
        {
            LoadingIndicator.IsActive = true;
            InnerImage.Opacity = 0.5;

            switch (Source)
            {
                case string url when UrlEx.IsWebUrl(url):
                    var webBitmap = await ImageHelper.LoadWebImageAsync(url);
                    if (generation != _loadGeneration) return;
                    InnerImage.Source = webBitmap;
                    ImageSource = webBitmap;
                    break;

                case string path when UrlEx.IsLocalPath(path):
                    var localBitmap = await ImageHelper.LoadLocalImage(path);
                    if (generation != _loadGeneration) return;
                    InnerImage.Source = localBitmap;
                    ImageSource = localBitmap;
                    break;

                case Uri uri when uri.IsWebUri:
                    var uriWebBitmap = await ImageHelper.LoadWebImageAsync(uri.ToString());
                    if (generation != _loadGeneration) return;
                    InnerImage.Source = uriWebBitmap;
                    ImageSource = uriWebBitmap;
                    break;

                case Uri uri when uri.IsLocalUri:
                    var uriLocalBitmap = await ImageHelper.LoadLocalImage(uri.ToString());
                    if (generation != _loadGeneration) return;
                    InnerImage.Source = uriLocalBitmap;
                    ImageSource = uriLocalBitmap;
                    break;

                case ImageSource imageSource:
                    InnerImage.Source = imageSource;
                    ImageSource = imageSource;
                    break;
            }
        }
        catch
        {
            if (generation == _loadGeneration) InnerImage.Source = null;
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                LoadingIndicator.IsActive = false;
                InnerImage.Opacity = 1;
            }
        }
    }

    private void InnerImage_Unloaded(object sender, RoutedEventArgs e)
    {
        //此处释放资源会导致UBBTextBlock中Emoji标签无法正常显示。因此注释掉。
        //InnerImage.Source = null;
    }
}