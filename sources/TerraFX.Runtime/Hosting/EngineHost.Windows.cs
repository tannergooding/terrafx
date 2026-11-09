// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Platform;

namespace TerraFX.Hosting;

public sealed partial class EngineHost
{
    private readonly Dictionary<WindowHandle, Surface> _surfacesByWindow = [];

    private static Diagnostic CreateMonitorUnavailableDiagnostic(uint hostId, MonitorHandle monitor)
        => new(
            "MonitorUnavailable",
            DiagnosticSeverity.Warning,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            hostId,
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
            operation: "EngineHost.CreateWindowAsync",
            nativeResult: null,
            message: "The requested monitor became unavailable after the window request was admitted.",
            context: [
                new DiagnosticContextEntry("MonitorIndex", monitor.Index.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("MonitorGeneration", monitor.Generation.ToString(CultureInfo.InvariantCulture)),
            ]);

    /// <summary>Creates a native window and its graphics Surface before reporting success.</summary>
    /// <param name="options">The validated native window preferences.</param>
    /// <param name="cancellationToken">A token that cancels the command before owner-thread application.</param>
    /// <returns>The created WindowHandle, or a rejected result with its diagnostic.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="options"/> contains a foreign monitor handle.</exception>
    /// <exception cref="InvalidOperationException">The host is stopping or an owner request cannot be admitted.</exception>
    /// <exception cref="NotSupportedException">The host profile does not create native windows or the selected backend lacks WSI.</exception>
    public Task<WindowCreationReceipt> CreateWindowAsync(
        WindowOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.Profile != HostProfile.Interactive)
        {
            return Task.FromException<WindowCreationReceipt>(
                new NotSupportedException("Headless and OffscreenNative hosts do not create windows."));
        }

        ThrowIfStopping();
        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");

        if (options.Monitor is MonitorHandle monitor
            && (!monitor.IsValid || (monitor.OwnerId != platformOwner.OwnerId)))
        {
            throw new ArgumentException("The monitor handle belongs to another platform owner.", nameof(options));
        }

        return InvokeOwnerRequestAsync(
            _ => CreateWindowOnOwner(options),
            cancellationToken);
    }

    /// <summary>Gets the host-owned Surface for one live Interactive window.</summary>
    /// <param name="window">The same-host live window handle.</param>
    /// <returns>The same Surface object established by successful window creation.</returns>
    /// <exception cref="ArgumentException"><paramref name="window"/> is invalid or belongs to another host.</exception>
    /// <exception cref="InvalidOperationException">The call is off the host owner thread or the window has no live Surface.</exception>
    public Surface GetSurface(WindowHandle window)
    {
        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");

        if (!window.IsValid || (window.OwnerId != platformOwner.OwnerId))
        {
            throw new ArgumentException("The window handle belongs to another host.", nameof(window));
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("EngineHost.GetSurface is restricted to the host owner thread.");
        }

        if (_options.Profile != HostProfile.Interactive
            || !_surfacesByWindow.TryGetValue(window, out var surface)
            || !surface.IsLive)
        {
            throw new InvalidOperationException("The live window has no Interactive Surface.");
        }

        return surface;
    }

    private WindowCommandReceipt CloseWindowOnOwner(WindowHandle window)
    {
        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");

        if (_surfacesByWindow.TryGetValue(window, out var surface))
        {
            if (surface.IsQuarantined)
            {
                throw new InvalidOperationException("The native Surface is quarantined; its window cannot be closed safely.");
            }

            var graphicsDevice = _graphicsDevice
                ?? throw new InvalidOperationException("The Interactive host has no GraphicsDevice.");
            graphicsDevice.RetireSurfaceAsync(surface).GetAwaiter().GetResult();
            _ = _surfacesByWindow.Remove(window);
        }

        var inputRoutingOwner = _platformInputRouting
            ?? throw new InvalidOperationException("The EngineHost platform input routing owner is not initialized.");
        inputRoutingOwner.CloseWindow(window);
        return platformOwner.CloseWindowCommand(window);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed window-to-Surface creation must retire either owner and preserve native cleanup failures.")]
    private WindowCreationReceipt CreateWindowOnOwner(WindowOptions options)
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Native windows and their Surface mappings are created on the host owner thread.");
        }

