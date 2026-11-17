// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly record struct RenderMeshVertexStreams(
    ImmutableArray<Vector3> Positions,
    ImmutableArray<Vector3> Normals,
    ImmutableArray<Vector4> Tangents,
    ImmutableArray<Vector2> Uv0,
    ImmutableArray<Vector2> Uv1,
    ImmutableArray<Vector4> Colors,
    ImmutableArray<ushort> JointIndices,
    ImmutableArray<Vector4> Weights);
