using CC98.Controls;
using CC98.Kernel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CC98.Services.Helpers;

/// <summary>
///     图片解码的统一入口。
/// </summary>
/// <remarks>
///     返回类型是 <see cref="ImageSource" />,可同时承载位图(<see cref="BitmapImage" />)与矢量图
///     (<see cref="SvgImageSource" />),具体类型由 <see cref="ImageSourceFactory" /> 决定:
///     <list type="bullet">
///         <item>字节流(网络图片、未知后缀)→ <b>按内容嗅探</b>,SVG 走 <see cref="SvgImageSource" />;</item>
///         <item>本地文件 / ms-appx 资源 → 按扩展名快速判断,非 SVG 仍走"流直接解码"的零拷贝路径。</item>
///     </list>
/// </remarks>
public class ImageHelper
{
    /// <summary>
    /// 加载基于 Web 地址的图像。
    /// </summary>
    /// <param name="url">图像资源的 URL。</param>
    /// <param name="lowRes">是否使用低分辨率模式加载。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>表示异步操作的任务。操作结果为加载后的图片对象;取不到内容时为 <see langword="null" />。</returns>
    public static async Task<ImageSource?> LoadWebImageAsync(string url, bool lowRes = false,CancellationToken cancellationToken = default)
    {
        var apiService = App.Current.GetService<ApiService>();
        var imageBytes = await apiService.GetBytesAsync(url, cancellationToken);

        // 取不到内容(网络异常 / 非 2xx)时返回 null,由调用方跳过设置图片,
        // 而不是拿着 null 去解码(那样只会抛 NullReferenceException)
        if (imageBytes is null || imageBytes.Length == 0) return null;

        // 网络图片的后缀经常不可靠(带查询串 / CDN 无后缀),这里一律按内容嗅探
        return await ImageSourceFactory.FromBytesAsync(imageBytes, lowRes, cancellationToken);
    }

    /// <summary>
    ///     加载本地图片(物理路径 / <c>ms-appx</c> / <c>ms-appdata</c> 资源)。
    /// </summary>
    /// <param name="path">图片路径或资源地址。</param>
    /// <param name="lowRes">是否使用低分辨率模式加载。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>加载后的图片对象。</returns>
    public static async Task<ImageSource> LoadLocalImage(string path, bool lowRes = false, CancellationToken cancellationToken = default)
    {
        if (path.StartsWith("ms-appx:///") || path.StartsWith("ms-appdata:///"))
        {
            return ImageSourceFactory.FromUri(new(await UrlEx.LocateEmojiAsync(path, cancellationToken)), lowRes);
        }

        if (File.Exists(path))
        {
            // 本地文件路径
            var file = await StorageFile.GetFileFromPathAsync(path);
            using IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read);

            // SVG 需要先读出内容(并做嗅探)才能解码;其余格式沿用"流直接解码"的零拷贝路径。
            // 本地文件的后缀是可靠的,所以这里按扩展名判断即可。
            if (ImageSourceFactory.LooksLikeSvg(path))
                return await ImageSourceFactory.FromStreamAsync(fileStream, lowRes, cancellationToken);

            BitmapImage bitmapImage = new();
            if (lowRes)
            {
                bitmapImage.DecodePixelHeight = 64;
                bitmapImage.DecodePixelWidth = 64;
            }
            await bitmapImage.SetSourceAsync(fileStream);
            return bitmapImage;
        }

        // 尝试作为资源加载
        var uri = new Uri($"ms-appx:///Assets/{path}");
        return ImageSourceFactory.FromUri(uri, lowRes);
    }
  

    /// <summary>
    ///     把图片字节解码为图片源(按内容嗅探 SVG 与位图)。
    /// </summary>
    /// <param name="bytes">图片字节。</param>
    /// <param name="lowRes">是否使用低分辨率模式加载。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>加载后的图片对象。</returns>
    public static async Task<ImageSource> LoadFromBytesAsync(byte[] bytes, bool lowRes = false,CancellationToken cancellationToken = default)
    {
        return await ImageSourceFactory.FromBytesAsync(bytes, lowRes, cancellationToken);
    }
}
