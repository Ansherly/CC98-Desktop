using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CC98.Controls;

/// <summary>
/// 图片成功解码并渲染后的事件参数,携带图片像素尺寸(用于视口适配等)。
/// </summary>
public sealed class PictureImageOpenedEventArgs : EventArgs
{
    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public PictureImageOpenedEventArgs(int pixelWidth, int pixelHeight)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }
}

public sealed partial class Picture : UserControl
{
    public Picture()
    {
        InitializeComponent();
        // 图片解码完成时转发事件,让调用方(如预览器)能拿到像素尺寸做视口适配。
        // 注意:SvgImageSource 与 BitmapImage 虽同属 ImageSource,但没有公开"矢量自然尺寸"的属性
        // (RasterizePixelWidth/Height 为 0 时表示按自然尺寸光栅化,取不到具体值),
        // 因此这里只对位图报尺寸;SVG 的加载完成仍会正常收掉占位动画,只是不抛出带尺寸的事件。
        Viewer.ImageOpened += (_, _) =>
        {
            if (Viewer.Source is BitmapImage bmp)
                ImageOpened?.Invoke(this, new(bmp.PixelWidth, bmp.PixelHeight));
        };
    }

    /// <summary>
    /// 图片成功解码并渲染后触发,携带像素尺寸。SVG 等无固定像素尺寸的源不会触发。
    /// </summary>
    public event EventHandler<PictureImageOpenedEventArgs>? ImageOpened;

    #region 图片加载

    // 加载代次:Source 快速变化时,旧请求完成不得覆盖新请求的结果
    private int _loadGeneration;

    private async Task LoadImage()
    {
        var generation = ++_loadGeneration;
        shimmer.IsActive = true;
        try
        {
            var src = Src;
            var callback = LoadImageCallback;
            // 加载器返回 ImageSource(位图或 SVG 矢量图),直接交给 Image 控件渲染
            var source = await callback.LoadImage(src);
            // 仅当 Src 未再次变化时才应用结果,避免慢请求覆盖新图
            if (generation != _loadGeneration) return;
            Viewer.Source = source;
        }
        catch
        {
            if (generation == _loadGeneration) Viewer.Source = null;
        }
        finally
        {
            // 无论成功/失败都关闭 shimmer,避免破图位置永久显示加载动画
            if (generation == _loadGeneration)
            {
                shimmer.IsActive = false;
                shimmer.Visibility = Visibility.Collapsed;
            }
        }
    }

    #endregion

    #region 依赖属性

    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(
            nameof(Stretch),
            typeof(Stretch),
            typeof(Picture),
            new(Stretch.UniformToFill));

    public static readonly DependencyProperty SrcProperty =
        DependencyProperty.Register(
            "Src",
            typeof(string),
            typeof(Picture),
            new("", OnSrcChanged));

    public static readonly DependencyProperty HideProperty =
        DependencyProperty.Register(
            "Hide",
            typeof(bool),
            typeof(Picture),
            new(false));

    public static readonly DependencyProperty LoadImageCallbackProperty =
        DependencyProperty.Register("LoadImageCallback", typeof(IImageLoader),
            typeof(Picture), new(new DefaultImageLoader()));

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public string Src
    {
        get => (string)GetValue(SrcProperty);
        set => SetValue(SrcProperty, value);
    }

    public bool Hide
    {
        get => (bool)GetValue(HideProperty);
        set => SetValue(HideProperty, value);
    }

    public IImageLoader LoadImageCallback
    {
        get => (IImageLoader)GetValue(LoadImageCallbackProperty);
        set => SetValue(LoadImageCallbackProperty, value);
    }

    #endregion


    #region 事件处理

    private static async void OnSrcChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Picture picture)
        {
            if (picture.Hide)
            {
                picture.DisplayButton.Visibility = Visibility.Visible;
                picture.shimmer.Visibility = Visibility.Collapsed;
                picture.Viewer.Source = null;
            }
            else
            {
                await picture.LoadImage();
            }
        }
    }

    private async void DisplayButton_Click(object sender, RoutedEventArgs e)
    {
        DisplayButton.Visibility = Visibility.Collapsed;
        shimmer.Visibility = Visibility.Visible;
        await LoadImage();
    }

    #endregion
}