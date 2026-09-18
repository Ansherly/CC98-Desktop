using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
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
    private readonly HttpClient httpClient = httpClientFactory.CreateClient("ForumClient");

    /// <summary>
    /// 封装通用GET请求，并实现自动错误处理。
    /// </summary>
    public async Task<ApiResponse<T>> Fetch<T>(string endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetAsync(endpoint, cancellationToken);
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
            var res = await httpClient.PutAsync(endpoint, content, cancellationToken);
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
            var res = await httpClient.DeleteAsync(endpoint, cancellationToken);
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
            var res = await httpClient.PostAsync(endpoint, content, cancellationToken);
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
            var res = await httpClient.GetAsync(endpoint,cancellationToken);
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
            using var res = await httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
    public async Task<Stream?> GetStreamAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return await res.Content.ReadAsStreamAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Kernel", $"其他错误{ex.Message}");
            return null;
        }
    }

    public static async Task CopyStreamToRandomAccessStream(Stream input, IRandomAccessStream output)
    {
        var buffer = new byte[16 * 1024];
        int bytesRead;

        // 获取输出流的写入器
        using var outputStream = output.GetOutputStreamAt(0);
        using var writer = new DataWriter(outputStream);
        while ((bytesRead = await input.ReadAsync(buffer)) > 0)
        {
            writer.WriteBytes(buffer.AsSpan(0, bytesRead).ToArray());
            await writer.StoreAsync();
            await outputStream.FlushAsync();
        }

        await writer.FlushAsync();
    }
}


//需要逐步迁移
public static class LinkAnalyzer
{
    public static KeyValuePair<string, string> Parse(string link)
    {
        if (!string.IsNullOrEmpty(link))
        {
            if (link.Contains("user/name"))
            {
                var pattern = @"https:\/\/api\.cc98\.org\/user\/name\/([^\/\s]+)";
                var matches = Regex.Matches(link, pattern);
                if (matches.Count == 1)
                {
                    var username = matches[0].Groups[1].Value;
                    return new("user", username);
                }

                return new("null", link);
            }

            if (link.Contains("/topic/") && !link.Contains("#"))
            {
                var pattern = @"\/topic\/([^\/\s]+)";
                var matches = Regex.Matches(link, pattern);
                if (matches.Count == 1)
                {
                    var pid = matches[0].Groups[1].Value;
                    return new("topic", pid);
                }

                return new("null", link);
            }

            if (link.Contains("#"))
            {
                var match = Regex.Match(link, @"/topic/(\d{7})/(\d+)#(\d+)");
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
                var match = Regex.Match(link, @"\/board\/(\d{2,3})");
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