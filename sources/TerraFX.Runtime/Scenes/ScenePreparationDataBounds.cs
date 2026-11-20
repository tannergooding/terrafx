// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal static class ScenePreparationDataBounds
{
    internal static long ArrayBytes(int count, int elementSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementSize);

        var payloadBytes = checked((long)count * elementSize);
        return checked(64L + ((payloadBytes + 15L) & ~15L));
    }
}
