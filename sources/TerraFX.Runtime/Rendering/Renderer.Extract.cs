// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

/// <summary>Extracts immutable CPU render snapshots from committed World state.</summary>
public sealed partial class Renderer
{
    private const int MaximumLightCount = 256;
    private static readonly Guid s_directionalLightSchemaId = new ContentSchemaIdentityAssignment("render/directional-light").SchemaId;
    private static readonly Guid s_pointLightSchemaId = new ContentSchemaIdentityAssignment("render/point-light").SchemaId;
    private static readonly Guid s_spotLightSchemaId = new ContentSchemaIdentityAssignment("render/spot-light").SchemaId;

    private static int CaptureCameras(
        World world,
        in WorldFrameReadLease frameReadLease,
        ImmutableArray<Guid> cameraIds,
        PixelSize targetExtent,
        Guid snapshotId,
        Span<CameraSnapshot> cameras)
    {
        if (cameras.Length < cameraIds.Length)
        {
            throw new ArgumentException("The camera scratch span is smaller than the selected camera set.", nameof(cameras));
        }

        for (var index = 0; index < cameraIds.Length; index++)
        {
            var cameraId = cameraIds[index];

            if (!world.TryGetEntity(cameraId, out var cameraEntity))
            {
                throw new ArgumentException("A requested camera identity is not live in this exact World.", nameof(cameraIds));
            }

            if (!world.TryGetCameraData(in frameReadLease, cameraEntity, out var camera))
            {
                throw new ArgumentException("A requested camera Entity has no registered Camera component.", nameof(cameraIds));
            }

            var (position, rotation) = world.ResolveSceneEntityPose(in frameReadLease, cameraEntity);
            cameras[index] = new CameraSnapshot(
                snapshotId,
                frameReadLease.WorldGuid,
                cameraId,
                camera.Order,
                frameReadLease.Frame3,
                targetExtent,
                position,
                rotation,
                (CameraProjectionKind)camera.ProjectionKind,
                camera.VerticalFieldOfView,
                camera.OrthographicHeight,
                camera.Near,
                (CameraFarMode)camera.FarMode,
                camera.Far.HasValue ? camera.Far.Value : null,
                camera.AspectRatioOverride.HasValue ? camera.AspectRatioOverride.Value : null,
                camera.Viewport,
                (CameraClearFlags)camera.ClearFlags,
                camera.ClearColor,
                camera.LayerMask);
        }

        for (var index = 1; index < cameraIds.Length; index++)
        {
            var camera = cameras[index];
            var insertionIndex = index;

            while (insertionIndex > 0 && CompareCameras(camera, cameras[insertionIndex - 1]) < 0)
            {
                cameras[insertionIndex] = cameras[insertionIndex - 1];
                insertionIndex--;
            }

            cameras[insertionIndex] = camera;
        }

        return cameraIds.Length;
    }

    private static ImmutableArray<RenderDirectionalLightSnapshot> CaptureDirectionalLights(
        World world,
        in WorldFrameReadLease frameReadLease,
        ReadOnlySpan<CameraSnapshot> cameras,
        Guid snapshotId,
        int directionalLightCount)
    {
        var directionalLights = new RenderDirectionalLightSnapshot[directionalLightCount];
        var directionalLightIndex = 0;
        var directionalLightComponents = world.ReadComponentSnapshot<RenderComponentDefinitions.DirectionalLightComponent>(in frameReadLease);

        while (directionalLightComponents.MoveNext())
        {
            var component = directionalLightComponents.CurrentValue;

            if (!component.Enabled || !IsSelectedByAnyCamera(component.LayerMask, cameras))
            {
                continue;
            }

            var entity = directionalLightComponents.CurrentEntity;
            var (_, rotation) = world.ResolveSceneEntityPose(in frameReadLease, entity);
            var direction = Vector3.Normalize(
                Vector3.TransformDirection(
                    component.Direction,
                    Matrix4x4.CreateFromQuaternion(rotation)));
            var entityId = world.GetEntityId(entity);
            var lightId = CreateLightId(entityId, s_directionalLightSchemaId);
            directionalLights[directionalLightIndex++] = new RenderDirectionalLightSnapshot(
                snapshotId,
                frameReadLease.WorldGuid,
                lightId,
                entityId,
                frameReadLease.Frame3,
                direction,
                component.Color,
                component.Illuminance,
                component.LayerMask,
                component.Priority);
        }

        if (directionalLightIndex != directionalLightCount)
        {
            throw new InvalidOperationException("The selected directional-light count changed during the committed frame read lease.");
        }

        Array.Sort(directionalLights, static (left, right) => FoundationCatalogue.CompareRfc(left.LightId, right.LightId));
        return [.. directionalLights];
    }

