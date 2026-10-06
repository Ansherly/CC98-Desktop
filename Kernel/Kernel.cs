using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using CC98.Kernel.Authorize;
using CC98.Objects;
using CC98.Services.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using Windows.Media.Core;
using System.Diagnostics;

namespace CC98.Kernel;

/// <summary>
///     对Http请求做二次封装，提供给UI层。
/// </summary>
public class ApiService(IHttpClientFactory httpClientFactory)
{
    // 按需创建 HttpClient:单例长期持有 factory 创建的实例会让 handler 轮换机制失效,
    // handler 链(含 CookieContainer 等)被钉在内存中永不释放
    private HttpClient CreateClient() => httpClientFactory.CreateClient("ForumClient");

    /// <summary>
    /// 封装通用GET请求，并实现自动错误处理。
    /// </summary>
    public async Task<ApiResponse<T>> Fetch<T>(string endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            using var res = await CreateClient().GetAsync(endpoint, cancellationToken);
            return await Deserialize<T>(res, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return ApiResponse<T>.Fail($"网络错误: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ApiResponse<T>.Fail("请求超时");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Kernel", $"其他错误{ex.Message}");
            return ApiResponse<T>.Fail($"系统错误: {ex.Message}");
        }
    }

    /// <summary>
    ///     适用于PUT。
    /// </summary>
    public  async Task<ApiResponse> Put(string endpoint, HttpContent? content,CancellationToken cancellationToken = default)
    {
        try
        {
            using var res = await CreateClient().PutAsync(endpoint, content, cancellationToken);
            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            return res.IsSuccessStatusCode
                ? ApiResponse.Success(json)
                : ApiResponse.Fail((res.ReasonPhrase ?? "响应失败") + ":" + json, (int)res.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return ApiResponse.Fail($"网络错误: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ApiResponse.Fail("请求超时");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"系统错误: {ex.Message}");
        }
    }

    /// <summary>
    ///     适用于DELETE。
    /// </summary>
    public async Task<ApiResponse> Delete(string endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            using var res = await CreateClient().DeleteAsync(endpoint, cancellationToken);
            var json = await res.Content.ReadAsStringAsync(cancellationToken);
            return res.IsSuccessStatusCode
                ? ApiResponse.Success(json)
                : ApiResponse.Fail((res.ReasonPhrase ?? "响应失败") + ":" + json, (int)res.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return ApiResponse.Fail($"网络错误: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ApiResponse.Fail("请求超时");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Kernel", $"其他错误{ex.Message}");
            return ApiResponse.Fail($"系统错误: {ex.Message}");
        }
    }

    /// <summary>
    ///     T是返回类型，不是提交数据类型。提交数据类型由HttpContent的具体实现决定
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="endpoint"></param>
    /// <param name="content"></param>
    /// <returns></returns>
    public  async Task<ApiResponse<T>> Submit<T>(string endpoint, HttpContent content, CancellationToken cancellationToken = default)
    {
        try
        {
            using var res = await CreateClient().PostAsync(endpoint, content, cancellationToken);
            return await Deserialize<T>(res, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return ApiResponse<T>.Fail($"网络错误: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ApiResponse<T>.Fail("请求超时");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Kernel", $"其他错误{ex.Message}");
            return ApiResponse<T>.Fail($"系统错误: {ex.Message}");
        }
    }

    public static async Task<ApiResponse<T>> Deserialize<T>(HttpResponseMessage res,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!res.IsSuccessStatusCode)
            {
                var errorContent = await res.Content.ReadAsStringAsync(cancellationToken);
                return ApiResponse<T>.Fail(errorContent??"响应失败", (int)res.StatusCode);
            }
            var contentLength = res.Content.Headers.ContentLength;
            if (contentLength == 0 || contentLength == null)
            {
                // 内容为空,此时ReadFromJsonAsync会报错：The input does not contain any JSON tokens
                //  使用额外的装箱来传入string.Empty,因为string是引用类型，default(string)是null,而不是string.Empty
                return typeof(T) == typeof(string)
                    ? ApiResponse<T>.Success((T)(object)string.Empty,"正文为空")
                    : ApiResponse<T>.Success(default!,"正文为空");
            }
            var obj = await res.Content.ReadFromJsonAsync(typeof(T), CC98JsonContext.Default, cancellationToken);
            return ApiResponse<T>.Success((T?)obj!);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine("Kernel", $"数据解析错误: {ex.Message}");
            return ApiResponse<T>.Fail(ex.Message, (int)res.StatusCode);
        }
    }

    public async Task<byte[]?> GetBytesAsync(string endpoint,CancellationToken cancellationToken=default)
    {
        try
        {
            using var res = await CreateClient().GetAsync(endpoint,cancellationToken);
            // 非 2xx 时响应体是错误页 / 错误 JSON,绝不是文件内容。
            // 之前把响应体直接返回,401/404 的错误体就会被当作图片写进缓存并长期生效。
            if (!res.IsSuccessStatusCode)
            {
                Debug.WriteLine("Kernel", $"获取字节失败: {(int)res.StatusCode} {endpoint}");
                return null;
            }
            return await res.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Kernel", $"其他错误{ex.Message}");
            return null;
        }
    }
    public async Task<MediaSource?> GetSourceAsync(string endpoint,CancellationToken cancellationToken=default)
    {
        try
        {
            using var res = await CreateClient().GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var memoryStream = new InMemoryRandomAccessStream();
            using var contentStream = await res.Content.ReadAsStreamAsync(cancellationToken);
            await CopyStreamToRandomAccessStream(contentStream, memoryStream);
            var source = MediaSource.CreateFromStream(memoryStream, res.Content.Headers.ContentType?.MediaType);
            return source;
        }
        catch(Exception ex)
        {
            Debug.WriteLine("Kernel", $"其他错误{ex.Message}");
            return null;
        }
    }
    /// <summary>
    ///     将远程内容流式复制到目标流。大文件下载应使用本方法而不是
    ///     <see cref="GetBytesAsync" />,避免整个文件驻留内存。
    /// </summary>
    public async Task<bool> CopyToStreamAsync(string endpoint, Stream destination, CancellationToken cancellationToken = default)
    {
        try
        {
            using var res = await CreateClient().GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                Debug.WriteLine("Kernel", $"流式下载失败: {(int)res.StatusCode} {endpoint}");
                return false;
            }
            await using var contentStream = await res.Content.ReadAsStreamAsync(cancellationToken);
            await contentStream.CopyToAsync(destination, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Kernel", $"其他错误{ex.Message}");
            return false;
        }
    }

    public static async Task CopyStreamToRandomAccessStream(Stream input, IRandomAccessStream output)
    {
        // 直接用 IOutputStream.WriteAsync(IBuffer):DataWriter 方案每 16KB 块都要 ToArray 一次,
        // 大文件时产生海量小分配;AsBuffer 是对数组的零拷贝包装。
        // 流的 Flush 在拷贝完成后统一执行一次。
        using var outputStream = output.GetOutputStreamAt(0);
        var buffer = new byte[64 * 1024];
        int bytesRead;
        while ((bytesRead = await input.ReadAsync(buffer)) > 0)
        {
            await outputStream.WriteAsync(buffer.AsBuffer(0, bytesRead));
        }
        await outputStream.FlushAsync();
    }
}


//需要逐步迁移
public static class LinkAnalyzer
{
    // 编译期单例正则:每次调用 new Regex 会重复编译,频繁点击链接时产生正则编译垃圾
    private static readonly Regex UserNamePattern = new(@"https:\/\/api\.cc98\.org\/user\/name\/([^\/\s]+)", RegexOptions.Compiled);
    private static readonly Regex TopicPattern = new(@"\/topic\/([^\/\s]+)", RegexOptions.Compiled);
    private static readonly Regex AnchorPattern = new(@"/topic/(\d{7})/(\d+)#(\d+)", RegexOptions.Compiled);
    private static readonly Regex BoardPattern = new(@"\/board\/(\d{2,3})", RegexOptions.Compiled);

