// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Render;
using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Render;

/// <summary>
/// <para>Used to pass arbitrary parameters to some mpv_render_* functions. The</para>
/// <para>meaning of the data parameter is determined by the type, and each</para>
/// <para>Mpv_RENDER_PARAM_* documents what type the value must point to.</para>
/// </summary>
/// <remarks>
/// <para>Each value documents the required data type as the pointer you cast to</para>
/// <para>void* and set on mpv_render_param.data. For example, if Mpv_RENDER_PARAM_FOO</para>
/// <para>documents the type as Something* , then the code should look like this:</para>
/// <para>Something foo = {...};</para>
/// <para>mpv_render_param param;</para>
/// <para>param.type = Mpv_RENDER_PARAM_FOO;</para>
/// <para>param.data =&amp;foo;</para>
/// <para>Normally, the data field points to exactly 1 object. If the type is char*,</para>
/// <para>it points to a 0-terminated string.</para>
/// <para>In all cases (unless documented otherwise) the pointers need to remain</para>
/// <para>valid during the call only. Unless otherwise documented, the API functions</para>
/// <para>will not write to the params array or any data pointed to it.</para>
/// <para>As a convention, parameter arrays are always terminated by type==0. There</para>
/// <para>is no specific order of the parameters required. The order of the 2 fields in</para>
/// <para>this struct is guaranteed (even after ABI changes).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct MpvRenderParam
{
    public MpvRenderParamType Type;
    public IntPtr Data; //idk what types this could be
}