    private static ImmutableArray<RenderPointLightSnapshot> CapturePointLights(
        World world,
        in WorldFrameReadLease frameReadLease,
        ReadOnlySpan<CameraSnapshot> cameras,
        Guid snapshotId,
        int pointLightCount)
    {
        var pointLights = new RenderPointLightSnapshot[pointLightCount];
        var pointLightIndex = 0;
        var pointLightComponents = world.ReadComponentSnapshot<RenderComponentDefinitions.PointLightComponent>(in frameReadLease);

        while (pointLightComponents.MoveNext())
        {
            var component = pointLightComponents.CurrentValue;

            if (!IsSelectedByAnyCamera(component.LayerMask, cameras))
            {
                continue;
            }

            var entity = pointLightComponents.CurrentEntity;
            var (position, _) = world.ResolveSceneEntityPose(in frameReadLease, entity);
            var entityId = world.GetEntityId(entity);
            var lightId = CreateLightId(entityId, s_pointLightSchemaId);
            pointLights[pointLightIndex++] = new RenderPointLightSnapshot(
                snapshotId,
                frameReadLease.WorldGuid,
                lightId,
                entityId,
                frameReadLease.Frame3,
                position,
                component.Color,
                component.LuminousFlux,
                component.Range,
                component.LayerMask);
        }

        if (pointLightIndex != pointLightCount)
        {
            throw new InvalidOperationException("The selected point-light count changed during the committed frame read lease.");
        }

        Array.Sort(pointLights, static (left, right) => FoundationCatalogue.CompareRfc(left.LightId, right.LightId));
        return [.. pointLights];
    }

    private static ImmutableArray<RenderSpotLightSnapshot> CaptureSpotLights(
        World world,
        in WorldFrameReadLease frameReadLease,
        ReadOnlySpan<CameraSnapshot> cameras,
        Guid snapshotId,
        int spotLightCount)
    {
        var spotLights = new RenderSpotLightSnapshot[spotLightCount];
        var spotLightIndex = 0;
        var spotLightComponents = world.ReadComponentSnapshot<RenderComponentDefinitions.SpotLightComponent>(in frameReadLease);

        while (spotLightComponents.MoveNext())
        {
            var component = spotLightComponents.CurrentValue;

            if (!component.Enabled || !IsSelectedByAnyCamera(component.LayerMask, cameras))
            {
                continue;
            }

            var entity = spotLightComponents.CurrentEntity;
            var (position, rotation) = world.ResolveSceneEntityPose(in frameReadLease, entity);
            var direction = Vector3.Normalize(
                Vector3.TransformDirection(
                    component.Direction,
                    Matrix4x4.CreateFromQuaternion(rotation)));
            var entityId = world.GetEntityId(entity);
            var lightId = CreateLightId(entityId, s_spotLightSchemaId);
            spotLights[spotLightIndex++] = new RenderSpotLightSnapshot(
                snapshotId,
                frameReadLease.WorldGuid,
                lightId,
                entityId,
                frameReadLease.Frame3,
                position,
                direction,
                component.Color,
                component.LuminousFlux,
                component.Range,
                component.InnerConeAngle,
                component.OuterConeAngle,
                component.LayerMask,
                component.Priority);
        }

        if (spotLightIndex != spotLightCount)
        {
            throw new InvalidOperationException("The selected spot-light count changed during the committed frame read lease.");
        }

        Array.Sort(spotLights, static (left, right) => FoundationCatalogue.CompareRfc(left.LightId, right.LightId));
        return [.. spotLights];
    }

