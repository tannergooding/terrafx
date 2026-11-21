// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Assets;

internal static class OriginalPackageIoSourceCapacity
{
    internal const long BackingBytes = 328;
    internal const long ExecutionStateBytes = 160;
    internal const long LifetimeBytes = 112;
    internal const int MaximumOpeningRequestCount = AssetStore.OriginalMaximumEntryCount + 5;
    internal const long OpeningBaseBytes = 4_334_040;
    internal const long OpeningRequestBytes = RequestBytes * MaximumOpeningRequestCount;
    internal const long OperationBytes = 152 + 544;
    internal const long RequestBytes = 256;
    internal const long ServiceBytes =
        AssetStore.OriginalStoreSourceBytes + AssetStore.OriginalIssuerSourceBytes + OperationBytes
        + ExecutionStateBytes + LifetimeBytes + 65_536 + 32;

    internal static long GetColdCreateSourceBytes(
        long openingPathSourceBytes,
        long reportedPathSourceBytes,
        bool ownsAnchor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(openingPathSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(reportedPathSourceBytes);
        return checked(
            ServiceBytes + (ownsAnchor ? AssetStore.OriginalAnchorSourceBytes : 0)
            + GetKeepSourceBytes(AssetStore.OriginalMaximumEntryCount, reportedPathSourceBytes)
            + OpeningBaseBytes + OpeningRequestBytes + openingPathSourceBytes);
    }

    internal static long GetKeepSourceBytes(int entryCount, long reportedPathSourceBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entryCount, AssetStore.OriginalMaximumEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(reportedPathSourceBytes);
        return checked(
            BackingBytes + AssetStore.OriginalReadLimitsSourceBytes + AssetStore.OriginalTokenSourceBytes
            + OperationBytes + RequestBytes + 24 + ((long)entryCount * 32)
            + reportedPathSourceBytes);
    }

    internal static long GetOpeningSourceBytes(long pathSourceBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pathSourceBytes);
        return checked(OpeningBaseBytes + OpeningRequestBytes + pathSourceBytes);
    }

    internal static long GetPathBufferSourceBytes(int characterCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterCapacity);
        return Align8(checked(24 + (2L * characterCapacity)));
    }

    internal static long GetReportedPathSourceBytes(int characterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterCount);
        return Align8(checked(16 + 4 + (2L * (characterCount + 1))));
    }

    private static long Align8(long bytes)
        => checked((bytes + 7) & ~7L);
}
