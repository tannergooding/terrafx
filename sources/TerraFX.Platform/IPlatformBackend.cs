// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using TerraFX;
using TerraFX.Numerics;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

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

    bool RequiresClipboardGesture => false;

    void BeginReadClipboardText(
        ClipboardReadRequest request,
        Action<ClipboardReadResponse> complete,
        Action<Exception> fail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(complete);
        ArgumentNullException.ThrowIfNull(fail);
        BeginReadClipboardText(request, complete, cancellationToken);
    }

    void BeginReadClipboardText(
        ClipboardReadRequest request,
        Action<ClipboardReadResponse> complete,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(complete);

        if (cancellationToken.IsCancellationRequested)
        {
            complete(new ClipboardReadResponse(ClipboardTextResult.Canceled, text: null, diagnostic: null));
            return;
        }

        complete(ReadClipboardText(request));
    }

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
