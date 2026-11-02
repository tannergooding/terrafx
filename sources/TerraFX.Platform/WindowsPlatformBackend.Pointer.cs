// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal sealed unsafe partial class WindowsPlatformBackend
{
    private static (Vector2 Position, Vector2 ContentScale) GetAbsolutePointerPosition(WindowState state, NativeRawMouse mouse)
    {
        var isVirtualDesktop = (mouse.Flags & RawMouseVirtualDesktop) != 0;
        var originX = isVirtualDesktop ? GetSystemMetricsNative((int)SystemMetricVirtualScreenLeft) : 0;
        var originY = isVirtualDesktop ? GetSystemMetricsNative((int)SystemMetricVirtualScreenTop) : 0;
        var width = GetSystemMetricsNative((int)(isVirtualDesktop ? SystemMetricVirtualScreenWidth : SystemMetricScreenWidth));
        var height = GetSystemMetricsNative((int)(isVirtualDesktop ? SystemMetricVirtualScreenHeight : SystemMetricScreenHeight));

        if ((mouse.LastX < 0) || (mouse.LastX > ushort.MaxValue)
            || (mouse.LastY < 0) || (mouse.LastY > ushort.MaxValue)
            || (width <= 0) || (height <= 0))
        {
            throw new InvalidOperationException("Windows returned an invalid absolute pointer coordinate.");
        }

        var point = new NativePoint {
            X = WindowsRawInputDecoder.MapAbsoluteCoordinate(mouse.LastX, originX, width),
            Y = WindowsRawInputDecoder.MapAbsoluteCoordinate(mouse.LastY, originY, height),
        };

        if (!ScreenToClientNative(state.NativeWindow, &point))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows could not map the absolute pointer position to its focused window.");
        }

        var dpi = GetDpiForWindowNative(state.NativeWindow);

        if (dpi == 0)
        {
            throw new InvalidOperationException("Windows did not report DPI for the focused pointer window.");
        }

        var scale = dpi / 96.0f;
        return (new Vector2(point.X, point.Y), new Vector2(scale, scale));
    }
}
