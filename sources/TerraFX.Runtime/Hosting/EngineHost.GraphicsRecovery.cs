// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Rendering;

namespace TerraFX.Hosting;

public sealed partial class EngineHost
{
    private bool _graphicsRecoveryBlocked;
    private bool _graphicsRecoveryPending;
    private readonly HashSet<GraphicsDevice> _graphicsShutdownAttempts = [];

    /// <summary>Explicitly replaces a lost graphics device while presentation is suspended.</summary>
    /// <param name="cancellationToken">The token that cancels admission before the owner callback executes.</param>
    /// <returns>A task completed after the replacement device and all live window surfaces are published.</returns>
    /// <exception cref="InvalidOperationException">The host is not using <see cref="GraphicsFailurePolicy.SuspendPresentation"/>, the selected device is not lost, or recovery has been blocked by uncertain native ownership.</exception>
    /// <exception cref="NotSupportedException">The host profile has no native graphics device to recover.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before the owner callback started.</exception>
    public Task RecoverGraphicsAsync(CancellationToken cancellationToken = default)
        => InvokeAsync(_ => RecoverGraphicsOnOwner(), cancellationToken);

    private void HandleGraphicsDeviceLost(GraphicsDevice graphicsDevice, int? nativeResult)
    {
        var policy = _options.GraphicsFailurePolicy;
        var diagnostic = new Diagnostic(
            "graphics-device-lost",
            DiagnosticSeverity.Error,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            hostId: Volatile.Read(ref _platformOwner)?.OwnerId,
            worldGuid: null,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: graphicsDevice.DeviceEpoch,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "GraphicsDevice",
            nativeResult,
            message: policy is GraphicsFailurePolicy.StopHost
                ? "The graphics device was lost; the host is stopping."
                : "The graphics device was lost; simulation and audio continue while presentation is suspended.",
            context: [
                new DiagnosticContextEntry("AdapterName", graphicsDevice.Capabilities.AdapterName),
                new DiagnosticContextEntry("GraphicsFailurePolicy", policy.ToString()),
            ]);
        Volatile.Read(ref _diagnostics)?.Write(diagnostic, executionSessionId: null, worldOwnerId: null);

        if (policy is GraphicsFailurePolicy.StopHost)
        {
            var stopTask = StopAsync();
            _ = stopTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void PumpPlatformDuringGraphicsWait()
    {
        var platformOwner = _platformOwner
            ?? throw new InvalidOperationException("The Interactive platform owner is not initialized.");
        var inputRoutingOwner = _platformInputRouting;
        inputRoutingOwner?.SynchronizeTextInputSessions();
        var capturedInput = platformOwner.Pump();

        if (!capturedInput.Diagnostics.IsEmpty)
        {
            var diagnostics = _diagnostics
                ?? throw new InvalidOperationException("The EngineHost diagnostics service is not initialized.");

            foreach (var diagnostic in capturedInput.Diagnostics)
            {
                diagnostics.Write(diagnostic, executionSessionId: null, worldOwnerId: null);
            }
        }

        inputRoutingOwner?.RouteCapturedInput(capturedInput);

        if (inputRoutingOwner is not null)
        {
            var hostTimestamp = platformOwner.GetHostTimestamp();
            inputRoutingOwner.ServiceWindowInputRoutes(hostTimestamp);
        }

        var modeDiagnostics = platformOwner.DrainModeDiagnostics();

        if (!modeDiagnostics.IsEmpty)
        {
            var diagnostics = _diagnostics
                ?? throw new InvalidOperationException("The EngineHost diagnostics service is not initialized.");

            foreach (var diagnostic in modeDiagnostics)
            {
                diagnostics.Write(diagnostic, executionSessionId: null, worldOwnerId: null);
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Recovery rollback must attempt to retire every replacement Surface and device and preserve cleanup failures.")]
    private void RecoverGraphicsOnOwner()
    {
        EnsureOwnerThread();

        if (_options.Profile is not (HostProfile.OffscreenNative or HostProfile.Interactive))
        {
            throw new NotSupportedException("The selected host profile has no native GraphicsDevice to recover.");
        }

        if (_options.GraphicsFailurePolicy is not GraphicsFailurePolicy.SuspendPresentation)
        {
            throw new InvalidOperationException("Graphics recovery requires the SuspendPresentation failure policy.");
        }

        if (_graphicsRecoveryBlocked)
        {
            throw new InvalidOperationException("Graphics recovery is blocked because native ownership could not be retired safely.");
        }

        var oldDevice = _graphicsDevice
            ?? throw new InvalidOperationException("The graphics-enabled host has no GraphicsDevice.");
        var oldStatus = oldDevice.Status;

        if ((oldStatus is not GraphicsDeviceStatus.Lost)
            && !(_graphicsRecoveryPending && (oldStatus is GraphicsDeviceStatus.Disposed)))
        {
            throw new InvalidOperationException("Graphics recovery requires a lost device or a retryable failed replacement.");
        }

        if (oldDevice.DeviceEpoch == uint.MaxValue)
        {
            throw new InvalidOperationException("The GraphicsDevice exhausted its device epochs and cannot be replaced.");
        }

        var surfaceEntries = _surfacesByWindow.ToArray();
        var replacementSurfaceEpochs = new Dictionary<WindowHandle, uint>(surfaceEntries.Length);

        foreach (var (window, surface) in surfaceEntries)
        {
            if (surface.IsQuarantined)
            {
                _graphicsRecoveryBlocked = true;
                throw new InvalidOperationException("A quarantined Surface prevents graphics recovery.");
            }

            if (surface.SurfaceEpoch == uint.MaxValue)
            {
                throw new InvalidOperationException("A Surface exhausted its epochs and cannot be replaced.");
            }

            replacementSurfaceEpochs.Add(window, surface.SurfaceEpoch + 1);
        }

        _graphicsRecoveryPending = true;
        var oldResidency = _gpuResidency;

        if (oldResidency is not null)
        {
            WaitForGraphicsTask(oldResidency.StopAsync());
        }

        foreach (var (_, surface) in surfaceEntries)
        {
            if (!surface.IsRetired)
            {
                try
                {
                    WaitForGraphicsTask(oldDevice.RetireSurfaceAsync(surface));
                }
                catch
                {
                    _graphicsRecoveryBlocked |= surface.IsQuarantined;
                    throw;
                }
            }
        }

        try
        {
            ShutdownGraphicsDeviceOnOwner(oldDevice);
        }
        catch
        {
            _graphicsRecoveryBlocked = true;
            throw;
        }

        var registration = _options.GraphicsBackendRegistration
            ?? throw new InvalidOperationException("The graphics-enabled host has no selected backend registration.");
        var graphicsProfile = (_options.Profile is HostProfile.Interactive)
            ? GraphicsDeviceProfile.Interactive
            : GraphicsDeviceProfile.OffscreenNative;
        var platformOwner = (_options.Profile is HostProfile.Interactive)
            ? _platformOwner ?? throw new InvalidOperationException("The Interactive platform owner is not initialized.")
            : null;
        var platformKind = platformOwner?.GraphicsPlatformKind ?? GraphicsPlatformKind.None;
        GraphicsNativeSurfaceBinding? presentationProbeBinding = null;
        WindowHandle? presentationProbeWindow = null;
        GraphicsDevice? replacementDevice = null;
        var replacementSurfaces = new List<KeyValuePair<WindowHandle, Surface>>(surfaceEntries.Length);
        var changedWindowEpochs = new List<(WindowHandle Window, uint SurfaceEpoch, uint SwapchainEpoch)>(surfaceEntries.Length);

        try
        {
            if ((platformOwner is not null) && (registration.Backend is GraphicsBackend.Vulkan))
            {
                if (surfaceEntries.Length > 0)
                {
                    var probeWindow = surfaceEntries[0].Key;
                    presentationProbeBinding = platformOwner.GetGraphicsNativeSurfaceBinding(
                        probeWindow,
                        replacementSurfaceEpochs[probeWindow]);
                }
                else
                {
                    presentationProbeWindow = platformOwner.CreateWindow(new WindowOptions("Vulkan surface capability probe") {
                        ClientSize = new Vector2(64.0f, 64.0f),
                        Visible = false,
                    });
                    presentationProbeBinding = platformOwner.GetGraphicsNativeSurfaceBinding(
                        presentationProbeWindow.Value,
                        surfaceEpoch: 1);
                }
            }

            replacementDevice = GraphicsDevice.CreateAsync(
                oldDevice.OwnerId,
                registration,
                graphicsProfile,
                _options.GraphicsFrameSlotCount,
                platformKind,
                presentationProbeBinding,
                oldDevice.DeviceEpoch + 1,
                CancellationToken.None).GetAwaiter().GetResult();

            if (presentationProbeWindow is WindowHandle probeWindowToClose)
            {
                platformOwner?.CloseWindow(probeWindowToClose);
                presentationProbeWindow = null;
            }

            foreach (var (window, _) in surfaceEntries)
            {
                var clientPixelSize = platformOwner!.Platform.GetMetrics(window).ClientPixelSize;
                var binding = platformOwner.GetGraphicsNativeSurfaceBinding(
                    window,
                    replacementSurfaceEpochs[window]);
                var surface = replacementDevice.CreateSurfaceAsync(
                    binding,
                    clientPixelSize,
                    CancellationToken.None).GetAwaiter().GetResult();
                replacementSurfaces.Add(new KeyValuePair<WindowHandle, Surface>(window, surface));
            }

            if (replacementDevice.Status is not GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The replacement GraphicsDevice was lost before it could be published.");
            }

            var replacementResidency = new GpuResidencyService(replacementDevice);
            var replacementRenderer = Renderer.Create(replacementDevice, replacementResidency);
            var publishedDevice = replacementDevice;
            replacementDevice.SetDeviceLostCallback(
                nativeResult => HandleGraphicsDeviceLost(publishedDevice, nativeResult));

            if (replacementDevice.Status is not GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The replacement GraphicsDevice was lost before recovery completed.");
            }

            if (platformOwner is not null)
            {
                foreach (var (window, surface) in replacementSurfaces)
                {
                    var previousSurface = _surfacesByWindow[window];
                    changedWindowEpochs.Add((window, previousSurface.SurfaceEpoch, previousSurface.SwapchainEpoch));
                    platformOwner.SetWindowSurfaceEpochs(window, surface.SurfaceEpoch, surface.SwapchainEpoch);
                }
            }

            foreach (var (window, surface) in replacementSurfaces)
            {
                _surfacesByWindow[window] = surface;
            }

            _graphicsDevice = replacementDevice;
            _gpuResidency = replacementResidency;
            _renderer = replacementRenderer;
            _graphicsRecoveryPending = false;
            _ = _graphicsShutdownAttempts.Remove(oldDevice);
            replacementDevice = null;
        }
        catch (Exception recoveryException)
        {
            var cleanupFailures = new List<Exception>();

            if (platformOwner is not null)
            {
                for (var index = changedWindowEpochs.Count - 1; index >= 0; index--)
                {
                    var (window, surfaceEpoch, swapchainEpoch) = changedWindowEpochs[index];

                    try
                    {
                        platformOwner.SetWindowSurfaceEpochs(
                            window,
                            surfaceEpoch,
                            swapchainEpoch);
                    }
                    catch (Exception cleanupException)
                    {
                        cleanupFailures.Add(cleanupException);
                    }
                }
            }

            if (replacementDevice is not null)
            {
                for (var index = replacementSurfaces.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        WaitForGraphicsTask(replacementDevice.RetireSurfaceAsync(replacementSurfaces[index].Value));
                    }
                    catch (Exception cleanupException)
                    {
                        cleanupFailures.Add(cleanupException);
                    }
                }

                try
                {
                    ShutdownGraphicsDeviceOnOwner(replacementDevice);
                    _ = _graphicsShutdownAttempts.Remove(replacementDevice);
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }
            }

            if (presentationProbeWindow is WindowHandle probeWindowToClose)
            {
                try
                {
                    platformOwner?.CloseWindow(probeWindowToClose);
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }
            }

            if (recoveryException is AggregateException)
            {
                _graphicsRecoveryBlocked = true;
            }

            if (cleanupFailures.Count != 0)
            {
                _graphicsRecoveryBlocked = true;
                cleanupFailures.Insert(0, recoveryException);
                throw new AggregateException("Graphics recovery and rollback both failed.", cleanupFailures);
            }

            throw;
        }
    }

    private void ShutdownGraphicsDeviceOnOwner(GraphicsDevice device)
    {
        if (_graphicsShutdownAttempts.Add(device))
        {
            WaitForGraphicsTask(device.ShutdownAsync());
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The owner must keep awaiting native graphics completion after Platform pumping fails, then report both failures.")]
    private void WaitForGraphicsTask(Task task)
    {
        if ((_options.Profile is not HostProfile.Interactive) || (_platformOwner is null))
        {
            task.GetAwaiter().GetResult();
            return;
        }

        Exception? platformFailure = null;

        while (!task.IsCompleted)
        {
            if (platformFailure is null)
            {
                try
                {
                    PumpPlatformDuringGraphicsWait();
                }
                catch (Exception exception)
                {
                    platformFailure = exception;
                }
            }

            Thread.Sleep(OwnerPumpIntervalMilliseconds);
        }

        if (platformFailure is not null)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception graphicsFailure)
            {
                throw new AggregateException(
                    "Graphics completion and Platform pumping both failed.",
                    platformFailure,
                    graphicsFailure);
            }

            ExceptionDispatchInfo.Capture(platformFailure).Throw();
        }

        task.GetAwaiter().GetResult();
    }
}
