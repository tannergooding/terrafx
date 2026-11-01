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
    private static ImmutableArray<CameraSnapshot> CaptureCameras(
        World world,
        in WorldFrameReadLease frameReadLease,
        ImmutableArray<Guid> cameraIds,
        PixelSize targetExtent,
        Guid snapshotId)
    {
        var cameras = new CameraSnapshot[cameraIds.Length];

        for (var index = 0; index < cameraIds.Length; index++)
        {
            var cameraId = cameraIds[index];

            if (!world.TryGetEntity(cameraId, out var cameraEntity))
            {
                throw new ArgumentException("A requested camera identity is not live in this exact World.", nameof(cameraIds));
            }

            if (!world.TryGet<SceneComponentDefinitions.CameraComponent>(cameraEntity, out var camera))
            {
                throw new ArgumentException("A requested camera Entity has no registered Camera component.", nameof(cameraIds));
            }

            var (position, rotation) = SceneHierarchyTransform.ResolveCameraPose(
                world,
                frameReadLease.Frame3,
                cameraEntity);
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

        Array.Sort(cameras, static (left, right) => {
            var comparison = left.Order.CompareTo(right.Order);
            return (comparison != 0)
                ? comparison
                : FoundationCatalogue.CompareRfc(left.CameraId, right.CameraId);
        });

        return [.. cameras];
    }

    private static ImmutableArray<Guid> CopyCameraIds(ImmutableArray<CameraSnapshot> cameras)
    {
        var cameraIds = ImmutableArray.CreateBuilder<Guid>(cameras.Length);

        foreach (var camera in cameras)
        {
            cameraIds.Add(camera.CameraId);
        }

        return cameraIds.MoveToImmutable();
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
        var cameras = CaptureCameras(
            world,
            in frameReadLease,
            options.CameraIds,
            options.TargetExtent,
            snapshotId);
        var cameraIds = CopyCameraIds(cameras);
        RenderSnapshot2D? snapshot = null;

        try
        {
            snapshot = new RenderSnapshot2D(
                snapshotId,
                frameReadLease.WorldGuid,
                frameReadLease.CompletedTick,
                cameraIds,
                cameras,
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
        var cameras = CaptureCameras(
            world,
            in frameReadLease,
            options.CameraIds,
            options.TargetExtent,
            snapshotId);
        var cameraIds = CopyCameraIds(cameras);
        RenderSnapshot3D? snapshot = null;

        try
        {
            snapshot = new RenderSnapshot3D(
                snapshotId,
                frameReadLease.WorldGuid,
                frameReadLease.CompletedTick,
                cameraIds,
                cameras,
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
