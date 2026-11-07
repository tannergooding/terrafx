// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

internal readonly record struct LinuxEvdevInputEvent(
    long Seconds,
    long Microseconds,
    ushort Type,
    ushort Code,
    int Value)
{
    internal bool TryGetNativeTimestamp(out long nanoseconds)
    {
        nanoseconds = 0;

        if ((Seconds < 0)
            || (Microseconds < 0)
            || (Microseconds >= 1_000_000))
        {
            return false;
        }

        var fractionalNanoseconds = Microseconds * 1_000;

        if (Seconds > ((long.MaxValue - fractionalNanoseconds) / 1_000_000_000))
        {
            return false;
        }

        nanoseconds = (Seconds * 1_000_000_000) + fractionalNanoseconds;
        return true;
    }
}
