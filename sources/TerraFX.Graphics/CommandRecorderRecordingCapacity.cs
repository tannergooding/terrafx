// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TerraFX.Graphics;

internal readonly struct CommandRecorderRecordingCapacity
{
    // Measured command-recorder and collection shells for the selected .NET 11 x64 profile.
    internal const long CommandRecorderStateCarrierByteCharge = 264;
    internal const long DiagnosticBindingDictionaryCarrierByteCharge = 80;
    internal const long DiagnosticBindingHashSetCarrierByteCharge = 64;
    internal const long DiagnosticBindingListCarrierByteCharge = 32;

    internal CommandRecorderRecordingCapacity(
        int commandCount,
        int useLeaseCount,
        int pendingStorageWriteCount,
        int bindingCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(commandCount);
        ArgumentOutOfRangeException.ThrowIfNegative(useLeaseCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pendingStorageWriteCount);
        ArgumentOutOfRangeException.ThrowIfNegative(bindingCount);

        CommandCount = commandCount;
        UseLeaseCount = useLeaseCount;
        PendingStorageWriteCount = pendingStorageWriteCount;
        BindingCount = bindingCount;
        IsBounded = true;
    }

    internal int BindingCount { get; }

    internal int CommandBackingCapacity => GetBackingCapacity(CommandCount);

    internal int CommandCount { get; }

    internal bool IsBounded { get; }

    internal long KnownBackingArrayBytes => CalculateKnownBackingArrayBytes(CommandCount, UseLeaseCount);

    internal int PendingStorageWriteCount { get; }

    internal int UseLeaseBackingCapacity => GetBackingCapacity(UseLeaseCount);

    internal int UseLeaseCount { get; }

    internal static long CalculateArrayCharge(long capacity, long stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);
        return checked(64 + ((checked(capacity * stride) + 15) & ~15L));
    }

    internal static long CalculateKnownBackingArrayBytes(long commandCount, long useLeaseCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(commandCount);
        ArgumentOutOfRangeException.ThrowIfNegative(useLeaseCount);
        long bytes = 0;

        if (commandCount != 0)
        {
            bytes = checked(
                CalculateArrayCharge(Math.Max(4, commandCount), Unsafe.SizeOf<RecordedGraphicsCommand>())
                + CalculateArrayCharge(commandCount, Unsafe.SizeOf<RecordedGraphicsCommand>()));
        }

        if (useLeaseCount != 0)
        {
            bytes = checked(
                bytes
                + CalculateArrayCharge(Math.Max(4, useLeaseCount), IntPtr.Size)
                + CalculateArrayCharge(useLeaseCount, IntPtr.Size));
        }

        return bytes;
    }

    internal static long CalculateKnownFourBindingScratchArrayBytes()
    {
        if (!GraphicsRecordingUseAdmission.IsSupportedProfile)
        {
            GraphicsRecordingUseAdmission.ThrowUnavailable();
        }

        const int BindingCount = 4;
        // Dictionary and HashSet EnsureCapacity(4) use capacity 7 on the selected .NET 11 profile.
        const int DictionaryAndHashSetCapacity = 7;
        var bindingKeyStride = Unsafe.SizeOf<(ShaderBindingGroup Group, ShaderResourceKind Kind, int Binding)>();
        var hashSetEntryStride = checked((2 * sizeof(int)) + bindingKeyStride);
        var dictionaryEntryStride = checked(
            (2 * sizeof(int))
            + bindingKeyStride
            + Unsafe.SizeOf<ShaderResourceAccess>());
        return checked(
            CalculateArrayCharge(BindingCount, Unsafe.SizeOf<StorageResourceUse>())
            + CalculateArrayCharge(BindingCount, Unsafe.SizeOf<(bool IsTexture, ulong ResourceId)>())
            + CalculateArrayCharge(DictionaryAndHashSetCapacity, sizeof(int))
            + CalculateArrayCharge(DictionaryAndHashSetCapacity, dictionaryEntryStride)
            + CalculateArrayCharge(DictionaryAndHashSetCapacity, sizeof(int))
            + CalculateArrayCharge(DictionaryAndHashSetCapacity, hashSetEntryStride));
    }

    internal static long CalculateKnownFourBindingScratchObjectBytes()
    {
        if (!GraphicsRecordingUseAdmission.IsSupportedProfile)
        {
            GraphicsRecordingUseAdmission.ThrowUnavailable();
        }

        return checked(
            CommandRecorderStateCarrierByteCharge
            + (4L * DiagnosticBindingListCarrierByteCharge)
            + DiagnosticBindingDictionaryCarrierByteCharge
            + DiagnosticBindingHashSetCarrierByteCharge);
    }

    private static int GetBackingCapacity(int count)
        => (count == 0) ? 0 : Math.Max(4, count);

    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "A known backing-array minimum above the unchanged joint ceiling cannot be admitted.")]
    internal void ValidateKnownBackingArrayMinimum()
    {
        if (!IsBounded)
        {
            return;
        }

        try
        {
            if (KnownBackingArrayBytes > (32L * 1024 * 1024))
            {
                throw new OutOfMemoryException("The recording/freeze command and use-reference arrays alone exceed the joint diagnostic CPU allowance.");
            }
        }
        catch (OverflowException exception)
        {
            throw new OutOfMemoryException("The recording/freeze backing-array minimum cannot be represented.", exception);
        }
    }
}
