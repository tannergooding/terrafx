// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal static class GamepadStickNormalizer
{
    internal static Vector2 Normalize(float x, float y)
    {
        var lengthSquared = (x * x) + (y * y);

        if (lengthSquared > 1.0f)
        {
            // Leave one float32 step of headroom so rounding keeps the vector inside the unit disk.
            var inverseLength = MathF.BitDecrement(1.0f) / MathF.Sqrt(lengthSquared);
            x *= inverseLength;
            y *= inverseLength;
        }

        return new Vector2(x, y);
    }
}
