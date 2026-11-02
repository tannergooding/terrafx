// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

internal static class WindowsRawInputDecoder
{
    internal const ushort RawKeyboardBreak = 0x0001;
    internal const ushort RawKeyboardE0 = 0x0002;
    internal const ushort RawKeyboardE1 = 0x0004;

    internal static int MapAbsoluteCoordinate(int normalizedCoordinate, int origin, int extent)
    {
        var coordinate = origin + ((double)normalizedCoordinate * (extent - 1) / ushort.MaxValue);

        if (!double.IsFinite(coordinate) || (coordinate < int.MinValue) || (coordinate > int.MaxValue))
        {
            throw new InvalidOperationException("Windows returned an absolute pointer coordinate outside the native range.");
        }

        return checked((int)Math.Round(coordinate, MidpointRounding.ToEven));
    }

    internal static bool TryMapKeyboardUsage(ushort makeCode, ushort flags, out ushort usage)
    {
        usage = 0;

        if ((flags & RawKeyboardE1) != 0)
        {
            if (makeCode == 0x45)
            {
                usage = 0x48;
                return true;
            }

            return false;
        }

        if ((flags & RawKeyboardE0) != 0)
        {
            usage = makeCode switch {
                0x1C => 0x58,
                0x1D => 0xE4,
                0x35 => 0x54,
                0x38 => 0xE6,
                0x47 => 0x4A,
                0x48 => 0x52,
                0x49 => 0x4B,
                0x4B => 0x50,
                0x4D => 0x4F,
                0x4F => 0x4D,
                0x50 => 0x51,
                0x51 => 0x4E,
                0x52 => 0x49,
                0x53 => 0x4C,
                0x5B => 0xE3,
                0x5C => 0xE7,
                0x5D => 0x65,
                _ => 0,
            };
            return usage != 0;
        }

        usage = makeCode switch {
            0x01 => 0x29,
            0x02 => 0x1E,
            0x03 => 0x1F,
            0x04 => 0x20,
            0x05 => 0x21,
            0x06 => 0x22,
            0x07 => 0x23,
            0x08 => 0x24,
            0x09 => 0x25,
            0x0A => 0x26,
            0x0B => 0x27,
            0x0C => 0x2D,
            0x0D => 0x2E,
            0x0E => 0x2A,
            0x0F => 0x2B,
            0x10 => 0x14,
            0x11 => 0x1A,
            0x12 => 0x08,
            0x13 => 0x15,
            0x14 => 0x17,
            0x15 => 0x1C,
            0x16 => 0x18,
            0x17 => 0x0C,
            0x18 => 0x12,
            0x19 => 0x13,
            0x1A => 0x2F,
            0x1B => 0x30,
            0x1C => 0x28,
            0x1D => 0xE0,
            0x1E => 0x04,
            0x1F => 0x16,
            0x20 => 0x07,
            0x21 => 0x09,
            0x22 => 0x0A,
            0x23 => 0x0B,
            0x24 => 0x0D,
            0x25 => 0x0E,
            0x26 => 0x0F,
            0x27 => 0x33,
            0x28 => 0x34,
            0x29 => 0x35,
            0x2A => 0xE1,
            0x2B => 0x31,
            0x2C => 0x1D,
            0x2D => 0x1B,
            0x2E => 0x06,
            0x2F => 0x19,
            0x30 => 0x05,
            0x31 => 0x11,
            0x32 => 0x10,
            0x33 => 0x36,
            0x34 => 0x37,
            0x35 => 0x38,
            0x36 => 0xE5,
            0x37 => 0x55,
            0x38 => 0xE2,
            0x39 => 0x2C,
            0x3A => 0x39,
            0x3B => 0x3A,
            0x3C => 0x3B,
            0x3D => 0x3C,
            0x3E => 0x3D,
            0x3F => 0x3E,
            0x40 => 0x3F,
            0x41 => 0x40,
            0x42 => 0x41,
            0x43 => 0x42,
            0x44 => 0x43,
            0x45 => 0x53,
            0x46 => 0x47,
            0x47 => 0x5F,
            0x48 => 0x60,
            0x49 => 0x61,
            0x4A => 0x56,
            0x4B => 0x5C,
            0x4C => 0x5D,
            0x4D => 0x5E,
            0x4E => 0x57,
            0x4F => 0x59,
            0x50 => 0x5A,
            0x51 => 0x5B,
            0x52 => 0x62,
            0x53 => 0x63,
            0x57 => 0x44,
            0x58 => 0x45,
            _ => 0,
        };
        return usage != 0;
    }
}
