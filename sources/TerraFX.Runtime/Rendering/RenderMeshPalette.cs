// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly record struct RenderMeshPalette(
    Guid EntityId,
    int MeshAssetId,
    int SkeletonAssetId,
    ImmutableArray<Matrix4x4> SkinMatrices);
