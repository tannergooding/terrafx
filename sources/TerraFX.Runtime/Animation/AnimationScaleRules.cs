// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Numerics;

namespace TerraFX.Animation;

internal static class AnimationScaleRules
{
    internal static bool IsScaleValid(Vector3 scale)
        => scale.IsFinite().All()
        && (float.Abs(scale.X) >= 0.0001f)
        && (float.Abs(scale.X) <= 1000.0f)
        && (float.Abs(scale.Y) >= 0.0001f)
        && (float.Abs(scale.Y) <= 1000.0f)
        && (float.Abs(scale.Z) >= 0.0001f)
        && (float.Abs(scale.Z) <= 1000.0f);
}
