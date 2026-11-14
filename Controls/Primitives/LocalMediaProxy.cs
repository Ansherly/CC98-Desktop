using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CC98.Controls.Primitives;

/// <summary>
///     本机回环代理:把一个需要请求头(Referer/Cookie/UA)的媒体地址,变成 MF 能直接吃的
///     <c>http://127.0.0.1:&lt;port&gt;/media</c>。
/// </summary>
/// <remarks>
///     <para>
///         为什么不用"托管实现 IRandomAccessStream":AOT + full trim 下 CsWinRT 不会为托管类生成
///         CCW 的 vtable,<c>MediaSource.CreateFromStream</c> 会抛
///         <c>Failed to create a CCW ... E_NOINTERFACE</c>。
///     </para>
///     <para>
///         为什么用 TcpListener 而不是 HttpListener:后者的 urlacl 在非管理员/打包进程里可能拿不到。
///         这里只实现够用的 HTTP/1.1:一条连接一个请求,原样转发 <c>Range</c>、回 206/200,
///         所以拖动仍然是一次新的 Range 请求,不会整段下载。
///     </para>
/// </remarks>
public sealed class LocalMediaProxy : IDisposable
{
    private readonly TcpListener _listener;
    private readonly HttpClient _client;
    private readonly Uri _target;
    private readonly Dictionary<string, string> _headers;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    private LocalMediaProxy(Uri target, IReadOnlyDictionary<string, string>? headers)
    {
        _target = target;
        _headers = headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);

        _client = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = new Uri($"http://127.0.0.1:{port}/media");

        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>喂给 <c>MediaSource.CreateFromUri</c> 的本地地址。</summary>
    public Uri Url { get; }

    /// <summary>启动代理。</summary>
    /// <param name="target">真正的媒体地址。</param>
    /// <param name="headers">每次转发都要附加的请求头。</param>
    public static LocalMediaProxy Start(Uri target, IReadOnlyDictionary<string, string>? headers = null)
        => new(target, headers);

    /// <inheritdoc />
    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _acceptLoop.Wait(200); } catch { }
        _client.Dispose();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = await ReadHeadAsync(stream);
                if (head is null) return;

                var (method, range) = ParseRequest(head);

                using var request = new HttpRequestMessage(
                    string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Head : HttpMethod.Get,
                    _target);

                if (!string.IsNullOrEmpty(range)) request.Headers.TryAddWithoutValidation("Range", range);
                foreach (var (name, value) in _headers) request.Headers.TryAddWithoutValidation(name, value);

                using var response = await _client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);

                await WriteResponseHeadAsync(stream, response);

                if (string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) return;

                await using var body = await response.Content.ReadAsStreamAsync(_cts.Token);
                var buffer = new byte[64 * 1024];
                while (!_cts.IsCancellationRequested)
                {
                    var read = await body.ReadAsync(buffer, _cts.Token);
                    if (read <= 0) break;

                    await stream.WriteAsync(buffer.AsMemory(0, read), _cts.Token);
                }
            }
            catch
            {
                // 客户端(MF)断开、取消、上游出错都走到这里:一条连接失败不影响别的连接
            }
        }
    }

    private static async Task WriteResponseHeadAsync(NetworkStream stream, HttpResponseMessage response)
    {
        var builder = new StringBuilder(256);
        builder.Append("HTTP/1.1 ")
            .Append((int)response.StatusCode)
            .Append(' ')
            .Append(string.IsNullOrEmpty(response.ReasonPhrase) ? "OK" : response.ReasonPhrase)
            .Append("\r\n");

        var contentType = response.Content.Headers.ContentType?.ToString();
        AppendHeader(builder, "Content-Type", string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType);

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is not null) AppendHeader(builder, "Content-Length", contentLength.Value.ToString());

        var contentRange = response.Content.Headers.ContentRange;
        if (contentRange is not null) AppendHeader(builder, "Content-Range", contentRange.ToString());

        AppendHeader(builder, "Accept-Ranges", "bytes");
        AppendHeader(builder, "Connection", "close");
        builder.Append("\r\n");

        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(bytes);
    }

    private static void AppendHeader(StringBuilder builder, string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        builder.Append(name).Append(": ").Append(value).Append("\r\n");
    }

    /// <summary>读到空行为止(请求头),最多读 8 KB。</summary>
    private static async Task<string?> ReadHeadAsync(NetworkStream stream)
    {
        var buffer = new byte[8 * 1024];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length));
            if (read <= 0) break;
            length += read;

            var text = Encoding.ASCII.GetString(buffer, 0, length);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal)) return text;
        }

        return length > 0 ? Encoding.ASCII.GetString(buffer, 0, length) : null;
    }

    private static (string Method, string? Range) ParseRequest(string head)
    {
        var lines = head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return ("GET", null);

        var parts = lines[0].Split(' ');
        var method = parts.Length > 0 ? parts[0] : "GET";

        string? range = null;
        foreach (var line in lines)
        {
            if (!line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) continue;
            range = line["Range:".Length..].Trim();
            break;
        }

        return (method, range);
    }
}
