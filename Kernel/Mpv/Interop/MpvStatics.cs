// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using Mpv.Core.Structs.Client;
using System.Runtime.InteropServices;

namespace Mpv.Core.Interop;

public static partial class MpvStatics
{
    [LibraryImport(MpvIdentifier)]
    private static partial ulong mpv_client_api_version();

    [LibraryImport(MpvIdentifier)]
    private static partial void mpv_free(IntPtr data);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(StringMarshaller))]
    private static partial string mpv_error_string(int error);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(StringMarshaller))]
    private static partial string mpv_event_name(MpvEventId eventId);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_event_to_node(out MpvNode node, ref MpvEvent src);

    private static void FreeInternal<T>(ref T data) where T : struct
    {
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
        try
        {
            Marshal.StructureToPtr(data, ptr, false);
            mpv_free(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Return the API version the mpv source has been compiled with.
    /// </summary>
    public static Version Version()
    {
        var versionNum = mpv_client_api_version();
        var major = (int)(versionNum >> 16);
        var minor = (int)(versionNum & 0xFFFF);

        return new Version(major, minor);
    }

    /// <summary>
    /// General function to deallocate memory returned by some of the API functions.
    /// Call this only if it's explicitly documented as allowed.
    /// </summary>
    public static void Free(ref IntPtr data) => mpv_free(data);

    public static void Free(ref MpvNode node) => FreeInternal(ref node);

    public static void Free(ref MpvNodeList nodeList) => FreeInternal(ref nodeList);

    public static void Free(ref MpvByteArray byteArray) => FreeInternal(ref byteArray);

    /// <summary>
    /// Return a string describing the error. For unknown errors, the string
    /// "unknown error" is returned.
    /// </summary>
    /// <param name="error">error error number, see enum <see cref="MpvError"/>.</param>
    /// <returns>
    /// A static string describing the error. The string is completely
    /// static, i.e.doesn't need to be deallocated, and is valid forever.
    /// </returns>
    public static string ParseError(int error)
        => mpv_error_string(error);

    /// <summary>
    /// Return a string describing the error. For unknown errors, the string
    /// "unknown error" is returned.
    /// </summary>
    /// <param name="error">error error number, see enum <see cref="MpvError"/>.</param>
    /// <returns>
    /// A static string describing the error. The string is completely
    /// static, i.e.doesn't need to be deallocated, and is valid forever.
    /// </returns>
    public static string ParseError(MpvError error)
        => ParseError((int)error);

    public static string ParseEventName(MpvEventId eventId)
        => mpv_event_name(eventId);

    public static MpvNode ToNode(this MpvEvent @event)
    {
        mpv_event_to_node(out var node, ref @event);
        return node;
    }
}
