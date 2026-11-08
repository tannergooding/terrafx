// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal sealed unsafe partial class WaylandPlatformBackend
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWaylandArray
    {
        internal nuint Size;
        internal nuint Allocated;
        internal nint Data;
    }

    private sealed class WaylandOutputState
    {
        internal WaylandOutputState(WaylandGlobal global, nint proxy, uint version)
        {
            CurrentHeight = 0;
            CurrentRefreshRate = 0;
            CurrentWidth = 0;
            DeviceName = string.Concat("wayland-output-", global.Name.ToString(CultureInfo.InvariantCulture));
            Global = global;
            HasCurrentMode = false;
            Make = string.Empty;
            Model = string.Empty;
            NativeProxy = proxy;
            PhysicalHeight = 0;
            PhysicalWidth = 0;
            Scale = 1;
            Version = checked((int)version);
            X = 0;
            Y = 0;
        }

        internal int CurrentHeight { get; set; }

        internal int CurrentRefreshRate { get; set; }

        internal int CurrentWidth { get; set; }

        internal string DeviceName { get; }

        internal WaylandGlobal Global { get; }

        internal bool HasCurrentMode { get; set; }

        internal string Make { get; set; }

        internal string Model { get; set; }

        internal nint NativeProxy { get; }

        internal int PhysicalHeight { get; set; }

        internal int PhysicalWidth { get; set; }

        internal int Scale { get; set; }

        internal int Version { get; }

        internal int X { get; set; }

        internal int Y { get; set; }
    }

    private sealed class WaylandWindowState
    {
        internal WaylandWindowState(
            WindowHandle window,
            Vector2 initialLogicalClientSize,
            WindowMode requestedMode,
            nint nativeOutput,
            nint surface,
            nint xdgSurface,
            nint toplevel)
        {
            EnteredOutputs = [];
            InitialLogicalClientSize = initialLogicalClientSize;
            IsConfigured = false;
            IsFullscreen = false;
            LogicalClientSize = initialLogicalClientSize;
            NativeOutput = nativeOutput;
            ObservedMode = null;
            PendingConfiguredHeight = 0;
            PendingConfiguredWidth = 0;
            PendingFullscreen = false;
            PointerPosition = default;
            RequestedMode = requestedMode;
            Surface = surface;
            XdgSurface = xdgSurface;
            Toplevel = toplevel;
            Window = window;
        }

        internal HashSet<nint> EnteredOutputs { get; }

        internal Vector2 InitialLogicalClientSize { get; }

        internal bool IsConfigured { get; set; }

        internal bool IsFullscreen { get; set; }

        internal Vector2 LogicalClientSize { get; set; }

        internal nint NativeOutput { get; set; }

        internal WindowMode? ObservedMode { get; set; }

        internal int PendingConfiguredHeight { get; set; }

        internal int PendingConfiguredWidth { get; set; }

        internal bool PendingFullscreen { get; set; }

        internal Vector2 PointerPosition { get; set; }

        internal WindowMode RequestedMode { get; set; }

        internal nint Surface { get; }

        internal nint Toplevel { get; }

        internal WindowHandle Window { get; }

        internal nint XdgSurface { get; }
    }
}