    private static int CompareCameras(CameraSnapshot left, CameraSnapshot right)
    {
        var comparison = left.Order.CompareTo(right.Order);
        return (comparison != 0)
            ? comparison
            : FoundationCatalogue.CompareRfc(left.CameraId, right.CameraId);
    }

    private static ImmutableArray<Guid> CopyCameraIds(ReadOnlySpan<CameraSnapshot> cameras)
    {
        var cameraIds = ImmutableArray.CreateBuilder<Guid>(cameras.Length);

        foreach (var camera in cameras)
        {
            cameraIds.Add(camera.CameraId);
        }

        return cameraIds.MoveToImmutable();
    }

    private static ImmutableArray<CameraSnapshot> CopyCameras(ReadOnlySpan<CameraSnapshot> cameras)
        => [.. cameras];

    private static ImmutableArray<Guid> CopyDirectionalLightIds(
        ImmutableArray<RenderDirectionalLightSnapshot> directionalLights)
    {
        var directionalLightIds = ImmutableArray.CreateBuilder<Guid>(directionalLights.Length);

        foreach (var directionalLight in directionalLights)
        {
            directionalLightIds.Add(directionalLight.LightId);
        }

        return directionalLightIds.MoveToImmutable();
    }

    private static ImmutableArray<Guid> CopyPointLightIds(ImmutableArray<RenderPointLightSnapshot> pointLights)
    {
        var pointLightIds = ImmutableArray.CreateBuilder<Guid>(pointLights.Length);

        foreach (var pointLight in pointLights)
        {
            pointLightIds.Add(pointLight.LightId);
        }

        return pointLightIds.MoveToImmutable();
    }

    private static ImmutableArray<Guid> CopySpotLightIds(ImmutableArray<RenderSpotLightSnapshot> spotLights)
    {
        var spotLightIds = ImmutableArray.CreateBuilder<Guid>(spotLights.Length);

        foreach (var spotLight in spotLights)
        {
            spotLightIds.Add(spotLight.LightId);
        }

        return spotLightIds.MoveToImmutable();
    }

    private static int CountSelectedDirectionalLights(
        World world,
        in WorldFrameReadLease frameReadLease,
        ReadOnlySpan<CameraSnapshot> cameras)
    {
        var count = 0;
        var directionalLights = world.ReadComponentSnapshot<RenderComponentDefinitions.DirectionalLightComponent>(in frameReadLease);

        while (directionalLights.MoveNext())
        {
            var light = directionalLights.CurrentValue;

            if (light.Enabled && IsSelectedByAnyCamera(light.LayerMask, cameras))
            {
                count = checked(count + 1);
            }
        }

        return count;
    }

    private static int CountSelectedPointLights(
        World world,
        in WorldFrameReadLease frameReadLease,
        ReadOnlySpan<CameraSnapshot> cameras)
    {
        var count = 0;
        var pointLights = world.ReadComponentSnapshot<RenderComponentDefinitions.PointLightComponent>(in frameReadLease);

        while (pointLights.MoveNext())
        {
            if (IsSelectedByAnyCamera(pointLights.CurrentValue.LayerMask, cameras))
            {
                count = checked(count + 1);
            }
        }

        return count;
    }

