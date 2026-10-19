using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using CC98.Kernel;

using Microsoft.UI.Xaml;

using Microsoft.Windows.Storage.Pickers;
using System.Linq;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CC98.Services.Helpers;

/// <summary>
/// 文件上传结果(强类型,替代 magic string 判断)。
/// </summary>
public sealed class UploadResult
{
    /// <summary>是否上传成功。</summary>
    public bool Success { get; init; }

    /// <summary>上传成功后的文件 URL。</summary>
    public string Url { get; init; } = "";

    /// <summary>文件名。</summary>
    public string FileName { get; init; } = "";

    /// <summary>失败时的错误信息。</summary>
    public string Error { get; init; } = "";

    /// <summary>成功时的补充说明(例如"图片过大,已缩放后上传"),没话可说就是空串。</summary>
    public string Note { get; init; } = "";

    public static UploadResult Ok(string url, string fileName) => new() { Success = true, Url = url, FileName = fileName };

    public static UploadResult Fail(string error) => new() { Error = error };

    /// <summary>给结果附一句提示,保持原有内容不变。</summary>
    public UploadResult WithNote(string note)
        => new() { Success = Success, Url = Url, FileName = FileName, Error = Error, Note = note };
}

/// <summary>
/// 统一文件上传服务:媒体类型映射、文件选择、流式上传。
/// SketchPage 与 ChatPage 共用,避免各页面重复实现。
/// </summary>
public static class FileUploadService
{
    private static readonly ApiService ApiService = App.Current.GetService<ApiService>();

    /// <summary>单文件上传大小上限(5MB)。</summary>
    public const long MaxUploadSizeBytes = 5 * 1024 * 1024;

    /// <summary>上限的展示文案。和 <see cref="MaxUploadSizeBytes" /> 同源,避免两处各写一个数字。</summary>
    public static string MaxUploadSizeText => $"{MaxUploadSizeBytes / (1024 * 1024)}MB";

    /// <summary>脚本/探针用:判断给定字节数是否超限(与上传前用的是同一判据)。</summary>
    public static bool IsOverSizeLimit(long byteCount) => byteCount > MaxUploadSizeBytes;

