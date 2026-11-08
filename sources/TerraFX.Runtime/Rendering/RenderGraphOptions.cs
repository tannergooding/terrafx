// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

/// <summary>Contains the immutable snapshot and selected output target for one render graph.</summary>
public sealed class RenderGraphOptions
{
    /// <summary>Initializes a graph bound to one offscreen snapshot and target.</summary>
    /// <param name="snapshotId">The nonempty render snapshot identity.</param>
    /// <param name="worldGuid">The nonempty source World identity.</param>
    /// <param name="tickIndex">The nonnegative committed tick.</param>
    /// <param name="frameDimension">The snapshot's spatial dimension.</param>
    /// <param name="profileId">The snapshot's spatial profile identity.</param>
    /// <param name="frameRevision">The positive snapshot frame revision.</param>
    /// <param name="cameraIds">The initialized, nonempty, unique camera selection.</param>
    /// <param name="targetExtent">The positive physical target extent.</param>
    /// <param name="offscreenColorTarget">The same-device 2D color-attachment target.</param>
    /// <exception cref="ArgumentException">An identity array or target does not match the selected graph contract.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="offscreenColorTarget"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An identity, revision, or extent is invalid.</exception>
    public RenderGraphOptions(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        RenderFrameDimension frameDimension,
        SpatialProfileId profileId,
        long frameRevision,
        ImmutableArray<Guid> cameraIds,
        PixelSize targetExtent,
        GpuTexture offscreenColorTarget)
        : this(
            snapshotId,
            worldGuid,
            tickIndex,
            frameDimension,
            profileId,
            frameRevision,
            cameraIds,
            targetExtent,
            surface: null,
            offscreenColorTarget: offscreenColorTarget,
            isSurfaceTarget: false)
    {
    }

    /// <summary>Initializes a graph bound to one interactive Surface.</summary>
    /// <param name="snapshotId">The nonempty render snapshot identity.</param>
    /// <param name="worldGuid">The nonempty source World identity.</param>
    /// <param name="tickIndex">The nonnegative committed tick.</param>
    /// <param name="frameDimension">The snapshot's spatial dimension.</param>
    /// <param name="profileId">The snapshot's spatial profile identity.</param>
    /// <param name="frameRevision">The positive snapshot frame revision.</param>
    /// <param name="cameraIds">The initialized, nonempty, unique camera selection.</param>
    /// <param name="targetExtent">The positive physical target extent.</param>
    /// <param name="surface">The live same-device interactive Surface.</param>
    public RenderGraphOptions(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        RenderFrameDimension frameDimension,
        SpatialProfileId profileId,
        long frameRevision,
        ImmutableArray<Guid> cameraIds,
        PixelSize targetExtent,
        Surface surface)
        : this(
            snapshotId,
            worldGuid,
            tickIndex,
            frameDimension,
            profileId,
            frameRevision,
            cameraIds,
            targetExtent,
            surface,
            offscreenColorTarget: null,
            isSurfaceTarget: true)
    {
    }

