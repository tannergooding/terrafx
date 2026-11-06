// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

/// <summary>Contains the immutable snapshot and offscreen target identities for one render graph.</summary>
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
        ArgumentNullException.ThrowIfNull(offscreenColorTarget);

        var uniqueCameraIds = new HashSet<Guid>();

        foreach (var cameraId in cameraIds)
        {
            if ((cameraId == Guid.Empty) || !uniqueCameraIds.Add(cameraId))
            {
                throw new ArgumentException("Camera identities must be nonempty and unique.", nameof(cameraIds));
            }
        }

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

        CameraIds = [.. cameraIds];
        FrameDimension = frameDimension;
        FrameRevision = frameRevision;
        OffscreenColorTarget = offscreenColorTarget;
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

    /// <summary>Gets the offscreen color target.</summary>
    public GpuTexture OffscreenColorTarget { get; }

    /// <summary>Gets the snapshot's spatial profile identity.</summary>
    public SpatialProfileId ProfileId { get; }

    /// <summary>Gets the render snapshot identity.</summary>
    public Guid SnapshotId { get; }

    /// <summary>Gets the positive physical target extent.</summary>
    public PixelSize TargetExtent { get; }

    /// <summary>Gets the snapshot's committed tick.</summary>
    public long TickIndex { get; }

    /// <summary>Gets the source World identity.</summary>
    public Guid WorldGuid { get; }
}
