// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly record struct RenderMeshBounds(
    Vector3 Minimum,
    Vector3 Maximum,
    Vector3 SphereCenter,
    float SphereRadius);