    private RenderGraphOptions(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        RenderFrameDimension frameDimension,
        SpatialProfileId profileId,
        long frameRevision,
        ImmutableArray<Guid> cameraIds,
        PixelSize targetExtent,
        Surface? surface,
        GpuTexture? offscreenColorTarget,
        bool isSurfaceTarget)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex);

        if (!Enum.IsDefined(frameDimension))
        {
            throw new ArgumentOutOfRangeException(nameof(frameDimension));
        }

        if (!profileId.IsValid)
        {
            throw new ArgumentException("A render graph requires a valid spatial profile identity.", nameof(profileId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRevision);

        if (cameraIds.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A render graph requires an initialized, nonempty camera selection.", nameof(cameraIds));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetExtent.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetExtent.Height);

        if (isSurfaceTarget)
        {
            ArgumentNullException.ThrowIfNull(surface);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(offscreenColorTarget);
        }

        if ((surface is null) == (offscreenColorTarget is null))
        {
            throw new ArgumentException("A render graph must select exactly one Surface or offscreen color target.");
        }

        var uniqueCameraIds = new HashSet<Guid>();

        foreach (var cameraId in cameraIds)
        {
            if ((cameraId == Guid.Empty) || !uniqueCameraIds.Add(cameraId))
            {
                throw new ArgumentException("Camera identities must be nonempty and unique.", nameof(cameraIds));
            }
        }

        uint deviceEpoch;
        uint surfaceEpoch;
        uint swapchainEpoch;

        if (surface is not null)
        {
            ObjectDisposedException.ThrowIf(!surface.IsLive, surface);
            var surfaceSnapshot = surface.CaptureSnapshot();

            if (surface.Device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The Surface GraphicsDevice is not ready.");
            }

            if (surface.Device.Capabilities.Profile != GraphicsDeviceProfile.Interactive)
            {
                throw new ArgumentException("A Surface graph requires an Interactive GraphicsDevice.", nameof(surface));
            }

            if ((surfaceSnapshot.ClientPixelSize != targetExtent)
                || (surfaceSnapshot.Capabilities.SwapchainEpoch == 0))
            {
                throw new InvalidOperationException("A Surface graph requires the matching active drawable extent.");
            }

            deviceEpoch = surface.Device.DeviceEpoch;
            surfaceEpoch = surfaceSnapshot.Capabilities.SurfaceEpoch;
            swapchainEpoch = surfaceSnapshot.Capabilities.SwapchainEpoch;
        }
        else
        {
            ArgumentNullException.ThrowIfNull(offscreenColorTarget);
            var targetDescription = offscreenColorTarget.Description;

            if ((targetDescription.Dimension != TextureDimension.Dimension2D)
                || (targetDescription.Depth != 1)
                || (targetDescription.Width != targetExtent.Width)
                || (targetDescription.Height != targetExtent.Height)
                || (targetDescription.Format == TextureFormat.D32Float)
                || ((targetDescription.Usage & TextureUsage.ColorAttachment) == 0))
            {
                throw new ArgumentException("The offscreen target must be a matching 2D color-attachment texture.", nameof(offscreenColorTarget));
            }

            deviceEpoch = offscreenColorTarget.DeviceEpoch;
            surfaceEpoch = 0;
            swapchainEpoch = 0;
        }

        CameraIds = [.. cameraIds];
        DeviceEpoch = deviceEpoch;
        FrameDimension = frameDimension;
        FrameRevision = frameRevision;
        Surface = surface;
        OffscreenColorTarget = offscreenColorTarget;
        SurfaceEpoch = surfaceEpoch;
        SwapchainEpoch = swapchainEpoch;
        ProfileId = profileId;
        SnapshotId = snapshotId;
        TargetExtent = targetExtent;
        TickIndex = tickIndex;
        WorldGuid = worldGuid;
    }

    /// <summary>Gets the selected camera identities in committed order.</summary>
    public ImmutableArray<Guid> CameraIds { get; }

    /// <summary>Gets the snapshot's spatial dimension.</summary>
    public RenderFrameDimension FrameDimension { get; }

    /// <summary>Gets the snapshot's frame revision.</summary>
    public long FrameRevision { get; }

    /// <summary>Gets the selected offscreen color target, or null for a Surface graph.</summary>
    public GpuTexture? OffscreenColorTarget { get; }

    /// <summary>Gets the snapshot's spatial profile identity.</summary>
    public SpatialProfileId ProfileId { get; }

    /// <summary>Gets the render snapshot identity.</summary>
    public Guid SnapshotId { get; }

    /// <summary>Gets the selected interactive Surface, or null for an offscreen graph.</summary>
    public Surface? Surface { get; }

    /// <summary>Gets the positive physical target extent.</summary>
    public PixelSize TargetExtent { get; }

    /// <summary>Gets the snapshot's committed tick.</summary>
    public long TickIndex { get; }

    /// <summary>Gets the source World identity.</summary>
    public Guid WorldGuid { get; }

    internal uint DeviceEpoch { get; }

    internal uint SurfaceEpoch { get; }

    internal uint SwapchainEpoch { get; }
}
