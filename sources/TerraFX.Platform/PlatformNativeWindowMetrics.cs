// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal readonly record struct PlatformNativeWindowMetrics(
    Vector2 LogicalClientSize,
    PixelSize ClientPixelSize,
    Vector2 ContentScale,
    float? EffectiveDpi,
    DpiSource DpiSource,
    DpiConfidence DpiConfidence);
