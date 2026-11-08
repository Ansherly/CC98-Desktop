using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CC98.Kernel.Bilibili;

/// <summary>
///     B 站开放接口的匿名访问封装(用于 UBB <c>[bili]</c> 内嵌播放)。
/// </summary>
/// <remarks>
///     <para>
///         这里刻意只走"未登录也能拿到"的接口与参数,不接触任何账号态:
///         <list type="bullet">
///             <item><c>web-interface/view</c> 拿标题 / 封面 / 时长 / 分 P(匿名可用);</item>
///             <item>
///                 <c>player/playurl</c> 走 <c>platform=html5&amp;high_quality=1&amp;fnval=1</c> 的单文件 mp4 通道 ——
///                 常规 DASH 通道未登录只给 480P 且音视频分离,<c>html5</c> 通道是匿名状态能拿到的最高画质(720P),
///                 并且是一条可直接交给播放器的完整文件;
///             </item>
///             <item><c>frontend/finger/spi</c> 拿 buvid3/buvid4 指纹,压低触发风控(-352)的概率。</item>
///         </list>
///     </para>
///     <para>
///         请求走独立的 <c>BiliClient</c>:既不携带 CC98 的令牌,VPN 开启时也不会把 B 站域名改写进 webvpn。
///     </para>
/// </remarks>
/// <param name="httpClientFactory">HttpClient 工厂。</param>
public sealed class BilibiliApi(IHttpClientFactory httpClientFactory)
{
    private const string ClientName = "BiliClient";
    private const string ViewApi = "https://api.bilibili.com/x/web-interface/view";
    private const string PlayUrlApi = "https://api.bilibili.com/x/player/playurl";
    private const string FingerApi = "https://api.bilibili.com/x/frontend/finger/spi";

    /// <summary>封面缩略图后缀:原图多为 1080P(解码后很占内存),卡片只需要 480px 宽。</summary>
    /// <remarks>只限宽、不裁切:卡片要按封面原始比例算高度,裁成 16:9 会把比例信息丢掉。</remarks>
    private const string CoverThumbSuffix = "@480w.jpg";

