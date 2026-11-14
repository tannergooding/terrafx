// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

/// <summary>Creates snapshot-bound render graphs for the selected renderer profile.</summary>
public sealed partial class Renderer
{
    /// <summary>Builds an offscreen graph for one exact committed render snapshot and target.</summary>
    /// <param name="snapshot">The immutable World-produced render snapshot.</param>
    /// <param name="offscreenColorTarget">The current same-device offscreen color target.</param>
    /// <returns>A graph bound to the exact snapshot and target identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="offscreenColorTarget"/> is null.</exception>
    /// <exception cref="ArgumentException">The target belongs to another device or has another extent.</exception>
    /// <exception cref="InvalidOperationException">The selected GraphicsDevice is not ready.</exception>
    /// <exception cref="NotSupportedException">This Renderer is Headless and has no native GraphicsDevice.</exception>
    /// <exception cref="ObjectDisposedException">The snapshot or target has been disposed.</exception>
    public RenderGraph BuildGraph(RenderSnapshot snapshot, GpuTexture offscreenColorTarget)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(offscreenColorTarget);

        if (_graphicsDevice is null)
        {
            throw new NotSupportedException("Headless rendering does not construct native render graphs.");
        }

        ObjectDisposedException.ThrowIf(snapshot.IsDisposed, snapshot);

        if (!ReferenceEquals(offscreenColorTarget.ResourceOwner.Device, _graphicsDevice))
        {
            throw new ArgumentException("The offscreen target belongs to another GraphicsDevice.", nameof(offscreenColorTarget));
        }

        if (offscreenColorTarget.DeviceEpoch != _graphicsDevice.DeviceEpoch)
        {
            throw new InvalidOperationException("The offscreen target belongs to a stale GraphicsDevice epoch.");
        }

        var graph = new RenderGraph(new RenderGraphOptions(
            snapshot.SnapshotId,
            snapshot.WorldGuid,
            snapshot.TickIndex,
            snapshot.FrameDimension,
            snapshot.ProfileId,
            snapshot.FrameRevision,
            snapshot.CameraIds,
            snapshot.TargetExtent,
            offscreenColorTarget));
        BuildSpriteC0Graph(snapshot, graph);
        BuildMeshC0Graph(snapshot, graph);
        return graph;
    }

    /// <summary>Builds an interactive graph for one exact committed render snapshot and Surface.</summary>
    /// <param name="snapshot">The immutable World-produced render snapshot.</param>
    /// <param name="surface">The live same-device interactive Surface.</param>
    /// <returns>A graph bound to the exact snapshot and Surface/swapchain generation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="surface"/> is null.</exception>
    /// <exception cref="ArgumentException">The Surface belongs to another device or has another extent.</exception>
    /// <exception cref="InvalidOperationException">The selected GraphicsDevice or Surface is not ready.</exception>
    /// <exception cref="NotSupportedException">This Renderer is Headless and has no native GraphicsDevice.</exception>
    /// <exception cref="ObjectDisposedException">The snapshot or Surface has been disposed or retired.</exception>
    public RenderGraph BuildGraph(RenderSnapshot snapshot, Surface surface)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(surface);

        if (_graphicsDevice is null)
        {
            throw new NotSupportedException("Headless rendering does not construct native render graphs.");
        }

        ObjectDisposedException.ThrowIf(snapshot.IsDisposed, snapshot);
        ObjectDisposedException.ThrowIf(!surface.IsLive, surface);

        if (!ReferenceEquals(surface.Device, _graphicsDevice))
        {
            throw new ArgumentException("The Surface belongs to another GraphicsDevice.", nameof(surface));
        }

        var graph = new RenderGraph(new RenderGraphOptions(
            snapshot.SnapshotId,
            snapshot.WorldGuid,
            snapshot.TickIndex,
            snapshot.FrameDimension,
            snapshot.ProfileId,
            snapshot.FrameRevision,
            snapshot.CameraIds,
            snapshot.TargetExtent,
            surface));
        BuildSpriteC0Graph(snapshot, graph);
        BuildMeshC0Graph(snapshot, graph);
        return graph;
    }
}
