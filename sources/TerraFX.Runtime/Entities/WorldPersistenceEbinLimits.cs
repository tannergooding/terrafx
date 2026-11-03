// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceEbinLimits
{
    private const int MaximumAggregateArrayElements = 2_147_483_584;
    private const int MaximumAggregateFields = 134_217_728;
    private const int MaximumAggregateRecords = 33_554_432;
    private const int MaximumArrayElements = 2_147_483_584;
    private const int MaximumUtf8Bytes = 1_048_576;

    internal static EbinReadLimits Create(WorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new EbinReadLimits(
            checked((int)options.MaximumSnapshotBytes),
            EbinReadLimits.Foundation.MaximumFieldsPerRecord,
            EbinReadLimits.Foundation.MaximumDepth,
            MaximumArrayElements,
            MaximumUtf8Bytes,
            MaximumAggregateRecords,
            MaximumAggregateFields,
            MaximumAggregateArrayElements);
    }
}