        if (_options.Profile != HostProfile.Interactive)
        {
            throw new NotSupportedException("Headless and OffscreenNative hosts do not create windows.");
        }

        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");
        var graphicsDevice = _graphicsDevice
            ?? throw new InvalidOperationException("Interactive startup has no admitted GraphicsDevice.");
        WindowHandle window;

        try
        {
            window = platformOwner.CreateWindow(options);
        }
        catch (PlatformMonitorUnavailableException) when (options.Monitor is MonitorHandle requestedMonitor)
        {
            return new WindowCreationReceipt(
                WindowCreationStatus.Rejected,
                window: null,
                rejection: CreateMonitorUnavailableDiagnostic(platformOwner.OwnerId, requestedMonitor));
        }

        Surface? surface = null;
        var surfaceRetirementFailed = false;

        try
        {
            var clientPixelSize = platformOwner.Platform.GetMetrics(window).ClientPixelSize;
            var binding = platformOwner.GetGraphicsNativeSurfaceBinding(window, surfaceEpoch: 1);
            surface = graphicsDevice.CreateSurfaceAsync(
                binding,
                clientPixelSize,
                CancellationToken.None).GetAwaiter().GetResult();
            _surfacesByWindow.Add(window, surface);
            platformOwner.SetWindowSurfaceEpochs(window, surface.SurfaceEpoch, surface.SwapchainEpoch);
            return new WindowCreationReceipt(WindowCreationStatus.Created, window, rejection: null);
        }
        catch (Exception creationException)
        {
            var failures = new List<Exception> { creationException };

            if (surface is not null)
            {
                try
                {
                    graphicsDevice.RetireSurfaceAsync(surface).GetAwaiter().GetResult();
                    _ = _surfacesByWindow.Remove(window);
                }
                catch (Exception retirementException)
                {
                    failures.Add(retirementException);
                    _surfacesByWindow[window] = surface;
                    surfaceRetirementFailed = true;
                }
            }

            if (!surfaceRetirementFailed)
            {
                try
                {
                    platformOwner.CloseWindow(window);
                }
                catch (Exception cleanupException)
                {
                    failures.Add(cleanupException);
                }
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("Interactive window creation and rollback both failed.", failures);
            }

            throw;
        }
    }

    private void RefreshWindowSurfaceMetricsOnOwner()
    {
        if (_surfacesByWindow.Count == 0)
        {
            return;
        }

        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The EngineHost platform owner is not initialized.");

        foreach (var (window, surface) in _surfacesByWindow)
        {
            if (!surface.IsLive)
            {
                continue;
            }

            var observedPixelSize = platformOwner.Platform.GetMetrics(window).ClientPixelSize;

            if (observedPixelSize != surface.ClientPixelSize)
            {
                _ = surface.ResizeAsync(observedPixelSize, CancellationToken.None).GetAwaiter().GetResult();
            }

            platformOwner.SetWindowSurfaceEpochs(window, surface.SurfaceEpoch, surface.SwapchainEpoch);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Shutdown keeps the native window owner alive if any Surface cannot prove safe retirement.")]
    private void RetireSurfacesBeforePlatformShutdown()
    {
        if (_surfacesByWindow.Count == 0)
        {
            return;
        }

        var graphicsDevice = _graphicsDevice
            ?? throw new InvalidOperationException("The Surface map has no GraphicsDevice owner.");
        var failures = new List<Exception>();

        foreach (var (window, surface) in _surfacesByWindow.ToArray())
        {
            try
            {
                graphicsDevice.RetireSurfaceAsync(surface).GetAwaiter().GetResult();
                _ = _surfacesByWindow.Remove(window);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count != 0)
        {
            throw new AggregateException("One or more native Surfaces could not be retired before Platform shutdown.", failures);
        }
    }
}
