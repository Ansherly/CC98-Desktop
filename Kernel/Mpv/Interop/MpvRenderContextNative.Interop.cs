// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using Mpv.Core.Enums.Render;
using Mpv.Core.Structs.Client;
using Mpv.Core.Structs.Render;
using System.Runtime.InteropServices;

namespace Mpv.Core.Interop;

public partial class MpvRenderContextNative
{
    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_render_context_create(out MpvRenderContextHandle context, MpvHandle handle, MpvRenderParam[] param);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_render_context_set_parameter(MpvRenderContextHandle context, MpvRenderParam param);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_render_context_get_info(MpvRenderContextHandle context, MpvRenderParam param);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_render_context_set_update_callback(MpvRenderContextHandle context, MpvRenderUpdateCallback callback, IntPtr callbackContext);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvRenderUpdateFlag mpv_render_context_update(MpvRenderContextHandle context);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_render_context_render(MpvRenderContextHandle context, MpvRenderParam[] param);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_render_context_report_swap(MpvRenderContextHandle context);

    [LibraryImport(MpvIdentifier)]
    private static partial void mpv_render_context_free(MpvRenderContextHandle context);

    [LibraryImport("gl")]
    private static partial void glGetIntegerv(int pname, out int data);

    public delegate void MpvRenderUpdateCallback(IntPtr callbackCtx);
}
