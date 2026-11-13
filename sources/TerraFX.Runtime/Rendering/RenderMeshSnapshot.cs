// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly record struct RenderMeshSnapshot(
    Guid SnapshotId,
    Guid WorldGuid,
    Guid SceneInstanceId,
    Guid DrawId,
    Guid EntityId,
    SpatialFrame3 Frame3,
    SpatialPosition3 Position,
    SpatialPosition3 SortPosition,
    Matrix4x4 LinearTransform,
    int MeshAssetId,
    int GroupIndex,
    int MaterialSlot,
    int MaterialAssetId,
    int PaletteIndex,
    ulong LayerMask,
    bool IsTransparent,
    bool HasPreviousTransform);
