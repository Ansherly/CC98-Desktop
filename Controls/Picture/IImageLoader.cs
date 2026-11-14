using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;

namespace CC98.Controls;

/// <summary>
///     图片加载器抽象。
/// </summary>
/// <remarks>
///     返回类型是 <see cref="ImageSource" /> 而不是 <c>BitmapImage</c>:位图(<c>BitmapImage</c>)与
///     矢量图(<c>SvgImageSource</c>)都派生自它,这样加载器才能同时支持 SVG 与常见位图格式。
///     实现方负责选择具体类型,调用方只需把结果赋给 <c>Image.Source</c>。
/// </remarks>
public interface IImageLoader
{
    /// <summary>
    ///     加载图片。加载失败应返回 <see langword="null" />(而非抛异常),由调用方决定占位表现。
    /// </summary>
    /// <param name="src">图片地址(Web / 本地文件 / ms-appx 资源均可)。</param>
    /// <returns>可直接赋给 <c>Image.Source</c> 的图片源;失败时为 <see langword="null" />。</returns>
    Task<ImageSource?> LoadImage(string src);
}
