// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using TerraFX;

namespace TerraFX.Rendering;

/// <summary>Contains copied immutable render inputs from one committed World generation.</summary>
public abstract class RenderSnapshot : IDisposable
{
    private readonly ImmutableArray<Guid> _cameraIds;
    private readonly ImmutableArray<CameraSnapshot> _cameras;
    private int _isDisposed;

    internal RenderSnapshot(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        ImmutableArray<Guid> cameraIds,
        ImmutableArray<CameraSnapshot> cameras,
        PixelSize targetExtent)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex);

        if (cameraIds.IsDefault || cameras.IsDefault)
        {
            throw new ArgumentException("Render snapshot camera arrays must be initialized.");
        }

        if (cameraIds.Length != cameras.Length)
        {
            throw new ArgumentException("The render snapshot camera IDs and camera values must have equal cardinality.");
        }

        RenderExtractionValidation.ValidateTargetExtent(targetExtent, nameof(targetExtent));
        var uniqueCameraIds = new HashSet<Guid>();

        for (var index = 0; index < cameraIds.Length; index++)
        {
            var cameraId = cameraIds[index];
            var camera = cameras[index];

            if (cameraId == Guid.Empty || !uniqueCameraIds.Add(cameraId))
            {
                throw new ArgumentException("The render snapshot camera IDs must be nonempty and unique.", nameof(cameraIds));
            }

            if ((camera.CameraId != cameraId)
                || (camera.SnapshotId != snapshotId)
                || (camera.WorldGuid != worldGuid)
                || (camera.TargetExtent != targetExtent))
            {
                throw new ArgumentException("The render snapshot camera values do not match its captured identity and target.");
            }
        }

        SnapshotId = snapshotId;
        WorldGuid = worldGuid;
        TickIndex = tickIndex;
        _cameraIds = [.. cameraIds];
        _cameras = [.. cameras];
        TargetExtent = targetExtent;
    }

    /// <summary>Gets the copied camera identities in committed camera order.</summary>
    public ImmutableArray<Guid> CameraIds => _cameraIds;

    /// <summary>Gets the copied camera values in committed camera order.</summary>
    public ImmutableArray<CameraSnapshot> Cameras => _cameras;

    /// <summary>Gets the spatial dimension represented by this snapshot.</summary>
    public abstract RenderFrameDimension FrameDimension { get; }

    /// <summary>Gets the exact spatial frame revision represented by this snapshot.</summary>
    public abstract long FrameRevision { get; }

    /// <summary>Gets the exact spatial profile represented by this snapshot.</summary>
    public abstract SpatialProfileId ProfileId { get; }

    /// <summary>Gets the identity of this immutable snapshot.</summary>
    public Guid SnapshotId { get; }

    /// <summary>Gets the physical target extent.</summary>
    public PixelSize TargetExtent { get; }

    /// <summary>Gets the completed tick index represented by this snapshot.</summary>
    public long TickIndex { get; }

    /// <summary>Gets the persistent identity of the source World.</summary>
    public Guid WorldGuid { get; }

    internal bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    /// <summary>Releases snapshot-owned CPU asset leases while preserving its copied values.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed snapshot ownership.</summary>
    /// <param name="disposing">Whether managed ownership should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ = Interlocked.Exchange(ref _isDisposed, 1);
        }
    }
}
