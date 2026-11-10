// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;

namespace TerraFX.Runtime.Tests.Hosting;

internal static partial class X11NativeSurfaceTest
{
    internal static void DestroyWindowExternally(nuint nativeWindow)
    {
        ArgumentOutOfRangeException.ThrowIfZero(nativeWindow);
        var display = XOpenDisplay(null);

        if (display == 0)
        {
            throw new PlatformNotSupportedException("The isolated X11 display could not be opened.");
        }

        try
        {
            if (XDestroyWindow(display, nativeWindow) == 0)
            {
                throw new InvalidOperationException("The isolated X11 server rejected native window destruction.");
            }

            _ = XSync(display, discard: 0);
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static partial int XCloseDisplay(nint display);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XDestroyWindow")]
    private static partial int XDestroyWindow(nint display, nuint window);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XOpenDisplay(string? displayName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
    private static partial int XSync(nint display, int discard);
}
