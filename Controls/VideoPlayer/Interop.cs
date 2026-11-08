using System;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;

namespace CC98.Controls.Interop;

/// <summary>
///     <c>SwapChainPanel</c> 的原生接口:用于把自建的 DXGI 交换链接到 XAML 面板上。
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
public interface ISwapChainPanelNative
{
    /// <summary>把交换链挂到面板上;传 <see cref="IntPtr.Zero" /> 表示解绑。</summary>
    [PreserveSig]
    HResult SetSwapChain([In] IntPtr swapChain);

    /// <summary>释放引用。</summary>
    [PreserveSig]
    ulong Release();
}
