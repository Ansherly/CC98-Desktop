using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace CC98.Controls;

/// <summary>
///     把"地址 / 字节流"解码为 <see cref="ImageSource" /> 的工厂,统一处理 SVG 与位图两类格式。
/// </summary>
/// <remarks>
///     <para>
///         WinUI 里 <see cref="BitmapImage" /> <b>不能</b>解码 SVG(矢量图),必须用
///         <see cref="SvgImageSource" />;两者都派生自 <see cref="ImageSource" />,因此调用方只需面向
///         <see cref="ImageSource" /> 编程,即可同时兼容两类格式。
///     </para>
///     <para>
///         网络图片经常拿不到可靠的扩展名(带查询串、CDN 无后缀路径),所以字节流一律按
///         <b>内容嗅探</b>判断(<see cref="LooksLikeSvg(ReadOnlySpan{byte})" />),而不是只看扩展名;
///         只有本地文件 / <c>ms-appx</c> 这类后缀可靠的地址才用扩展名快速判断。
///     </para>
/// </remarks>
public static class ImageSourceFactory
{
    /// <summary>SVG 嗅探时最多向后扫描的字节数(XML 声明 / DOCTYPE / 注释都不会太长)。</summary>
    private const int SniffLength = 2048;

    /// <summary>读取流时使用的分块大小。</summary>
    private const int ReadChunkSize = 8192;

    /// <summary>低分辨率模式下 SVG 的光栅化边长(与位图的 <c>DecodePixel*</c> 语义对齐)。</summary>
    private const int LowResRasterizeSize = 64;

