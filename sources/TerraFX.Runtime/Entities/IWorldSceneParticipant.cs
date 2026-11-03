// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Numerics;

namespace TerraFX.Entities;

internal interface IWorldSceneParticipant
{
    Type HierarchyLocalTransformComponentType { get; }

    Type HierarchyRootPositionComponentType { get; }

    long OwnedByteReservation { get; }

    void CopySceneAssetIds(Span<uint> destination);

    void Dispose();

    void EndPersistenceCapture();

    int GetSceneAssetIdCount();

    ImmutableArray<WorldHierarchyDetachUpdate> PrepareHierarchyDetachment(
        int ancestorNodeCount,
        ReadOnlyMemory<WorldHierarchyNodeSnapshot> nodes);

    void PumpControlBoundary();

    (SpatialPosition3 Position, Quaternion Rotation) ResolveEntityPose(
        World world,
        SpatialFrame3 frame,
        Entity entity);

    (SpatialPosition3 Position, Matrix4x4 LinearTransform, Quaternion Rotation) ResolveEntityTransform(
        World world,
        SpatialFrame3 frame,
        Entity entity);

    bool TryBeginPersistenceCapture();

    bool TryGetCameraData(World world, Entity entity, out WorldCameraData camera);

    bool TryGetSceneInstanceId(World world, Entity entity, out Guid sceneInstanceId);
}
