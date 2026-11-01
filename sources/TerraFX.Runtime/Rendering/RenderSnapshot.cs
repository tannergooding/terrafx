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
    private readonly ImmutableArray<Guid> _directionalLightIds;
    private readonly ImmutableArray<RenderDirectionalLightSnapshot> _directionalLights;
    private int _isDisposed;
    private readonly ImmutableArray<Guid> _pointLightIds;
    private readonly ImmutableArray<RenderPointLightSnapshot> _pointLights;
    private readonly ImmutableArray<Guid> _spotLightIds;
    private readonly ImmutableArray<RenderSpotLightSnapshot> _spotLights;

    internal RenderSnapshot(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        ImmutableArray<Guid> cameraIds,
        ImmutableArray<CameraSnapshot> cameras,
        ImmutableArray<Guid> directionalLightIds,
        ImmutableArray<RenderDirectionalLightSnapshot> directionalLights,
        ImmutableArray<Guid> pointLightIds,
        ImmutableArray<RenderPointLightSnapshot> pointLights,
        ImmutableArray<Guid> spotLightIds,
        ImmutableArray<RenderSpotLightSnapshot> spotLights,
        PixelSize targetExtent)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex);

        if (cameraIds.IsDefault
            || cameras.IsDefault
            || directionalLightIds.IsDefault
            || directionalLights.IsDefault
            || pointLightIds.IsDefault
            || pointLights.IsDefault
            || spotLightIds.IsDefault
            || spotLights.IsDefault)
        {
            throw new ArgumentException("Render snapshot identity/value arrays must be initialized.");
        }

        if ((cameraIds.Length != cameras.Length)
            || (directionalLightIds.Length != directionalLights.Length)
            || (pointLightIds.Length != pointLights.Length)
            || (spotLightIds.Length != spotLights.Length))
        {
            throw new ArgumentException("Each render snapshot identity array must align with its value array.");
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

            if ((index > 0) && !RenderExtractionValidation.MatchesFrame(camera.Frame3, cameras[0].Frame3))
            {
                throw new ArgumentException("The render snapshot cameras must use one exact committed 3D frame.");
            }
        }

        var uniqueLightIds = new HashSet<Guid>();

        for (var index = 0; index < directionalLightIds.Length; index++)
        {
            var lightId = directionalLightIds[index];
            var light = directionalLights[index];

            if (lightId == Guid.Empty || !uniqueLightIds.Add(lightId))
            {
                throw new ArgumentException("The render snapshot light IDs must be nonempty and unique.", nameof(directionalLightIds));
            }

            if ((light.LightId != lightId)
                || (light.SnapshotId != snapshotId)
                || (light.WorldGuid != worldGuid)
                || cameras.IsEmpty
                || !RenderExtractionValidation.MatchesFrame(light.Frame3, cameras[0].Frame3))
            {
                throw new ArgumentException("The directional-light values do not match their captured identity and frame.");
            }

            if (index > 0 && FoundationCatalogue.CompareRfc(directionalLightIds[index - 1], lightId) >= 0)
            {
                throw new ArgumentException("The render snapshot directional lights must be in canonical light-ID order.", nameof(directionalLightIds));
            }
        }

        for (var index = 0; index < pointLightIds.Length; index++)
        {
            var lightId = pointLightIds[index];
            var light = pointLights[index];

            if (lightId == Guid.Empty || !uniqueLightIds.Add(lightId))
            {
                throw new ArgumentException("The render snapshot light IDs must be nonempty and unique.", nameof(pointLightIds));
            }

            if ((light.LightId != lightId)
                || (light.SnapshotId != snapshotId)
                || (light.WorldGuid != worldGuid)
                || cameras.IsEmpty
                || !RenderExtractionValidation.MatchesFrame(light.Frame3, cameras[0].Frame3))
            {
                throw new ArgumentException("The point-light values do not match their captured identity and frame.");
            }

            if (index > 0 && FoundationCatalogue.CompareRfc(pointLightIds[index - 1], lightId) >= 0)
            {
                throw new ArgumentException("The render snapshot point lights must be in canonical light-ID order.", nameof(pointLightIds));
            }
        }

        for (var index = 0; index < spotLightIds.Length; index++)
        {
            var lightId = spotLightIds[index];
            var light = spotLights[index];

            if (lightId == Guid.Empty || !uniqueLightIds.Add(lightId))
            {
                throw new ArgumentException("The render snapshot light IDs must be nonempty and unique.", nameof(spotLightIds));
            }

            if ((light.LightId != lightId)
                || (light.SnapshotId != snapshotId)
                || (light.WorldGuid != worldGuid)
                || cameras.IsEmpty
                || !RenderExtractionValidation.MatchesFrame(light.Frame3, cameras[0].Frame3))
            {
                throw new ArgumentException("The spot-light values do not match their captured identity and frame.");
            }

            if (index > 0 && FoundationCatalogue.CompareRfc(spotLightIds[index - 1], lightId) >= 0)
            {
                throw new ArgumentException("The render snapshot spot lights must be in canonical light-ID order.", nameof(spotLightIds));
            }
        }

        SnapshotId = snapshotId;
        WorldGuid = worldGuid;
        TickIndex = tickIndex;
        _cameraIds = [.. cameraIds];
        _cameras = [.. cameras];
        _directionalLightIds = [.. directionalLightIds];
        _directionalLights = [.. directionalLights];
        _pointLightIds = [.. pointLightIds];
        _pointLights = [.. pointLights];
        _spotLightIds = [.. spotLightIds];
        _spotLights = [.. spotLights];
        TargetExtent = targetExtent;
    }

    /// <summary>Gets the copied camera identities in committed camera order.</summary>
    public ImmutableArray<Guid> CameraIds => _cameraIds;

    /// <summary>Gets the copied camera values in committed camera order.</summary>
    public ImmutableArray<CameraSnapshot> Cameras => _cameras;

    /// <summary>Gets the copied directional-light identities in canonical RFC-byte order.</summary>
    public ImmutableArray<Guid> DirectionalLightIds => _directionalLightIds;

    /// <summary>Gets the copied directional-light values in canonical RFC-byte order.</summary>
    public ImmutableArray<RenderDirectionalLightSnapshot> DirectionalLights => _directionalLights;

    /// <summary>Gets the spatial dimension represented by this snapshot.</summary>
    public abstract RenderFrameDimension FrameDimension { get; }

    /// <summary>Gets the exact spatial frame revision represented by this snapshot.</summary>
    public abstract long FrameRevision { get; }

    /// <summary>Gets the copied point-light identities in canonical RFC-byte order.</summary>
    public ImmutableArray<Guid> PointLightIds => _pointLightIds;

    /// <summary>Gets the copied point-light values in canonical RFC-byte order.</summary>
    public ImmutableArray<RenderPointLightSnapshot> PointLights => _pointLights;

    /// <summary>Gets the exact spatial profile represented by this snapshot.</summary>
    public abstract SpatialProfileId ProfileId { get; }

    /// <summary>Gets the identity of this immutable snapshot.</summary>
    public Guid SnapshotId { get; }

    /// <summary>Gets the copied spot-light identities in canonical RFC-byte order.</summary>
    public ImmutableArray<Guid> SpotLightIds => _spotLightIds;

    /// <summary>Gets the copied spot-light values in canonical RFC-byte order.</summary>
    public ImmutableArray<RenderSpotLightSnapshot> SpotLights => _spotLights;

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
