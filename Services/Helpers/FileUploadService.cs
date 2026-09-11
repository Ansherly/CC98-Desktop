using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using CC98.Kernel;

using Microsoft.UI.Xaml;

using Microsoft.Windows.Storage.Pickers;
using System.Linq;
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

    public static UploadResult Ok(string url, string fileName) => new() { Success = true, Url = url, FileName = fileName };

    public static UploadResult Fail(string error) => new() { Error = error };
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
            foreach (var filter in filters) picker.FileTypeFilter.Add(filter);            var file = await picker.PickSingleFileAsync();
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
            if (fileInfo.Length > MaxUploadSizeBytes) return TooLarge(fileInfo.Name);

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
            if (properties.Size > (ulong)MaxUploadSizeBytes) return TooLarge(file.Name);

            using var stream = await file.OpenStreamForReadAsync();
            return await UploadStreamAsync(stream, file.Name);
        }
        catch (Exception ex)
        {
            return UploadResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 上传剪贴板中的位图(从浏览器/截图工具复制而来的图片)。
    /// 先识别真实编码再按原格式落盘,避免无损转 PNG 导致体积暴涨而被大小限制误拦。
    /// </summary>
    /// <param name="bitmap">剪贴板位图引用。</param>
    /// <returns>上传结果。</returns>
    public static async Task<UploadResult> UploadClipboardBitmapAsync(RandomAccessStreamReference bitmap)
    {
        StorageFile? tempFile = null;
        try
        {
            using var source = await bitmap.OpenReadAsync();
            // 用解码器识别真实编码,据此决定落盘扩展名
            var decoder = await BitmapDecoder.CreateAsync(source);
            var isJpeg = decoder.DecoderInformation.CodecId == BitmapDecoder.JpegDecoderId;
            var extension = isJpeg ? ".jpg" : ".png";

            tempFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
                $"clipboard_{Guid.NewGuid():N}{extension}", CreationCollisionOption.ReplaceExisting);

            // 原样复制字节:重新编码会改变体积与画质。先关闭写入流再上传,确保数据已落盘。
            source.Seek(0);
            using (var netStream = source.AsStreamForRead())
            using (var destStream = await tempFile.OpenStreamForWriteAsync())
            {
                await netStream.CopyToAsync(destStream);
            }

            return await UploadAsync(tempFile.Path);
        }
        catch (Exception ex)
        {
            return UploadResult.Fail($"剪贴板图片处理失败:{ex.Message}");
        }
        finally
        {
            if (tempFile != null)
            {
                try { await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { }
            }
        }
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
        => UploadResult.Fail($"{fileName} 超过 {MaxUploadSizeBytes / 1024 / 1024}MB 上限");
}
