// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Defines the structural limits for one EBIN read.</summary>
public sealed class EbinReadLimits
{
    /// <summary>Initializes structural limits for EBIN records.</summary>
    /// <param name="maximumRecordBytes">The maximum encoded byte length of any individual record.</param>
    /// <param name="maximumFieldsPerRecord">The maximum field count in one record.</param>
    /// <param name="maximumDepth">The maximum nested-record depth.</param>
    /// <param name="maximumArrayElements">The maximum element count in one array.</param>
    /// <param name="maximumUtf8Bytes">The maximum byte count in one UTF-8 field.</param>
    /// <param name="maximumAggregateRecords">The maximum total number of records in one scan.</param>
    /// <param name="maximumAggregateFields">The maximum total number of fields in one scan.</param>
    /// <param name="maximumAggregateArrayElements">The maximum total number of array elements in one scan.</param>
    /// <exception cref="ArgumentOutOfRangeException">A limit is outside its supported range.</exception>
    public EbinReadLimits(
        int maximumRecordBytes,
        int maximumFieldsPerRecord,
        int maximumDepth,
        int maximumArrayElements,
        int maximumUtf8Bytes,
        long maximumAggregateRecords,
        long maximumAggregateFields,
        long maximumAggregateArrayElements)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecordBytes, 64);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumFieldsPerRecord);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDepth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumDepth, 64);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumArrayElements);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumUtf8Bytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAggregateRecords, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAggregateFields);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAggregateArrayElements);

        MaximumRecordBytes = maximumRecordBytes;
        MaximumFieldsPerRecord = maximumFieldsPerRecord;
        MaximumDepth = maximumDepth;
        MaximumArrayElements = maximumArrayElements;
        MaximumUtf8Bytes = maximumUtf8Bytes;
        MaximumAggregateRecords = maximumAggregateRecords;
        MaximumAggregateFields = maximumAggregateFields;
        MaximumAggregateArrayElements = maximumAggregateArrayElements;
    }

    /// <summary>Gets the limits for the M02 foundation structural profile.</summary>
    public static EbinReadLimits Foundation { get; } = new EbinReadLimits(65_536, 64, 64, 1_048_576, 1_048_576, 1_048_576, 1_048_576, 1_048_576);

    /// <summary>Gets the maximum total array-element count in one scan.</summary>
    public long MaximumAggregateArrayElements { get; }

    /// <summary>Gets the maximum total field count in one scan.</summary>
    public long MaximumAggregateFields { get; }

    /// <summary>Gets the maximum total record count in one scan.</summary>
    public long MaximumAggregateRecords { get; }

    /// <summary>Gets the maximum element count in one array.</summary>
    public int MaximumArrayElements { get; }

    /// <summary>Gets the maximum nested-record depth.</summary>
    public int MaximumDepth { get; }

    /// <summary>Gets the maximum field count in one record.</summary>
    public int MaximumFieldsPerRecord { get; }

    /// <summary>Gets the maximum encoded byte length of one record.</summary>
    public int MaximumRecordBytes { get; }

    /// <summary>Gets the maximum byte count in one UTF-8 field.</summary>
    public int MaximumUtf8Bytes { get; }
}