    /// <summary>单次接口请求的超时(客户端本身不设超时,媒体下载要用同一套头但不能被 15 秒掐断)。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>视频信息的进程内缓存时长:同一条视频常被多个楼层引用,不该每次都打接口。</summary>
    private static readonly TimeSpan InfoCacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>信息缓存条目上限,超过后先清理已过期的条目。</summary>
    private const int MaxInfoCacheEntries = 256;

    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpireAt, BilibiliVideoInfo Info)> InfoCache = new();

    private readonly SemaphoreSlim _cookieLock = new(1, 1);

    // 匿名指纹只取一次;取不到就退回"不带 Cookie",接口本身也允许
    private string? _cookieHeader;

    private HttpClient CreateClient() => httpClientFactory.CreateClient(ClientName);

    /// <summary>
    ///     取视频基础信息(标题 / 封面 / UP 主 / 时长 / 分 P)。
    /// </summary>
    /// <param name="rawId">BV 号、av 号或视频页地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析失败或接口返回非 0 时为 <see langword="null" />。</returns>
    public async Task<BilibiliVideoInfo?> GetVideoAsync(string? rawId, CancellationToken cancellationToken = default)
    {
        if (!BilibiliVideoId.TryParse(rawId, out var id, out _)) return null;

        if (InfoCache.TryGetValue(id, out var cached) && cached.ExpireAt > DateTimeOffset.Now)
            return cached.Info;

        var query = BilibiliVideoId.IsBv(id)
            ? $"{ViewApi}?bvid={id}"
            : $"{ViewApi}?aid={id[2..]}";

        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        var root = await SendForJsonAsync(request, cancellationToken).ConfigureAwait(false);
        if (root is null) return null;

        try
        {
            if (root.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.Number
                && code.GetInt32() != 0)
            {
                Debug.WriteLine("Bilibili", $"view 接口返回错误码 {code.GetInt32()}({id})");
                return null;
            }

            if (!root.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
                return null;

            var parts = new List<BilibiliVideoPart>();
            if (data.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
                foreach (var page in pages.EnumerateArray())
                    parts.Add(new(
                        (int)GetInt64(page, "page", 1),
                        GetInt64(page, "cid"),
                        GetString(page, "part") ?? string.Empty,
                        GetInt64(page, "duration")));

            var info = new BilibiliVideoInfo(
                GetString(data, "bvid") ?? id,
                GetInt64(data, "aid"),
                GetString(data, "title") ?? "(无标题)",
                GetString(GetObject(data, "owner"), "name") ?? "未知 UP 主",
                NormalizeCoverUrl(GetString(data, "pic")),
                GetInt64(data, "duration"),
                parts);

            // 长时间挂着的客户端里,过期条目不该无限堆积
            if (InfoCache.Count > MaxInfoCacheEntries) PurgeExpiredInfoCache();

            InfoCache[id] = (DateTimeOffset.Now + InfoCacheTtl, info);
            return info;
        }
        finally
        {
            root.Dispose();
        }
    }

    /// <summary>
    ///     取匿名直链(单文件 mp4)。
    /// </summary>
    /// <param name="bvid">BV 号。</param>
    /// <param name="cid">分 P 的 cid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>取不到直链时为 <see langword="null" />(调用方应给"在 B 站打开"的出口)。</returns>
    public async Task<BilibiliPlayUrl?> GetPlayUrlAsync(string bvid, long cid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(bvid) || cid <= 0) return null;

        // fnval=1 表示走 durl(单文件)而不是 DASH;platform=html5 + high_quality=1 才会下发 720P
        var query = $"{PlayUrlApi}?bvid={bvid}&cid={cid}&qn=64&fnval=1&platform=html5&high_quality=1";

        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        var root = await SendForJsonAsync(request, cancellationToken).ConfigureAwait(false);
        if (root is null) return null;

        try
        {
            if (root.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.Number
                && code.GetInt32() != 0)
            {
                Debug.WriteLine("Bilibili", $"playurl 接口返回错误码 {code.GetInt32()}({bvid})");
                return null;
            }

            if (!root.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
                return null;

            if (!data.TryGetProperty("durl", out var durl)
                || durl.ValueKind != JsonValueKind.Array
                || durl.GetArrayLength() == 0)
                return null;

            var first = durl[0];
            var url = GetString(first, "url");
            if (string.IsNullOrEmpty(url)) return null;

            return new(
                url,
                (int)GetInt64(data, "quality"),
                GetString(data, "format") ?? "mp4",
                GetInt64(first, "size"),
                GetInt64(first, "length") > 0 ? GetInt64(first, "length") : GetInt64(data, "timelength"));
        }
        finally
        {
            root.Dispose();
        }
    }

    /// <summary>
    ///     下载一份图片字节(用于封面)。失败返回 <see langword="null" />。
    /// </summary>
    /// <param name="url">图片地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<byte[]?> GetImageBytesAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var timeout = CreateTimeout(cancellationToken);

        try
        {
            using var response = await CreateClient()
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine("Bilibili", $"封面请求失败 {(int)response.StatusCode}:{url}");
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Debug.WriteLine("Bilibili", $"封面请求异常:{ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     把封面地址拼上缩略图后缀(拿不到缩略图时调用方应退回原图)。
    /// </summary>
    /// <param name="coverUrl">原始封面地址。</param>
    public static string AppendCoverThumb(string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl)) return string.Empty;

        // hdslb 的图片样式使用 '@' 后缀;已经带过样式(或不是 hdslb 域名)就不要再拼
        return coverUrl.Contains('@') ? coverUrl : coverUrl + CoverThumbSuffix;
    }

    /// <summary>
    ///     封面地址归一化:接口常返回 <c>http</c>,统一升级为 <c>https</c>。
    /// </summary>
    private static string? NormalizeCoverUrl(string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl)) return null;

        return coverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + coverUrl[7..]
            : coverUrl;
    }

    /// <summary>
    ///     发送请求并解析为 JSON 文档;网络错误 / 非 2xx / 非法 JSON 一律返回 <see langword="null" />。
    /// </summary>
    /// <remarks>返回的 <see cref="JsonDocument" /> 由调用方释放。</remarks>
    private async Task<JsonDocument?> SendForJsonAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken);

        try
        {
            await AttachCookieAsync(request, timeout.Token).ConfigureAwait(false);

            using var response = await CreateClient()
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine("Bilibili", $"请求失败 {(int)response.StatusCode}:{request.RequestUri}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or IOException)
        {
            Debug.WriteLine("Bilibili", $"请求异常:{ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     首次请求时取一次匿名指纹(buvid3/buvid4)并挂到 Cookie 头上。
    /// </summary>
    /// <remarks>取不到指纹不算错误:B 站这套接口在没有 Cookie 时同样会返回数据。</remarks>
    private async Task AttachCookieAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_cookieHeader is null)
        {
            await _cookieLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cookieHeader is null) _cookieHeader = await FetchCookieAsync(cancellationToken)
                    .ConfigureAwait(false) ?? string.Empty;
            }
            finally
            {
                _cookieLock.Release();
            }
        }

        if (!string.IsNullOrEmpty(_cookieHeader))
            request.Headers.TryAddWithoutValidation("Cookie", _cookieHeader);
    }

    /// <summary>
    ///     取匿名指纹;失败返回 <see langword="null" />(由调用方记成空串,避免反复重试)。
    /// </summary>
    private async Task<string?> FetchCookieAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, FingerApi);
        using var timeout = CreateTimeout(cancellationToken);

        try
        {
            using var response = await CreateClient()
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            using var root = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            var data = GetObject(root.RootElement, "data");
            var buvid3 = GetString(data, "b_3");
            var buvid4 = GetString(data, "b_4");
            if (string.IsNullOrEmpty(buvid3)) return null;

            return string.IsNullOrEmpty(buvid4)
                ? $"buvid3={buvid3}"
                : $"buvid3={buvid3}; buvid4={buvid4}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or IOException)
        {
            Debug.WriteLine("Bilibili", $"指纹获取失败:{ex.Message}");
            return null;
        }
    }

    /// <summary>把调用方令牌与"接口请求超时"合成为一个令牌。</summary>
    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(RequestTimeout);
        return source;
    }

    /// <summary>清理已经过期的信息缓存条目。</summary>
    private static void PurgeExpiredInfoCache()
    {
        var now = DateTimeOffset.Now;
        foreach (var entry in InfoCache)
            if (entry.Value.ExpireAt <= now)
                InfoCache.TryRemove(entry.Key, out _);
    }

    private static JsonElement GetObject(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long GetInt64(JsonElement element, string name, long fallback = 0) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : fallback;
}
