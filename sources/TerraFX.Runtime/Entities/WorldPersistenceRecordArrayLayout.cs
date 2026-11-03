// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceRecordArrayLayout
{
    internal static EbinWriteStatus MeasurePayloadBytes(
        int childCount,
        long childStorageBytes,
        EbinReadLimits limits,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegative(childCount);
        ArgumentOutOfRangeException.ThrowIfNegative(childStorageBytes);
        payloadBytes = 0;
        diagnostic = null;

        if ((childCount > limits.MaximumArrayElements)
            || (childCount > limits.MaximumAggregateArrayElements))
        {
            var limit = Math.Min((long)limits.MaximumArrayElements, limits.MaximumAggregateArrayElements);
            diagnostic = CreateDiagnostic(
                "array-count-budget",
                checked((ulong)childCount),
                checked((ulong)limit));
            return EbinWriteStatus.InvalidRecord;
        }

        // A record array places children after its 16-byte-aligned count and count-plus-one offset table.
        var offsetTableBytes = checked(8L + ((checked((long)childCount) + 1L) * 8L));
        var firstChildOffset = checked((offsetTableBytes + 15L) & ~15L);
        var totalBytes = checked(firstChildOffset + childStorageBytes);

        if (totalBytes > limits.MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic(
                "record-byte-budget",
                checked((ulong)totalBytes),
                checked((ulong)limits.MaximumRecordBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        payloadBytes = checked((int)totalBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong claim, ulong limit)
        => new EbinStructuralDiagnostic(
            Guid.Empty,
            0,
            category,
            null,
            claim,
            limit,
            EbinStructuralDiagnosticSource.Construction);
}
