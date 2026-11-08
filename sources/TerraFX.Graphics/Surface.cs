// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;

namespace TerraFX.Graphics;

/// <summary>Represents one host-owned native presentation surface.</summary>
public sealed class Surface
{
    private readonly IGraphicsBackendSurface _backendSurface;
    private readonly GraphicsDevice _device;
    private int _lifecycle;
    private readonly TaskCompletionSource _retirementCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly GraphicsOwnerRequestQueue.GraphicsRetirementReservation _retirementReservation;
    private SurfaceSnapshot _snapshot;

    internal Surface(
        GraphicsDevice device,
        IGraphicsBackendSurface backendSurface,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(backendSurface);
        ArgumentNullException.ThrowIfNull(retirementReservation);
        _device = device;
        _backendSurface = backendSurface;
        _retirementReservation = retirementReservation;
        _snapshot = CreateSnapshot(backendSurface);
    }

    /// <summary>Gets the immutable capabilities for the current surface/swapchain generation.</summary>
    public SurfaceCapabilities Capabilities
        => Volatile.Read(ref _snapshot).Capabilities;

    /// <summary>Gets the active physical client extent, or a zero extent while suspended.</summary>
    public PixelSize ClientPixelSize
        => Volatile.Read(ref _snapshot).ClientPixelSize;

    /// <summary>Gets the selected color format for the active surface.</summary>
    public TextureFormat ColorFormat
        => Volatile.Read(ref _snapshot).ColorFormat;

    /// <summary>Gets the native surface generation.</summary>
    public uint SurfaceEpoch
        => Volatile.Read(ref _snapshot).Capabilities.SurfaceEpoch;

    /// <summary>Gets the active swapchain generation, or zero while suspended.</summary>
    public uint SwapchainEpoch
        => Volatile.Read(ref _snapshot).Capabilities.SwapchainEpoch;

    internal IGraphicsBackendSurface BackendSurface => _backendSurface;

    internal GraphicsDevice Device => _device;

    internal bool IsLive => Volatile.Read(ref _lifecycle) == 0;

    internal bool IsQuarantined => Volatile.Read(ref _lifecycle) == 3;

    internal bool IsRetired => Volatile.Read(ref _lifecycle) == 2;

    internal GraphicsOwnerRequestQueue.GraphicsRetirementReservation RetirementReservation
        => _retirementReservation;

    internal Task RetirementTask => _retirementCompletion.Task;

    private static SurfaceSnapshot CreateSnapshot(IGraphicsBackendSurface backendSurface)
        => new(backendSurface.Capabilities, backendSurface.ClientPixelSize, backendSurface.ColorFormat);

    /// <summary>Requests a graphics-resource resize without resizing the native window.</summary>
    /// <param name="clientPixelSize">The observed physical drawable extent; a zero dimension suspends presentation.</param>
    /// <param name="cancellationToken">A token that cancels admission or the caller's wait.</param>
    /// <returns>The owner-applied resize status and resulting surface/swapchain epochs.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The extent exceeds the selected device's maximum texture dimension.</exception>
    /// <exception cref="InvalidOperationException">The surface is stale or the graphics device is stopping.</exception>
    public Task<SurfaceResizeReceipt> ResizeAsync(
        PixelSize clientPixelSize,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(!IsLive, this);
        return _device.ResizeSurfaceAsync(this, clientPixelSize, cancellationToken);
    }

    /// <summary>Waits for an independently observable presentation outcome.</summary>
    /// <param name="presentationSequence">The sequence returned by an interactive render submission.</param>
    /// <param name="cancellationToken">A token that cancels only this observation wait.</param>
    /// <returns>The terminal presentation status and the native generations that produced it.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="presentationSequence"/> is not positive.</exception>
    /// <exception cref="OperationCanceledException">The observation wait was canceled.</exception>
    public Task<PresentationReceipt> WaitForPresentationAsync(
        long presentationSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);
        cancellationToken.ThrowIfCancellationRequested();
        return _device.WaitForPresentationAsync(this, presentationSequence, cancellationToken);
    }

    internal SurfaceSnapshot CaptureSnapshot()
        => Volatile.Read(ref _snapshot);

    internal void MarkQuarantined(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Volatile.Write(ref _lifecycle, 3);
        _ = _retirementCompletion.TrySetException(exception);
    }

    internal void MarkRetired()
    {
        Volatile.Write(ref _lifecycle, 2);
        _ = _retirementCompletion.TrySetResult();
    }

    internal void PublishBackendState()
        => Volatile.Write(ref _snapshot, CreateSnapshot(_backendSurface));

    internal bool TryInvalidate()
        => Interlocked.CompareExchange(ref _lifecycle, 1, 0) == 0;

    internal sealed record SurfaceSnapshot(
        SurfaceCapabilities Capabilities,
        PixelSize ClientPixelSize,
        TextureFormat ColorFormat);
}
