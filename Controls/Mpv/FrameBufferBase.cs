using System;

namespace MpvWinUI.Common;

public abstract class FrameBufferBase : IDisposable
{
    public abstract int BufferWidth { get; protected set; }

    public abstract int BufferHeight { get; protected set; }

    public abstract IntPtr SwapChainHandle { get; protected set; }

    public abstract void Dispose();
}