    private static int CountSelectedSpotLights(
        World world,
        in WorldFrameReadLease frameReadLease,
        ReadOnlySpan<CameraSnapshot> cameras)
    {
        var count = 0;
        var spotLights = world.ReadComponentSnapshot<RenderComponentDefinitions.SpotLightComponent>(in frameReadLease);

        while (spotLights.MoveNext())
        {
            var light = spotLights.CurrentValue;

            if (light.Enabled && IsSelectedByAnyCamera(light.LayerMask, cameras))
            {
                count = checked(count + 1);
            }
        }

        return count;
    }

    private static Guid CreateLightId(Guid entityId, Guid schemaId)
        => FoundationNameUuid.Create(entityId, "light/" + schemaId.ToString("N"));

    private static bool IsSelectedByAnyCamera(ulong layerMask, ReadOnlySpan<CameraSnapshot> cameras)
    {
        foreach (var camera in cameras)
        {
            if ((layerMask & camera.LayerMask) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Extracts a copied 2D snapshot from the exact committed World frame.</summary>
    /// <param name="world">The World whose committed state is to be copied.</param>
    /// <param name="options">The exact 2D frame, target extent, and live camera selection.</param>
    /// <returns>The immutable snapshot or a typed capacity refusal.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">The selected frame, camera, or World binding is invalid.</exception>
    /// <exception cref="InvalidOperationException">CPU extraction is not registered, the World is not idle, or its current revision is not committed.</exception>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The reviewed public API exposes Extract as an instance operation on the Renderer service.")]
    public RenderExtractionResponse Extract(World world, RenderExtractionOptions2D options)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        using var frameReadLease = world.AcquireFrameReadLease();

        if (frameReadLease.Frame2 is not SpatialFrame2 frame
            || !RenderExtractionValidation.MatchesFrame(options.Frame, frame))
        {
            throw new ArgumentException("The selected 2D frame is not the exact current World frame.", nameof(options));
        }

        var snapshotId = Guid.NewGuid();
        Span<CameraSnapshot> cameraScratch = stackalloc CameraSnapshot[options.CameraIds.Length];
        var cameraCount = CaptureCameras(
            world,
            in frameReadLease,
            options.CameraIds,
            options.TargetExtent,
            snapshotId,
            cameraScratch);
        var cameraSpan = cameraScratch[..cameraCount];
        var directionalLightCount = CountSelectedDirectionalLights(world, in frameReadLease, cameraSpan);
        var pointLightCount = CountSelectedPointLights(world, in frameReadLease, cameraSpan);
        var spotLightCount = CountSelectedSpotLights(world, in frameReadLease, cameraSpan);
        var lightCount = checked(checked(directionalLightCount + pointLightCount) + spotLightCount);

        if (lightCount > MaximumLightCount)
        {
            return new RenderExtractionResponse(
                RenderExtractionStatus.CapacityExceeded,
                snapshot: null,
                new RenderExtractionLimitDiagnostic(RenderExtractionLimitKind.Lights, lightCount, MaximumLightCount),
                []);
        }

        var cameras = CopyCameras(cameraSpan);
        var cameraIds = CopyCameraIds(cameraSpan);
        var directionalLights = CaptureDirectionalLights(
            world,
            in frameReadLease,
            cameraSpan,
            snapshotId,
            directionalLightCount);
        var directionalLightIds = CopyDirectionalLightIds(directionalLights);
        var pointLights = CapturePointLights(
            world,
            in frameReadLease,
            cameraSpan,
            snapshotId,
            pointLightCount);
        var pointLightIds = CopyPointLightIds(pointLights);
        var spotLights = CaptureSpotLights(
            world,
            in frameReadLease,
            cameraSpan,
            snapshotId,
            spotLightCount);
        var spotLightIds = CopySpotLightIds(spotLights);
        RenderSnapshot2D? snapshot = null;

        try
        {
            snapshot = new RenderSnapshot2D(
                snapshotId,
                frameReadLease.WorldGuid,
                frameReadLease.CompletedTick,
                cameraIds,
                cameras,
                directionalLightIds,
                directionalLights,
                pointLightIds,
                pointLights,
                spotLightIds,
                spotLights,
                options.TargetExtent,
                frame);
            frameReadLease.ValidateCurrent();
            var response = new RenderExtractionResponse(
                RenderExtractionStatus.Extracted,
                snapshot,
                limitDiagnostic: null,
                []);
            snapshot = null;
            return response;
        }
        finally
        {
            snapshot?.Dispose();
        }
    }

    /// <summary>Extracts a copied 3D snapshot from the exact committed World frame.</summary>
    /// <param name="world">The World whose committed state is to be copied.</param>
    /// <param name="options">The exact 3D frame, target extent, and live camera selection.</param>
    /// <returns>The immutable snapshot or a typed capacity refusal.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">The selected frame, camera, or World binding is invalid.</exception>
    /// <exception cref="InvalidOperationException">CPU extraction is not registered, the World is not idle, or its current revision is not committed.</exception>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The reviewed public API exposes Extract as an instance operation on the Renderer service.")]
    public RenderExtractionResponse Extract(World world, RenderExtractionOptions3D options)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        using var frameReadLease = world.AcquireFrameReadLease();

        if (!RenderExtractionValidation.MatchesFrame(options.Frame, frameReadLease.Frame3))
        {
            throw new ArgumentException("The selected 3D frame is not the exact current World frame.", nameof(options));
        }

        var snapshotId = Guid.NewGuid();
        Span<CameraSnapshot> cameraScratch = stackalloc CameraSnapshot[options.CameraIds.Length];
        var cameraCount = CaptureCameras(
            world,
            in frameReadLease,
            options.CameraIds,
            options.TargetExtent,
            snapshotId,
            cameraScratch);
        var cameraSpan = cameraScratch[..cameraCount];
        var directionalLightCount = CountSelectedDirectionalLights(world, in frameReadLease, cameraSpan);
        var pointLightCount = CountSelectedPointLights(world, in frameReadLease, cameraSpan);
        var spotLightCount = CountSelectedSpotLights(world, in frameReadLease, cameraSpan);
        var lightCount = checked(checked(directionalLightCount + pointLightCount) + spotLightCount);

        if (lightCount > MaximumLightCount)
        {
            return new RenderExtractionResponse(
                RenderExtractionStatus.CapacityExceeded,
                snapshot: null,
                new RenderExtractionLimitDiagnostic(RenderExtractionLimitKind.Lights, lightCount, MaximumLightCount),
                []);
        }

        var cameras = CopyCameras(cameraSpan);
        var cameraIds = CopyCameraIds(cameraSpan);
        var directionalLights = CaptureDirectionalLights(
            world,
            in frameReadLease,
            cameraSpan,
            snapshotId,
            directionalLightCount);
        var directionalLightIds = CopyDirectionalLightIds(directionalLights);
        var pointLights = CapturePointLights(
            world,
            in frameReadLease,
            cameraSpan,
            snapshotId,
            pointLightCount);
        var pointLightIds = CopyPointLightIds(pointLights);
        var spotLights = CaptureSpotLights(
            world,
            in frameReadLease,
            cameraSpan,
            snapshotId,
            spotLightCount);
        var spotLightIds = CopySpotLightIds(spotLights);
        RenderSnapshot3D? snapshot = null;

        try
        {
            snapshot = new RenderSnapshot3D(
                snapshotId,
                frameReadLease.WorldGuid,
                frameReadLease.CompletedTick,
                cameraIds,
                cameras,
                directionalLightIds,
                directionalLights,
                pointLightIds,
                pointLights,
                spotLightIds,
                spotLights,
                options.TargetExtent,
                frameReadLease.Frame3);
            frameReadLease.ValidateCurrent();
            var response = new RenderExtractionResponse(
                RenderExtractionStatus.Extracted,
                snapshot,
                limitDiagnostic: null,
                []);
            snapshot = null;
            return response;
        }
        finally
        {
            snapshot?.Dispose();
        }
    }
}
