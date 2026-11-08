// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Client;

/// <summary>Generic data storage.</summary>
/// <remarks>
/// <para>If mpv writes this struct (e.g. via mpv_get_property()), you must not change</para>
/// <para>the data. In some cases (mpv_get_property()), you have to free it with</para>
/// <para>mpv_free_node_contents(). If you fill this struct yourself, you're also</para>
/// <para>responsible for freeing it, and you must not call mpv_free_node_contents().</para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct MpvNode
{
    /// <summary>valid if format==Mpv_FORMAT_STRING</summary>
    public string? StringValue => Marshal.PtrToStringUTF8(_structuredValue);

    [FieldOffset(0)]
    internal IntPtr _structuredValue;

    /// <summary>valid if format==Mpv_FORMAT_FLAG</summary>
    /// <remarks>0 = no; 1 = yes</remarks>
    [FieldOffset(0)]
    public readonly int Flag;

    /// <summary>valid if format==Mpv_FORMAT_INT64</summary>
    [FieldOffset(0)]
    public readonly long IntegerValue;

    /// <summary>valid if format==Mpv_FORMAT_DOUBLE</summary>
    [FieldOffset(0)]
    public readonly double DoubleValue;

    /// <summary>valid if format==Mpv_FORMAT_NODE_ARRAY</summary>
    /// <summary>or if format==Mpv_FORMAT_NODE_MAP</summary>
    public MpvNodeList RemoteNodeListValue => Marshal.PtrToStructure<MpvNodeList>(_structuredValue);

    /// <summary>valid if format==Mpv_FORMAT_BYTE_ARRAY</summary>
    public MpvByteArray ByteArrayValue => Marshal.PtrToStructure<MpvByteArray>(_structuredValue);

    /// <summary>
    /// <para>Type of the data stored in this struct. This value rules what members in</para>
    /// <para>the given union can be accessed. The following formats are currently</para>
    /// <para>defined to be allowed in mpv_node:</para>
    /// </summary>
    /// <remarks>
    /// <para>Mpv_FORMAT_STRING       (u.string)</para>
    /// <para>Mpv_FORMAT_FLAG         (u.flag)</para>
    /// <para>Mpv_FORMAT_INT64        (u.int64)</para>
    /// <para>Mpv_FORMAT_DOUBLE       (u.double_)</para>
    /// <para>Mpv_FORMAT_NODE_ARRAY   (u.list)</para>
    /// <para>Mpv_FORMAT_NODE_MAP     (u.list)</para>
    /// <para>Mpv_FORMAT_BYTE_ARRAY   (u.ba)</para>
    /// <para>Mpv_FORMAT_NONE         (no member)</para>
    /// <para>If you encounter a value you don't know, you must not make any</para>
    /// <para>assumptions about the contents of union u.</para>
    /// </remarks>
    [FieldOffset(8)]
    public MpvFormat Format;

    public MpvNode(string? value)
    {
        Format = MpvFormat.String;
        _structuredValue = Marshal.StringToCoTaskMemUTF8(value);
    }

    public MpvNode(bool value)
    {
        Format = MpvFormat.Flag;
        Flag = value ? 1 : 0;
    }

    public MpvNode(long value)
    {
        Format = MpvFormat.Int64;
        IntegerValue = value;
    }

    public MpvNode(double value)
    {
        Format = MpvFormat.Double;
        DoubleValue = value;
    }

    public MpvNode(MpvNodeList value)
    {
        Format = value._keysPtr != IntPtr.Zero ? MpvFormat.NodeMap : MpvFormat.NodeArray;
        _structuredValue = Marshal.AllocCoTaskMem(Marshal.SizeOf<MpvNodeList>());
        Marshal.StructureToPtr(value, _structuredValue, false);
    }

    public MpvNode(MpvByteArray value)
    {
        Format = MpvFormat.ByteArray;
        _structuredValue = Marshal.AllocCoTaskMem(Marshal.SizeOf<MpvByteArray>());
        Marshal.StructureToPtr(value, _structuredValue, false);
    }
}
