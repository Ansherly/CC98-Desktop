using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CC98.Controls.Picture;

/// <summary>
///     默认图片加载器:把绝对 URI 直接交给系统解码。
/// </summary>
/// <remarks>
///     不做网络请求与缓存,只适合本地 / 资源地址或已由系统缓存的远程地址;
///     应用内的常规图片加载请用 <c>SmartImageLoader</c>。
///     <para>
///         自 2026-09-19 起支持 SVG:按扩展名判断(<see cref="ImageSourceFactory.LooksLikeSvg(string)" />),
///         <c>.svg</c> 走 <see cref="SvgImageSource" />,其余仍走 <see cref="BitmapImage" />。
///         URL 后缀不可靠时请改用 <see cref="ImageSourceFactory.FromBytesAsync" /> 走内容嗅探。
///     </para>
/// </remarks>
public class DefaultImageLoader : IImageLoader
{
    /// <summary>是否按低分辨率解码。</summary>
    public bool LowRes { get; set; }

    public async Task<ImageSource?> LoadImage(string src)
    {
        try
        {
            if (Uri.TryCreate(src, UriKind.Absolute, out var uri))
                return await Task.FromResult(ImageSourceFactory.FromUri(uri, LowRes));

            return null;
        }
        catch
        {
            return null;
        }
    }
}
