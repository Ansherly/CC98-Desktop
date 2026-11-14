using System;
using System.Runtime.InteropServices;
using WinRT;

namespace CC98.Controls.Interop;

/// <summary>
///     <c>SwapChainPanel</c> 的原生接口 <c>ISwapChainPanelNative</c>(IID 63aad0b8-…),
///     用来把自建的 DXGI 交换链接到 XAML 面板上。
/// </summary>
/// <remarks>
///     <para>
///         <b>不能用</b> <c>[ComImport]</c> + <c>panel.As&lt;ISwapChainPanelNative&gt;()</c>:那条路在
///         NativeAOT 下会走 <c>ComWrappers.ComObjectForInterface</c>,直接抛
///         <c>NotSupportedException 0x80131515 "COM Interop requires ComWrapper instance registered for marshalling."</c>
///         —— 和之前"托管类实现 WinRT 接口建不出 CCW"是同一类问题。
///     </para>
///     <para>
///         这里改成:IWinRTObject.NativeObject.ThisPtr 拿裸 IInspectable 指针(不经过任何 marshal),
///         QueryInterface 拿到目标接口,再按 vtable 手抄调用。
///         <c>ISwapChainPanelNative</c> 的布局是 IUnknown 三项 + <c>SetSwapChain</c>,所以槽位是 3。
///     </para>
/// </remarks>
internal static unsafe class SwapChainPanelNative
{
    /// <summary>ISwapChainPanelNative 的 IID。</summary>
    public static readonly Guid InterfaceId = new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    /// <summary>IUnknown(3 项)之后的第一个槽位就是 SetSwapChain。</summary>
    private const int SetSwapChainSlot = 3;

    /// <summary>
    ///     把交换链挂到面板上;传 <see cref="IntPtr.Zero" /> 表示解绑。
    /// </summary>
    /// <param name="panel">XAML 的 <c>SwapChainPanel</c>。</param>
    /// <param name="swapChain">DXGI 交换链指针。</param>
    /// <returns>HRESULT。</returns>
    public static int SetSwapChain(object panel, IntPtr swapChain)
    {
        var inspectable = GetInspectable(panel);
        if (inspectable == IntPtr.Zero) return unchecked((int)0x80004003); // E_POINTER

        var iid = InterfaceId;
        var hr = Marshal.QueryInterface(inspectable, in iid, out var native);
        if (hr < 0) return hr;

        try
        {
            var vtable = *(void***)native;
            var setSwapChain = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vtable[SetSwapChainSlot];
            return setSwapChain(native, swapChain);
        }
        finally
        {
            Marshal.Release(native);
        }
    }

    /// <summary>取对象的 IInspectable 裸指针。WinRT 投影对象直接给,普通 COM 对象退化成 GetIUnknownForObject。</summary>
    private static IntPtr GetInspectable(object panel)
    {
        if (panel is IWinRTObject winrtObject) return winrtObject.NativeObject.ThisPtr;

        try
        {
            // 非 WinRT 投影对象(理论上不会走到):依赖 BuiltInComInteropSupport
            return Marshal.GetIUnknownForObject(panel);
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }
}