    // 各媒体类型的扩展名。GetPickSpec(正向)与 GetLabelForFileName(反向)共用,保证映射不脱节。
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi", ".mov", ".wmv"];
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".flac", ".aac"];
    private static readonly string[] DocumentExtensions = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".zip", ".rar", ".7z"];

    /// <summary>
    /// 按媒体类型返回文件选择器配置(过滤器 + 起始库)。
    /// </summary>
    /// <param name="label">媒体类型:img / video / audio / upload。</param>
    public static (List<string> Filters, PickerLocationId Location) GetPickSpec(string label)
    {
        List<string> filters = label switch
        {
            "img" => [.. ImageExtensions],
            "video" => [.. VideoExtensions],
            "audio" => [.. AudioExtensions],
            "upload" => [.. DocumentExtensions],
            _ => []
        };
        PickerLocationId location = label switch
        {
            "img" => PickerLocationId.PicturesLibrary,
            "video" => PickerLocationId.VideosLibrary,
            "audio" => PickerLocationId.MusicLibrary,
            "upload" => PickerLocationId.DocumentsLibrary,
            _ => PickerLocationId.ComputerFolder
        };
        return (filters, location);
    }

    /// <summary>
    /// 按文件扩展名推断媒体标签,用于粘贴上传时自动选择标签类型。
    /// </summary>
    /// <param name="fileName">文件名(含扩展名)。</param>
    /// <returns>img / video / audio / upload 之一;未知类型按文档处理。</returns>
    public static string GetLabelForFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (ImageExtensions.Contains(extension)) return "img";
        if (VideoExtensions.Contains(extension)) return "video";
        if (AudioExtensions.Contains(extension)) return "audio";
        return "upload";
    }

    /// <summary>
    /// 打开文件选择器,选择文件并上传。
    /// </summary>
    /// <param name="xamlRoot">当前窗口的 XamlRoot,用于初始化文件选择器。</param>
    /// <param name="label">媒体类型:img / video / audio / upload。</param>
    /// <returns>上传结果。用户取消时返回 Success=false 且 Error 为空。</returns>
    public static async Task<UploadResult> PickAndUploadAsync(XamlRoot xamlRoot, string label)
    {
        var (filters, location) = GetPickSpec(label);
        try
        {
            var picker = new FileOpenPicker(xamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                CommitButtonText = "上传",
                SuggestedStartLocation = location
            };
            foreach (var filter in filters) picker.FileTypeFilter.Add(filter);
            var file = await picker.PickSingleFileAsync();
            if (file == null) return new UploadResult(); // 用户取消,非错误

            return await UploadAsync(file.Path);
        }
        catch (Exception ex)
        {
            return UploadResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 上传本地文件。超过 <see cref="MaxUploadSizeBytes"/> 时直接拒绝,不发起请求。
    /// </summary>
    /// <param name="filePath">本地文件完整路径。</param>
    /// <returns>上传结果。</returns>
    public static async Task<UploadResult> UploadAsync(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) return UploadResult.Fail("文件不存在");
            if (IsOverSizeLimit(fileInfo.Length)) return TooLarge(fileInfo.Name);

            using var fileStream = File.OpenRead(filePath);
            return await UploadStreamAsync(fileStream, fileInfo.Name);
        }
        catch (Exception ex)
        {
            return UploadResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 上传 <see cref="StorageFile"/>。粘贴/选择器场景更稳妥:云盘、压缩包等虚拟文件可能没有可用的本地路径。
    /// </summary>
    /// <param name="file">待上传文件。</param>
    /// <returns>上传结果。</returns>
    public static async Task<UploadResult> UploadAsync(StorageFile file)
    {
        try
        {
            var properties = await file.GetBasicPropertiesAsync();
            if (IsOverSizeLimit((long)properties.Size)) return TooLarge(file.Name);

            using var stream = await file.OpenStreamForReadAsync();
            return await UploadStreamAsync(stream, file.Name);
        }
        catch (Exception ex)
        {
            return UploadResult.Fail(ex.Message);
        }
    }

    /// <summary>
    ///     上传剪贴板中的位图(从浏览器/截图工具复制而来的图片)。
    /// </summary>
    /// <remarks>
    ///     剪贴板常常只给 DIB(原始像素),拿不到用户复制的那张 JPEG 的原字节 —— Windows 会把它编码成
    ///     PNG 交出来,而同一张照片的 PNG 往往比 JPEG 大好几倍,于是"明明是张普通照片却超限"。
    ///     这里的策略是「先原样、再降级」:
    ///     <list type="number">
    ///         <item>原始字节没超限就直接传,零损失;</item>
    ///         <item>超限则重新编码,只靠<b>缩放</b>逐级减体积:PNG(有透明通道时)/ JPEG @100% → 70% → 50% → 35%;</item>
    ///         <item>全都超限才报错,并提示改用「上传本地文件」或先裁剪。</item>
    ///     </list>
    ///     为什么不用 <c>ImageQuality</c> 降质量:实测 <see cref="BitmapEncoder" /> 对
    ///     <c>BitmapPropertySet["ImageQuality"]</c> 完全无视 —— 1.0 / 0.6 / 0.2 编出来的字节约等于一模一样
    ///     (不管设在 SetPixelData 之前还是之后)。所以只能靠 <see cref="BitmapTransform" /> 缩放,这个确实有效。
    /// </remarks>
    public static async Task<UploadResult> UploadClipboardBitmapAsync(RandomAccessStreamReference bitmap)
    {
        try
        {
            using var source = await bitmap.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(source);

            var isPng = decoder.DecoderInformation.CodecId == BitmapDecoder.PngDecoderId;
            var isJpeg = decoder.DecoderInformation.CodecId == BitmapDecoder.JpegDecoderId;
            var hasAlpha = decoder.BitmapAlphaMode != BitmapAlphaMode.Ignore;

            // ① 原字节能过就原样上传,一个像素都不动
            var original = await ReadAllBytesAsync(source);
            if (!IsOverSizeLimit(original.LongLength))
                return await UploadStreamAsync(new MemoryStream(original),
                    $"clipboard_{Guid.NewGuid():N}{(isJpeg ? ".jpg" : ".png")}");

            // ② 超限:按代价从小到大重新编码,第一个装得下的就用
            foreach (var step in BuildClipboardEncodeSteps(decoder, isPng, hasAlpha))
            {
                var bytes = await EncodeAsync(decoder, step);
                if (IsOverSizeLimit(bytes.LongLength)) continue;

                var uploaded = await UploadStreamAsync(new MemoryStream(bytes),
                    $"clipboard_{Guid.NewGuid():N}{step.Extension}");

                // 缩过就说一声,免得用户纳闷图为什么变糊了
                return step.Scale < 1.0
                    ? uploaded.WithNote($"图片过大,已缩到 {(int)Math.Round(step.Scale * 100)}% 上传")
                    : uploaded;
            }

            return UploadResult.Fail($"图片压到 {MaxUploadSizeText} 以内失败,请改用「上传本地文件」或先裁剪");
        }
        catch (Exception ex)
        {
            return UploadResult.Fail($"剪贴板图片处理失败:{ex.Message}");
        }
    }

    /// <summary>一次重新编码的尝试:目标扩展名 + 缩放比例。</summary>
    private readonly record struct EncodeStep(string Extension, double Scale);

    /// <summary>超过这个像素数就先缩一档再编,省得对着几千万像素白跑一轮。</summary>
    private const double HugeImagePixelThreshold = 24_000_000;

    private static IEnumerable<EncodeStep> BuildClipboardEncodeSteps(BitmapDecoder decoder, bool sourceIsPng,
        bool hasAlpha)
    {
        var huge = decoder.PixelWidth * decoder.PixelHeight > HugeImagePixelThreshold;
        var full = huge ? 0.7 : 1.0;

        // 有透明通道:只能 PNG,JPEG 没有 alpha 会把透明区压成黑块
        if (hasAlpha)
        {
            if (!sourceIsPng) yield return new EncodeStep(".png", full);
            yield return new EncodeStep(".png", 0.7);
            yield return new EncodeStep(".png", 0.5);
        }

        // 其余情况(绝大多数照片/截图):JPEG 体积最小,靠缩放逐级减
        yield return new EncodeStep(".jpg", full);
        yield return new EncodeStep(".jpg", 0.7);
        yield return new EncodeStep(".jpg", 0.5);
        yield return new EncodeStep(".jpg", 0.35);
    }

    /// <summary>按给定缩放比例重新编码一张图,返回编码后的字节。</summary>
    private static async Task<byte[]> EncodeAsync(BitmapDecoder decoder, EncodeStep step)
    {
        var width = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * step.Scale));
        var height = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * step.Scale));

        // PNG 走预乘 alpha 保住透明;JPEG 没有 alpha,只能丢弃(Ignore)
        var alphaMode = step.Extension == ".png" ? BitmapAlphaMode.Premultiplied : BitmapAlphaMode.Ignore;

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            alphaMode,
            new BitmapTransform
            {
                ScaledWidth = width,
                ScaledHeight = height,
                InterpolationMode = BitmapInterpolationMode.Linear
            },
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(
            step.Extension == ".png" ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId, output);

        encoder.SetPixelData(BitmapPixelFormat.Bgra8, alphaMode, width, height, 96, 96,
            pixelData.DetachPixelData());

        await encoder.FlushAsync();
        return await ReadAllBytesAsync(output);
    }

    /// <summary>
    ///     把随机访问流整个读成字节数组。
    /// </summary>
    /// <remarks>
    ///     刻意不用 <c>AsStreamForRead()</c>:那个适配流一 Dispose 会把底层流一起关掉,
    ///     而这里读完还要拿同一个流给 <see cref="BitmapDecoder" /> 取像素。
    ///     改用 DataReader 读完再 <c>DetachStream()</c>,底层流保持打开。
    /// </remarks>
    private static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStream stream)
    {
        var size = (uint)stream.Size;
        if (size == 0) return [];

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var loaded = 0u;
        while (loaded < size)
        {
            var count = await reader.LoadAsync(size - loaded);
            if (count == 0) break;
            loaded += count;
        }

        var bytes = new byte[loaded];
        reader.ReadBytes(bytes);
        reader.DetachStream();
        return bytes;
    }

    /// <summary>
    /// 流式上传到 CC98。所有上传入口共用,保证行为一致。
    /// </summary>
    private static async Task<UploadResult> UploadStreamAsync(Stream stream, string fileName)
    {
        var url = ApiEndpoints.Forum.UploadFile;
        using var formData = new MultipartFormDataContent();
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new("multipart/form-data");
        formData.Add(fileContent, "files", fileName);

        var res = await ApiService.Submit<List<string>>(url, formData);
        if (!res.IsSuccess || res.Data == null || res.Data.Count == 0)
            return UploadResult.Fail(res.Message);

        return UploadResult.Ok(res.Data[0], fileName);
    }

    private static UploadResult TooLarge(string fileName)
        => UploadResult.Fail($"{fileName} 超过 {MaxUploadSizeText}({MaxUploadSizeBytes:N0} 字节)上限");
}
