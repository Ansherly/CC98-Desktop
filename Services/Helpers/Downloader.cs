using CC98.Kernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CC98.Services.Helpers;

public class Downloader
{
    /// <summary>
    /// 复制在线图片到剪贴板
    /// </summary>
    /// <param name="imageUrl">图片URL</param>
    /// <returns>是否成功</returns>
    public static async Task<bool> CopyImageToClipboardAsync(string imageUrl)
    {
        try
        {
            var apiService = App.Current.GetService<ApiService>();
            var imageBytes = await apiService.GetBytesAsync(imageUrl);
            if(imageBytes == null) return false;
            using var stream = new MemoryStream(imageBytes);
            var randomAccessStream = new InMemoryRandomAccessStream();
            await randomAccessStream.WriteAsync(imageBytes.AsBuffer());
            randomAccessStream.Seek(0);

            // 创建数据包
            var dataPackage = new DataPackage();
            dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(randomAccessStream));

            // 设置描述信息
            dataPackage.Properties.Title = "CC98图片";
            dataPackage.Properties.Description = "已复制的图片";

            // 复制到剪贴板
            Clipboard.SetContent(dataPackage);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"复制图片失败: {ex.Message}");
            return false;
        }
    }
    /// <summary>
    /// 下载文件到用户下载文件夹
    /// </summary>
    /// <param name="fileName">文件名（可选，不指定则从URL自动提取）</param>
    /// <returns>下载成功返回文件路径，失败返回null</returns>
    public static async Task<string?> DownloadFileAsync(string sourceUrl, string? folderPath=null,string? fileName = null)
    {
        try
        {
            // 获取用户的下载文件夹
            var downloadsFolder = folderPath != null ? await StorageFolder.GetFolderFromPathAsync(folderPath) : await GetDownloadsFolderAsync();
            if (downloadsFolder == null)
            {
                Debug.WriteLine("无法访问下载文件夹");
                return null;
            }

            // 如果没有指定文件名，从URL中提取
            if (string.IsNullOrEmpty(fileName)) fileName = ExtractFileNameFromUrl(sourceUrl);

            // 处理文件名冲突
            fileName = await GetUniqueFileNameAsync(downloadsFolder, fileName);

            // 创建文件
            var file = await downloadsFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            var apiService = App.Current.GetService<ApiService>();
            var imageBytes = await apiService.GetBytesAsync(sourceUrl);
            if (imageBytes == null) return null;
            await using var stream = await file.OpenStreamForWriteAsync();
            await stream.WriteAsync(imageBytes);
            return file.Path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"下载图片失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     下载文件到应用缓存目录(内部缓存用,例如用户头像)。
    ///     与 <see cref="DownloadFileAsync" /> 的三点关键差异:
    ///       1. 文件名由 URL 决定且固定,重复下载直接覆盖 —— 不会像下载功能那样每次生成 _1/_2 副本
    ///          (缓存目录里堆出多份头像的根因就在这里);
    ///       2. 先写 .tmp 再改名替换,中途失败既不留半个文件,也不会破坏已有的有效缓存;
    ///       3. 先取到内容再建文件,取不到直接返回 null,不会留下 0 字节却"看起来像缓存"的文件。
    /// </summary>
    /// <returns>成功返回缓存文件完整路径;失败返回 null,调用方应保留原有缓存。</returns>
    public static async Task<string?> DownloadCacheFileAsync(string sourceUrl, string folderPath, string? fileName = null)
    {
        StorageFile? tempFile = null;
        var moved = false;

        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
            var name = string.IsNullOrEmpty(fileName) ? ExtractFileNameFromUrl(sourceUrl) : fileName;
            var apiService = App.Current.GetService<ApiService>();
            var bytes = await apiService.GetBytesAsync(sourceUrl);
            if (bytes is null || bytes.Length == 0)
            {
                Debug.WriteLine("缓存下载失败: 未取到内容");
                return null;
            }

            tempFile = await folder.CreateFileAsync($"{name}.tmp", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteBytesAsync(tempFile, bytes);
            await tempFile.MoveAsync(folder, name, NameCollisionOption.ReplaceExisting);
            moved = true;

            return Path.Combine(folder.Path, name);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"缓存下载失败: {ex.Message}");
            return null;
        }
        finally
        {
            if (!moved && tempFile is not null)
            {
                try { await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete); }
                catch { /* 清理临时文件失败不影响结果 */ }
            }
        }
    }

    /// <summary>
    ///     获取用户的下载文件夹
    /// </summary>
    private static async Task<StorageFolder?> GetDownloadsFolderAsync()
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloadsPath = Path.Combine(userProfile, "Downloads");
            return await StorageFolder.GetFolderFromPathAsync(downloadsPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     从URL中提取文件名
    /// </summary>
    private static string ExtractFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);

            // 如果文件名无效，生成默认文件名
            if (string.IsNullOrEmpty(fileName) || !fileName.Contains('.'))
                fileName = $"CC98_{DateTime.Now:yyyyMMdd_HHmmss}";

            return fileName;
        }
        catch
        {
            return $"CC98_{DateTime.Now:yyyyMMdd_HHmmss}";
        }
    }

    /// <summary>
    /// 确保文件名唯一（如果文件已存在，添加数字后缀）
    /// </summary>
    private static async Task<string> GetUniqueFileNameAsync(StorageFolder folder, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;
        var uniqueFileName = fileName;

        while (await folder.TryGetItemAsync(uniqueFileName) != null)
        {
            uniqueFileName = $"{baseName}_{counter}{extension}";
            counter++;
        }

        return uniqueFileName;
    }
}