    public static KeyValuePair<string, string> Parse(string link)
    {
        if (!string.IsNullOrEmpty(link))
        {
            if (link.Contains("user/name"))
            {
                var matches = UserNamePattern.Matches(link);
                if (matches.Count == 1)
                {
                    var username = matches[0].Groups[1].Value;
                    return new("user", username);
                }

                return new("null", link);
            }

            if (link.Contains("/topic/") && !link.Contains("#"))
            {
                var matches = TopicPattern.Matches(link);
                if (matches.Count == 1)
                {
                    var pid = matches[0].Groups[1].Value;
                    return new("topic", pid);
                }

                return new("null", link);
            }

            if (link.Contains("#"))
            {
                var match = AnchorPattern.Match(link);
                if (match.Success)
                {
                    var numberAfterHash = match.Groups[1].Value;
                    return new("anchor", link); //返回索引楼层
                }

                return new("null", link);
            }

            if (link.Contains("https://www.bilibili.com/video")) return new("backlink", "bili");

            if (link.Contains("board"))
            {
                var match = BoardPattern.Match(link);
                if (match.Success)
                {
                    var boardId = match.Groups[1].Value;
                    return new("board", boardId);
                }

                return new("null", link);
            }

            return new("null", link);
        }

        return new("null", link);
    }
}