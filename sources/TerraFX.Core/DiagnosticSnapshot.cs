// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

/// <summary>Contains one immutable diagnostic snapshot and its cumulative service counters.</summary>
public sealed class DiagnosticSnapshot
{
    /// <summary>Initializes a copied diagnostic snapshot.</summary>
    /// <param name="records">The immutable records in stable admission order.</param>
    /// <param name="droppedCount">The cumulative dropped-record count.</param>
    /// <param name="coalescedCount">The cumulative coalesced-occurrence count.</param>
    /// <exception cref="ArgumentException"><paramref name="records"/> is default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A counter is negative.</exception>
    internal DiagnosticSnapshot(ImmutableArray<Diagnostic> records, long droppedCount, long coalescedCount)
    {
        if (records.IsDefault)
        {
            throw new ArgumentException("Diagnostic records must be initialized.", nameof(records));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(droppedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(coalescedCount);

        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
        }

        Records = [.. records];
        DroppedCount = droppedCount;
        CoalescedCount = coalescedCount;
    }

    /// <summary>Gets the cumulative coalesced-occurrence count.</summary>
    public long CoalescedCount { get; }

    /// <summary>Gets the cumulative dropped-record count.</summary>
    public long DroppedCount { get; }

    /// <summary>Gets the immutable records in stable admission order.</summary>
    public ImmutableArray<Diagnostic> Records { get; }
}
