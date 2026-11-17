// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TerraFX.Buffers;

internal readonly struct BufferCompletionFootprint
{
    internal const int RegistrationCapacity = 4;

    internal BufferCompletionFootprint(nint byteLength, nint alignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alignment);

        if ((alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        Alignment = alignment;
        ByteLength = byteLength;
        var remainder = byteLength % alignment;
        NativeAllocationBytes = (remainder == 0) ? byteLength : checked(byteLength + alignment - remainder);
    }

    internal static bool IsSupportedProfile
        => (IntPtr.Size == 8)
        && (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        && RuntimeFeature.IsDynamicCodeCompiled
        && (RuntimeInformation.FrameworkDescription == ".NET 11.0.0-rc.3.26522.148");

    // Conservative x64 object bounds use a 64-byte header/alignment allowance and
    // pointer-width field slots (two for the embedded gate). The complete group
    // remains reserved: disposed wrappers can still root its controls/backing.
    internal static long ManagedByteAllowance
        => checked(
            ObjectCharge(17)             // allocation owner, including its admission reference and embedded gate
            + ObjectCharge(3)            // List<LeaseRegistration>
            + ArrayCharge(RegistrationCapacity, IntPtr.Size)
            + ObjectCharge(2)            // BufferAllocationOptions
            + ObjectCharge(2)            // finalized owner
            + (2 * ObjectCharge(2))      // writer and reader wrappers
            + (2 * ObjectCharge(7))      // writer and reader registrations
            + ObjectCharge(12)           // one-shot admission
            + ObjectCharge(0));          // possible first-use native allocator singleton

    internal nint Alignment { get; }

    internal nint ByteLength { get; }

    internal nint NativeAllocationBytes { get; }

    internal long TotalByteAllowance => checked(NativeAllocationBytes + ManagedByteAllowance);

    private static long ArrayCharge(long capacity, long stride)
        => checked(64 + ((checked(capacity * stride) + 15) & ~15L));

    private static long ObjectCharge(int pointerWidthSlots)
        => ArrayCharge(pointerWidthSlots, IntPtr.Size);
}
