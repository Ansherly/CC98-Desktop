// Copyright (c) Bili Copilot. All rights reserved.

using Mpv.Core.Enums.Client;
using Mpv.Core.Structs.Client;
using System.Runtime.InteropServices;

namespace Mpv.Core.Interop;

public partial class MpvClientNative
{
    #region Client
    [LibraryImport(MpvIdentifier)]
    private static partial MpvHandle mpv_create();

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvHandle mpv_create_client(MpvHandle handle, string name);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvHandle mpv_create_weak_client(MpvHandle handle, string name);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_initialize(MpvHandle handle);

    [LibraryImport(MpvIdentifier)]
    private static partial void mpv_destroy(MpvHandle handle);

    [LibraryImport(MpvIdentifier)]
    private static partial void mpv_terminate_destroy(MpvHandle handle);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(StringMarshaller))]
    private static partial string mpv_client_name(MpvHandle handle);

    [LibraryImport(MpvIdentifier)]
    private static partial long mpv_client_id(MpvHandle handle);
    #endregion

    #region Config
    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_load_config_file(MpvHandle handle, string fileName);
    #endregion

    #region Options
    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_set_option(MpvHandle handle, string name, MpvFormat format, ref MpvNode value);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_set_option_string(MpvHandle handle, string name, string value);
    #endregion

    #region Command
    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_command(MpvHandle handle, string?[] args);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_command_string(MpvHandle handle, string args);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_command_node(MpvHandle handle, ref MpvNode command, out MpvNode result);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_command_ret(MpvHandle handle, string?[] command, out MpvNode result);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_command_async(MpvHandle handle, ulong requestId, string?[] args);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_command_node_async(MpvHandle handle, ulong requestId, ref MpvNode command);

    [LibraryImport(MpvIdentifier)]
    private static partial void mpv_abort_async_command(MpvHandle handle, ulong requestId);
    #endregion

    #region Properties
    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_set_property(MpvHandle handle, string name, MpvFormat format, ref MpvNode data);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_set_property_string(MpvHandle handle, string name, string data);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_set_property_async(MpvHandle handle, ulong requestId, string name, MpvFormat format, ref MpvNode data);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_get_property(MpvHandle handle, string name, MpvFormat format, out MpvNode data);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial string mpv_get_property_string(MpvHandle handle, string name);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_del_property(MpvHandle handle, string name);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_observe_property(MpvHandle handle, ulong requestId, string name, MpvFormat format);

    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_unobserve_property(MpvHandle handle, ulong requestId);
    #endregion

    #region Events
    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_request_event(MpvHandle handle, MpvEventId eventId, int enabled);

    [LibraryImport(MpvIdentifier)]
    private static partial IntPtr mpv_wait_event(MpvHandle handle, double timeout);

    [LibraryImport(MpvIdentifier)]
    private static partial void mpv_wakeup(MpvHandle handle);

    [LibraryImport(MpvIdentifier)]
    private static partial void mpv_set_wakeup_callback(MpvHandle handle, MpvWakeupCallback callback, IntPtr data);

    [LibraryImport(MpvIdentifier)]
    private static partial void mpv_wait_async_requests(MpvHandle handle);
    #endregion

    #region Hook
    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_hook_add(MpvHandle handle, ulong requestId, string name, int priority);

    [LibraryImport(MpvIdentifier)]
    private static partial MpvError mpv_hook_continue(MpvHandle handle, ulong requestId);
    #endregion

    #region Log
    [LibraryImport(MpvIdentifier, StringMarshalling = StringMarshalling.Utf8)]
    private static partial MpvError mpv_request_log_messages(MpvHandle handle, string minLevel);
    #endregion

    #region Delegate
    /// <summary>mpv 唤醒回调。<c>LibraryImport</c> 不支持泛型委托的封送,所以这里不用泛型。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MpvWakeupCallback(IntPtr data);
    #endregion
}