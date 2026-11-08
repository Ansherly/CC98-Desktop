// Copyright (c) Bili Copilot. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Mpv.Core;

[CustomMarshaller(typeof(string), MarshalMode.ManagedToUnmanagedOut, typeof(StringMarshaller))]
internal static class StringMarshaller
{
    public static string ConvertToManaged(IntPtr unmanaged)
        => Marshal.PtrToStringUTF8(unmanaged) ?? string.Empty;
}
