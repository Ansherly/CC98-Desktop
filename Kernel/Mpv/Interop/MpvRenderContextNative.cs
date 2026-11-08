// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using Mpv.Core.Enums.Render;
using Mpv.Core.Structs.Client;
using Mpv.Core.Structs.Render;

namespace Mpv.Core.Interop;

public partial class MpvRenderContextNative
{
    public MpvRenderContextNative(MpvHandle coreHandle, MpvRenderParam[] param)
    {
        var errorCode = mpv_render_context_create(out var context, coreHandle, param);
        if (errorCode != MpvError.Success)
        {
            throw new Exception($"Failed to create a render context. Error: {errorCode}", CreateError(errorCode));
        }

        Handle = context;
    }

    public int GLGetFrameBufferBinding()
    {
        glGetIntegerv(0x8CA6, out var data); // GL_DRAW_FRAMEBUFFER_BINDING
        return data;
    }

    public void SetParameter(MpvRenderParam param)
    {
        var errorCode = mpv_render_context_set_parameter(Handle, param);
        if (errorCode != MpvError.Success)
        {
            throw new Exception($"Failed to set a render context parameter. Error: {errorCode}", CreateError(errorCode));
        }
    }

    public MpvRenderParam GetInformation(MpvRenderParam param)
    {
        var errorCode = mpv_render_context_get_info(Handle, param);
        if (errorCode != MpvError.Success)
        {
            throw new Exception($"Failed to get a render context info. Error: {errorCode}", CreateError(errorCode));
        }

        return param;
    }

    public MpvRenderUpdateFlag Update()
        => mpv_render_context_update(Handle);

    public void SetUpdateCallback(MpvRenderUpdateCallback callback, IntPtr callbackContext)
    {
        var errorCode = mpv_render_context_set_update_callback(Handle, callback, callbackContext);
        if (errorCode != MpvError.Success)
        {
            throw new Exception($"Failed to set a render context update callback. Error: {errorCode}", CreateError(errorCode));
        }
    }

    public void Render(MpvRenderParam[] param)
    {
        var errorCode = mpv_render_context_render(Handle, param);
        if (errorCode != MpvError.Success)
        {
            throw new Exception($"Failed to render a frame. Error: {errorCode}", CreateError(errorCode));
        }
    }

    public void Destroy()
    {
        try
        {
            mpv_render_context_free(Handle);
        }
        catch (Exception)
        {
        }
    }

    public void ReportSwap()
    {
        var errorCode = mpv_render_context_report_swap(Handle);
        if (errorCode != MpvError.Success)
        {
            throw new Exception($"Failed to report a render context swap. Error: {errorCode}", CreateError(errorCode));
        }
    }

    public MpvRenderContextHandle Handle { get; }
}
