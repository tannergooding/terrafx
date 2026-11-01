// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly record struct RenderSpriteSnapshot(
    Guid SnapshotId,
    Guid WorldGuid,
    Guid SceneInstanceId,
    Guid DrawId,
    Guid EntityId,
    SpatialFrame3 Frame3,
    SpatialPosition3 Position,
    Matrix4x4 LinearTransform,
    int AtlasAssetId,
    Guid FrameId,
    Vector2 Pivot,
    Vector2 Size,
    Vector4 Tint,
    byte FlipMask,
    int SortingLayer,
    int SortingOrder,
    bool PixelSnap,
    int? MaterialAssetId,
    bool HasPreviousTransform);
