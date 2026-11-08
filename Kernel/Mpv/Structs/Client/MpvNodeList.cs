// Copyright (c) Bili Copilot. All rights reserved.

using System.Linq;
using System.Runtime.InteropServices;

namespace Mpv.Core.Structs.Client;

/// <summary>(see mpv_node)</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct MpvNodeList
{
    /// <summary>Number of entries. Negative values are not allowed.</summary>
    [FieldOffset(0)]
    public int Num;

    [FieldOffset(8)]
    internal IntPtr _nodesPtr;

    /// <summary>
    /// <para>Mpv_FORMAT_NODE_ARRAY:</para>
    /// <para>unused (typically NULL), access is not allowed</para>
    /// </summary>
    /// <remarks>
    /// <para>Mpv_FORMAT_NODE_MAP:</para>
    /// <para>keys[N] refers to key of the Nth key/value pair. If num &gt; 0, keys[0] to</para>
    /// <para>keys[num-1] (inclusive) are valid. Otherwise, this can be NULL.</para>
    /// <para>The keys are in random order. The only guarantee is that keys[N] belongs</para>
    /// <para>to the value values[N]. NULL keys are not allowed.</para>
    /// </remarks>
    [FieldOffset(16)]
    internal IntPtr _keysPtr;

    /// <summary>
    /// <para>Mpv_FORMAT_NODE_ARRAY:</para>
    /// <para>values[N] refers to value of the Nth item</para>
    /// </summary>
    public MpvNodeList[]? ValuesArray => this.ToArray();

    /// <summary>
    /// <para>Mpv_FORMAT_NODE_MAP:</para>
    /// <para>values[N] refers to value of the Nth key/value pair</para>
    /// <para>If num &gt; 0, values[0] to values[num-1] (inclusive) are valid.</para>
    /// <para>Otherwise, this can be NULL</para>
    /// </summary>
    public Dictionary<string, MpvNodeList>? ValuesMap => this.ToDictionary();

    public MpvNodeList(params MpvNodeList[] values)
    {
        Num = values.Length;
        _nodesPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<MpvNodeList>() * Num);
        for (int i = 0; i < Num; i++)
            Marshal.StructureToPtr(values[i], _nodesPtr + (i * Marshal.SizeOf<MpvNodeList>()), false);
    }

    public MpvNodeList(Dictionary<string, MpvNodeList> values)
    {
        Num = values.Count;
        var valKeys = values.Keys.ToArray();
        _keysPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<IntPtr>() * Num);
        for (int i = 0; i < Num; i++)
        {
            var ptr = Marshal.StringToCoTaskMemUTF8(valKeys[i]);
            Marshal.WriteIntPtr(_keysPtr, i * Marshal.SizeOf<IntPtr>(), ptr);
        }

        _nodesPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<MpvNodeList>() * Num);
        int c = 0;
        foreach (var item in values)
        {
            Marshal.StructureToPtr(item.Value, _nodesPtr + (c * Marshal.SizeOf<MpvNodeList>()), false);
            c++;
        }
    }

    private MpvNodeList[]? ToArray()
    {
        if (_nodesPtr == IntPtr.Zero)
            return null;
        var result = new MpvNodeList[Num];
        for (int i = 0; i < Num; i++)
            result[i] = Marshal.PtrToStructure<MpvNodeList>(_nodesPtr + (i * Marshal.SizeOf<MpvNodeList>()));
        return result;
    }

    private Dictionary<string, MpvNodeList>? ToDictionary()
    {
        if (_keysPtr == IntPtr.Zero || _nodesPtr == IntPtr.Zero)
            return null;
        var result = new Dictionary<string, MpvNodeList>(Num);
        for (int i = 0; i < Num; i++)
        {
            var val = Marshal.PtrToStructure<MpvNodeList>(_nodesPtr + (i * Marshal.SizeOf<MpvNodeList>()));
            var keyPtr = Marshal.ReadIntPtr(_keysPtr, i * Marshal.SizeOf<IntPtr>());
            var key = Marshal.PtrToStringUTF8(keyPtr);
            result.Add(key!, val);
        }

        return result;
    }
}
