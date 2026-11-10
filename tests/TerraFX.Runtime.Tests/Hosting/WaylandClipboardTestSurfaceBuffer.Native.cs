// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed unsafe partial class WaylandClipboardTestSurfaceBuffer
{
    private static IOException CreateNativeIOException(string operation)
        => new($"The Wayland test surface could not {operation}; native error {Marshal.GetLastPInvokeError()}.");

    private static void DestroyProxy(ref nint proxy, uint opcode, bool sendDestructorRequest)
    {
        if (proxy == 0)
        {
            return;
        }

        if (sendDestructorRequest)
        {
            SendRequest(proxy, opcode, arguments: null);
        }

        WaylandNative.ProxyDestroy(proxy);
        proxy = 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnBufferRelease(nint data, nint buffer)
    {
        _ = data;
        _ = buffer;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnGlobal(nint data, nint registry, uint name, nint interfaceNamePointer, uint version)
    {
        _ = registry;

        if (GCHandle.FromIntPtr(data).Target is not WaylandClipboardTestSurfaceBuffer surfaceBuffer)
        {
            return;
        }

        try
        {
            if (Marshal.PtrToStringUTF8(interfaceNamePointer) is "wl_shm")
            {
                surfaceBuffer._shmName = name;
                surfaceBuffer._shmVersion = version;
            }
        }
        catch (Exception exception)
        {
            surfaceBuffer._callbackFailure ??= exception;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGlobalRemove(nint data, nint registry, uint name)
    {
        _ = data;
        _ = registry;
        _ = name;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnShmFormat(nint data, nint shm, uint format)
    {
        _ = shm;

        if (GCHandle.FromIntPtr(data).Target is not WaylandClipboardTestSurfaceBuffer surfaceBuffer)
        {
            return;
        }

        surfaceBuffer._shmFormatCount++;

        if (surfaceBuffer._shmFormatCount == 1)
        {
            surfaceBuffer._firstShmFormat = format;
        }

        if (format == ShmFormatXrgb8888)
        {
            surfaceBuffer._supportsXrgb8888 = true;
        }
    }

    private static void SendRequest(nint proxy, uint opcode, WaylandArgument* arguments)
    {
        if (proxy != 0)
        {
            _ = WaylandNative.ProxyMarshalArrayFlags(
                proxy,
                opcode,
                interfacePointer: 0,
                WaylandNative.ProxyGetVersion(proxy),
                flags: 0,
                arguments);
        }
    }
}
