// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal interface IPlatformBackend : IDisposable
{
    PlatformServiceStatus AudioStatus { get; }

    PlatformBackend Backend { get; }

    ClipboardServiceStatus ClipboardStatus { get; }

    PlatformServiceStatus DisplayStatus { get; }

    PlatformServiceStatus GamepadStatus { get; }

    uint OwnerId { get; }

    PlatformServiceStatus PhysicalInputStatus { get; }

    void CloseWindow(WindowHandle window);

    void CreateWindow(WindowHandle window, WindowOptions options, nint nativeMonitorHandle);

    PlatformInputSampleBatch DrainInputSamples();

    ImmutableArray<WindowHandle> DrainWindowCloseRequests();

    ImmutableArray<MonitorHandle> GetMonitorHandles();

    ImmutableArray<PlatformMonitorInfo> GetMonitorInfo();

    PlatformNativeWindowMetrics GetNativeWindowMetrics(WindowHandle window);

    PlatformNativeWindowModeState GetNativeWindowModeState(WindowHandle window);

    void Pump();

    ClipboardReadResponse ReadClipboardText(ClipboardReadRequest request);

    void RequestGamepadStateReconciliation(nint nativeDeviceIdentity);

    void RequestInputStateReconciliation(WindowHandle? window);

    void SetMonitorHandles(ImmutableArray<MonitorHandle> monitorHandles);

    WindowCommandStatus SetWindowClientSize(WindowHandle window, Vector2 logicalClientSize);

    WindowCommandStatus SetWindowMode(WindowHandle window, WindowMode mode);

    WindowCommandStatus SetWindowTitle(WindowHandle window, string title);

    bool TakeMonitorTopologyChanged();

    bool TryGetNativeWindowBinding(WindowHandle window, out PlatformNativeWindowBinding binding);

    ClipboardWriteResponse WriteClipboardText(ClipboardWriteRequest request);
}
