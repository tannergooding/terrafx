// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace TerraFX.Platform;

internal static class PlatformBackendFactory
{
    internal static IPlatformBackend Create(
        PlatformBackend backend,
        uint ownerId,
        ClipboardServicePolicy clipboardPolicy,
        TimeProvider? timeProvider = null)
        => Create(backend, ownerId, clipboardPolicy, out _, timeProvider);

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The backend returned to the caller transfers disposal ownership to PlatformHost.")]
    internal static IPlatformBackend Create(
        PlatformBackend backend,
        uint ownerId,
        ClipboardServicePolicy clipboardPolicy,
        out Diagnostic? startupDiagnostic,
        TimeProvider? timeProvider = null)
    {
        startupDiagnostic = null;
        return backend switch {
            PlatformBackend.Headless => new HeadlessPlatformBackend(ownerId, clipboardPolicy),
            PlatformBackend.WindowsNative when OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                => new WindowsPlatformBackend(ownerId, clipboardPolicy, timeProvider),
            PlatformBackend.WindowsNative
                => throw new PlatformNotSupportedException("The Windows Platform backend requires Windows 11."),
            PlatformBackend.WaylandThenX11 when OperatingSystem.IsLinux()
                => CreateWaylandThenX11(ownerId, clipboardPolicy, out startupDiagnostic, timeProvider),
            PlatformBackend.WaylandThenX11
                => throw new PlatformNotSupportedException("The WaylandThenX11 Platform backend requires Linux."),
            PlatformBackend.WaylandOnly when OperatingSystem.IsLinux()
                => CreateWayland(ownerId, clipboardPolicy),
            PlatformBackend.X11Only when OperatingSystem.IsLinux()
                => new X11PlatformBackend(ownerId, clipboardPolicy),
            PlatformBackend.X11Only
                => throw new PlatformNotSupportedException("The X11 Platform backend requires Linux."),
            PlatformBackend.WaylandOnly
                => throw new PlatformNotSupportedException("The Wayland Platform backend requires Linux."),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
    }

    private static IPlatformBackend CreateWayland(uint ownerId, ClipboardServicePolicy clipboardPolicy)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegative((int)clipboardPolicy);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)clipboardPolicy, (int)ClipboardServicePolicy.Required);

        if (clipboardPolicy is ClipboardServicePolicy.Required)
        {
            throw new NotSupportedException("The Wayland backend does not provide clipboard transfers yet.");
        }

        using var connection = WaylandDisplayConnection.Connect();

        if (!connection.HasGlobal("wl_compositor") || !connection.HasGlobal("xdg_wm_base"))
        {
            throw new PlatformNotSupportedException("The Wayland compositor does not advertise the required wl_compositor and xdg_wm_base globals.");
        }

        throw new PlatformNotSupportedException(
            "The Wayland compositor connection succeeded, but the xdg-shell window backend is not implemented.");
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The selected X11 backend transfers disposal ownership to the caller.")]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed Wayland start must be retained when the explicitly requested X11 fallback is selected or also fails.")]
    private static IPlatformBackend CreateWaylandThenX11(
        uint ownerId,
        ClipboardServicePolicy clipboardPolicy,
        out Diagnostic? startupDiagnostic,
        TimeProvider? timeProvider)
    {
        startupDiagnostic = null;

        try
        {
            return CreateWayland(ownerId, clipboardPolicy);
        }
        catch (Exception waylandFailure) when (IsNativeStartupFailure(waylandFailure))
        {
            IPlatformBackend x11Backend;

            try
            {
                x11Backend = new X11PlatformBackend(ownerId, clipboardPolicy);
            }
            catch (Exception x11Failure) when (IsNativeStartupFailure(x11Failure))
            {
                throw new AggregateException(
                    "Wayland startup failed and the X11 fallback could not be initialized.",
                    waylandFailure,
                    x11Failure);
            }

            startupDiagnostic = new Diagnostic(
                "platform-wayland-fallback-x11",
                DiagnosticSeverity.Warning,
                (timeProvider ?? TimeProvider.System).GetTimestamp(),
                hostId: ownerId,
                worldGuid: null,
                deviceId: null,
                surfaceId: null,
                deviceEpoch: null,
                surfaceEpoch: null,
                swapchainEpoch: null,
                tick: null,
                systemId: null,
                passId: null,
                assetId: null,
                commandIdentity: null,
                operation: "PlatformBackend.WaylandThenX11",
                nativeResult: null,
                message: $"Wayland startup failed ({waylandFailure.GetType().Name}: {waylandFailure.Message}); selected the X11 backend.",
                context: []);
            return x11Backend;
        }
    }

    private static bool IsNativeStartupFailure(Exception exception)
        => exception is PlatformNotSupportedException
            or IOException
            or DllNotFoundException
            or EntryPointNotFoundException;
}