    /// <summary>
    ///     按内容判断字节流是否为 SVG。
    /// </summary>
    /// <remarks>
    ///     依次跳过 UTF-8 BOM、空白、XML 声明、注释与 DOCTYPE,再看是否以 <c>&lt;svg</c> 开头,
    ///     因此 <c>&lt;?xml …?&gt;</c> + 注释 + <c>&lt;svg …&gt;</c> 这类常见开头也能识别。
    /// </remarks>
    /// <param name="bytes">待检测的字节流。</param>
    /// <returns>是 SVG 时返回 <see langword="true" />。</returns>
    public static bool LooksLikeSvg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4) return false;

        // 跳过 UTF-8 BOM(UTF-16 编码的 SVG 极罕见,不为它增加分支)
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];

        if (bytes.Length < 4) return false;

        var head = bytes[..Math.Min(bytes.Length, SniffLength)];
        var text = Encoding.ASCII.GetString(head);

        var index = 0;
        while (index < text.Length)
        {
            // 1) 空白
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            if (index >= text.Length) break;

            // 2) XML 声明 <?xml … ?>
            if (text.AsSpan(index).StartsWith("<?", StringComparison.Ordinal))
            {
                var end = text.IndexOf("?>", index, StringComparison.Ordinal);
                if (end < 0) break;
                index = end + 2;
                continue;
            }

            // 3) 注释 <!-- … -->
            if (text.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = text.IndexOf("-->", index, StringComparison.Ordinal);
                if (end < 0) break;
                index = end + 3;
                continue;
            }

            // 4) 文档类型 <!DOCTYPE …>
            if (text.AsSpan(index).StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
            {
                var end = text.IndexOf('>', index);
                if (end < 0) break;
                index = end + 1;
                continue;
            }

            break;
        }

        // 根元素是 <svg>(也涵盖带命名空间前缀的 <svg:svg)
        return text.AsSpan(index).StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     按扩展名判断地址是否指向 SVG(适用于本地文件 / 资源地址这些后缀可靠的场景)。
    /// </summary>
    /// <param name="pathOrUrl">文件路径或地址。</param>
    /// <returns>扩展名为 <c>.svg</c>(忽略查询串、片段与大小写)时返回 <see langword="true" />。</returns>
    public static bool LooksLikeSvg(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return false;

        var path = pathOrUrl;

        // 去掉查询串与片段,避免 "icon.svg?v=2" 这类地址被误判
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];

        return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     由 URI 创建图片源:按扩展名判断 SVG,其余交给 <see cref="BitmapImage" />。
    /// </summary>
    /// <remarks>
    ///     这条路径<b>不</b>读取内容,适合 <c>ms-appx</c> / <c>ms-appdata</c> / 本地文件这类后缀可靠的地址。
    ///     网络地址若后缀不可靠(CDN 无后缀路径),请改用 <see cref="FromBytesAsync" />,它会按内容嗅探。
    /// </remarks>
    /// <param name="uri">图片地址。</param>
    /// <param name="lowRes">是否按低分辨率解码。</param>
    /// <returns>可直接赋给 <c>Image.Source</c> 的图片源。</returns>
    public static ImageSource FromUri(Uri uri, bool lowRes = false)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (LooksLikeSvg(uri.OriginalString))
        {
            var svg = new SvgImageSource(uri);
            if (lowRes) SetLowResRasterizeSize(svg);
            return svg;
        }

        var bitmap = new BitmapImage(uri);
        ApplyLowResDecodeSize(bitmap, lowRes);
        return bitmap;
    }

    /// <summary>
    ///     把字节流解码为图片源:按内容嗅探区分 SVG 与位图。
    /// </summary>
    /// <param name="bytes">图片字节。</param>
    /// <param name="lowRes">是否按低分辨率解码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解码后的图片源。</returns>
    public static Task<ImageSource> FromBytesAsync(byte[] bytes, bool lowRes = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (LooksLikeSvg(bytes)) return CreateSvgFromBytesAsync(bytes, lowRes, cancellationToken);
        return CreateBitmapFromBytesAsync(bytes, lowRes, cancellationToken);
    }

    /// <summary>
    ///     把随机访问流解码为图片源(SVG 需要先读出内容再解码,故内部会读完整流)。
    /// </summary>
    /// <param name="stream">图片流;调用方负责释放。</param>
    /// <param name="lowRes">是否按低分辨率解码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解码后的图片源。</returns>
    public static async Task<ImageSource> FromStreamAsync(IRandomAccessStream stream, bool lowRes = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var bytes = await ReadAllBytesAsync(stream, cancellationToken);
        return await FromBytesAsync(bytes, lowRes, cancellationToken);
    }

    /// <summary>
    ///     把随机访问流完整读成字节数组(从流当前位置开始)。
    /// </summary>
    /// <param name="stream">待读取的流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>读到的全部字节。</returns>
    public static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // 图片总是"从头读",这里回到起点;个别不支持定位的流按当前位置读
        try
        {
            stream.Seek(0);
        }
        catch
        {
            // 忽略:继续按当前位置读
        }

        using var memory = new MemoryStream();
        var buffer = new byte[ReadChunkSize];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsBuffer(), ReadChunkSize, InputStreamOptions.None)
                .AsTask(cancellationToken);
            if (read.Length == 0) break;

            memory.Write(buffer, 0, (int)read.Length);
        }

        return memory.ToArray();
    }

    private static async Task<ImageSource> CreateSvgFromBytesAsync(byte[] bytes, bool lowRes,
        CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer()).AsTask(cancellationToken);
        stream.Seek(0);

        var svg = new SvgImageSource();
        // 低分辨率时限制光栅化尺寸;高分辨率保持 0,由 SvgImageSource 按矢量自然尺寸渲染
        if (lowRes) SetLowResRasterizeSize(svg);

        await svg.SetSourceAsync(stream);
        return svg;
    }

    private static async Task<ImageSource> CreateBitmapFromBytesAsync(byte[] bytes, bool lowRes,
        CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer()).AsTask(cancellationToken);
        stream.Seek(0);

        var bitmap = new BitmapImage();
        ApplyLowResDecodeSize(bitmap, lowRes);

        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static void SetLowResRasterizeSize(SvgImageSource svg)
    {
        svg.RasterizePixelWidth = LowResRasterizeSize;
        svg.RasterizePixelHeight = LowResRasterizeSize;
    }

    private static void ApplyLowResDecodeSize(BitmapImage bitmap, bool lowRes)
    {
        if (!lowRes) return;

        bitmap.DecodePixelWidth = LowResRasterizeSize;
        bitmap.DecodePixelHeight = LowResRasterizeSize;
    }
}
