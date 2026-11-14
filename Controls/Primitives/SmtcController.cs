using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Storage.Streams;

namespace CC98.Controls.Primitives;

/// <summary>
///     把 <see cref="GlobalMediaPlayer" /> 的播放状态接入 Windows 11 系统媒体状态栏(SMTC):
///     在音量浮层/锁屏/媒体键浮窗中显示歌名、作者与海报,并响应系统的播放/暂停按钮。
/// </summary>
/// <remarks>
///     WinUI 3 桌面端的 SMTC 必须经由窗口(WindowId)获取,整个进程共享一个实例;
///     需先在某处调用 <see cref="Initialize" /> 注册主窗口,之后由当前持有全局播放器的
///     MusicPlayer 在状态变化时调用 <see cref="Update" /> 上报信息。
///     海报:桌面端的 SMTC 由系统进程渲染,拿不到应用的网络上下文,远程图必须先在应用内
///     下载并转成内存流;同时统一转码为 PNG,规避系统侧缺少 WebP 解码器导致的空白。
/// </remarks>
public static class SmtcController
{
    private static SystemMediaTransportControls? _smtc;
    private static bool _initialized;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>注册承载 SMTC 的窗口。重复调用无副作用;拿不到窗口前其他方法全部静默跳过。</summary>
    public static void Initialize(Window window)
    {
        if (_initialized) return;

        //桌面端 SMTC 经由窗口句柄获取(WinRT 投影的 GetForWindow 接收 HWND)
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var smtc = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
        smtc.IsEnabled = true;
        //上一首/下一首没有业务语义(帖子里的音频互相独立),不启用对应按钮
        smtc.IsPlayEnabled = true;
        smtc.IsPauseEnabled = true;
        smtc.ButtonPressed += OnButtonPressed;

        _smtc = smtc;
        _initialized = true;
    }

    private static void OnButtonPressed(SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        if (args.Button is not (SystemMediaTransportControlsButton.Play or SystemMediaTransportControlsButton.Pause))
            return;

        //无拥有者(无源)时忽略,避免对空源 Play
        if (GlobalMediaPlayer.Instance.CurrentOwner == null) return;

        GlobalMediaPlayer.Instance.TogglePlayPause();
    }

    /// <summary>上报当前歌曲信息与播放状态。异步执行;初始化前调用则静默跳过。</summary>
    public static async void Update(string title, string artist, string posterUri, bool isPlaying)
    {
        if (!_initialized) return;
        var smtc = _smtc!;

        try
        {
            var updater = smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = string.IsNullOrWhiteSpace(title) ? "CC98 音频" : title;
            updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(artist) ? "未知作者" : artist;
            updater.MusicProperties.AlbumTitle = "CC98";

            var thumbnail = await GetThumbnailAsync(posterUri);
            if (thumbnail != null)
                updater.Thumbnail = thumbnail;

            smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            updater.Update();
        }
        catch
        {
            //SMTC 属于系统侧锦上添花的功能,任何失败都不应影响播放本身
        }
    }

    /// <summary>无正在播放的媒体时把状态栏置为停止。</summary>
    public static void SetStopped()
    {
        if (!_initialized) return;

        try
        {
            _smtc!.PlaybackStatus = MediaPlaybackStatus.Stopped;
            _smtc.DisplayUpdater.Update();
        }
        catch
        {
            //同上,忽略系统侧失败
        }
    }

    /// <summary>
    ///     为 SMTC 准备海报缩略图:本地 URI 直接引用;远程图下载后转码为 PNG 内存流。
    /// </summary>
    private static async Task<RandomAccessStreamReference?> GetThumbnailAsync(string? posterUri)
    {
        if (string.IsNullOrWhiteSpace(posterUri)) return null;
        if (!Uri.TryCreate(posterUri, UriKind.Absolute, out var uri)) return null;

        //本地资源(内置封面等)系统侧本来就能解析
        if (uri.Scheme is "ms-appx" or "ms-appdata" or "file")
            return RandomAccessStreamReference.CreateFromUri(uri);

        try
        {
            var bytes = await Http.GetByteArrayAsync(uri);

            var raw = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(raw))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            raw.Seek(0);

            //统一转码为 PNG:SMTC 由系统进程渲染,若系统缺 WebP 编解码器,直接塞原始字节会得到空白封面
            try
            {
                var decoder = await BitmapDecoder.CreateAsync(raw);
                var pixelData = (await decoder.GetPixelDataAsync()).DetachPixelData();

                var png = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
                encoder.SetPixelData(decoder.BitmapPixelFormat, BitmapAlphaMode.Ignore,
                    decoder.OrientedPixelWidth, decoder.OrientedPixelHeight,
                    decoder.DpiX, decoder.DpiY, pixelData);
                await encoder.FlushAsync();
                png.Seek(0);

                raw.Dispose();
                return RandomAccessStreamReference.CreateFromStream(png);
            }
            catch
            {
                //应用内解码失败则退回原始字节流(系统若恰好有对应解码器仍可显示)
                raw.Seek(0);
                return RandomAccessStreamReference.CreateFromStream(raw);
            }
        }
        catch
        {
            //下载失败(断网/404/超时)则不更新封面,保留 SMTC 上一张
            return null;
        }
    }
}
