// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace TerraFX.Graphics;

internal sealed class GraphicsRecordingUseAdmission
{
    private int _claimedUseCount;
    private readonly int _useCount;

    private GraphicsRecordingUseAdmission(int useCount)
    {
        _useCount = useCount;
    }

    internal static bool IsSupportedProfile
        => (IntPtr.Size == 8)
        && (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        && RuntimeFeature.IsDynamicCodeCompiled
        && (RuntimeInformation.FrameworkDescription == ".NET 11.0.0-rc.3.26522.148");

    internal int ClaimedUseCount => Volatile.Read(ref _claimedUseCount);

    // The cohort remains charged while a recipe or any live/disposed use lease
    // retains it. Removing a tree node is not a reusable managed-byte refund.
    internal long ManagedByteCharge => CalculateByteAllowance(_useCount);

    internal static long CalculateByteAllowance(long useCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(useCount);

        if (useCount == 0)
        {
            return 0;
        }

        // Qualified x64 field-slot bounds: admission (two ints), UseLease
        // (owner, sequence, exclusive flag, admission), SortedSet<UseLease>.Node
        // (item, left, right, color). Each object includes the 64-byte allowance.
        return checked(ObjectCharge(2) + (useCount * (ObjectCharge(4) + ObjectCharge(4))));
    }

    internal static GraphicsRecordingUseAdmission Create(int useCount, long maximumCpuBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(useCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCpuBytes);

        if (!IsSupportedProfile || (CalculateByteAllowance(useCount) > maximumCpuBytes))
        {
            ThrowUnavailable();
        }

        return new GraphicsRecordingUseAdmission(useCount);
    }

    [DoesNotReturn]
    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "An unqualified, contended or exhausted diagnostic use cohort is an allocation-budget refusal.")]
    internal static void ThrowUnavailable()
        => throw new OutOfMemoryException("The diagnostic recording use has no qualified nonwaiting CPU allowance.");

    private static long ObjectCharge(int pointerWidthSlots)
        => checked(64 + ((checked(pointerWidthSlots * (long)IntPtr.Size) + 15) & ~15L));

    internal void ClaimUse()
    {
        var observed = ClaimedUseCount;

        while (observed < _useCount)
        {
            var previous = Interlocked.CompareExchange(ref _claimedUseCount, observed + 1, observed);

            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }

        ThrowUnavailable();
    }

    internal void ValidateComplete()
    {
        if (ClaimedUseCount != _useCount)
        {
            throw new InvalidOperationException("The recording did not consume its exact pipeline-use cohort.");
        }
    }
}
