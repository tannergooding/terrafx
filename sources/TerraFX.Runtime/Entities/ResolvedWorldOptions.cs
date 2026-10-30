// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Numerics;

namespace TerraFX.Entities;

internal readonly record struct ResolvedWorldOptions(
    WorldOptions Options,
    (Vector2 Minimum, Vector2 Maximum)? FrameExtent2,
    (Vector3 Minimum, Vector3 Maximum) FrameExtent3,
    int CpuWorkerCount);
