// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Hosting;

internal sealed class WorldPersistenceCheckpointContents
{
    private readonly byte[] _canonicalHash;
    private readonly byte[] _checkpointDigest;
    private readonly byte[] _headerRecord;
    private readonly byte[] _worldRecord;

    internal WorldPersistenceCheckpointContents(
        long transactionGeneration,
        byte mode,
        Guid worldGuid,
        Guid lineageGuid,
        long nextTick,
        byte[] canonicalHash,
        byte[] checkpointDigest,
        byte[] headerRecord,
        byte[] worldRecord)
    {
        ArgumentNullException.ThrowIfNull(canonicalHash);
        ArgumentNullException.ThrowIfNull(checkpointDigest);
        ArgumentNullException.ThrowIfNull(headerRecord);
        ArgumentNullException.ThrowIfNull(worldRecord);
        TransactionGeneration = transactionGeneration;
        Mode = mode;
        WorldGuid = worldGuid;
        LineageGuid = lineageGuid;
        NextTick = nextTick;
        _canonicalHash = canonicalHash;
        _checkpointDigest = checkpointDigest;
        _headerRecord = headerRecord;
        _worldRecord = worldRecord;
    }

    internal ReadOnlyMemory<byte> CanonicalHash => _canonicalHash;

    internal ReadOnlyMemory<byte> CheckpointDigest => _checkpointDigest;

    internal ReadOnlyMemory<byte> HeaderRecord => _headerRecord;

    internal Guid LineageGuid { get; }

    internal byte Mode { get; }

    internal long NextTick { get; }

    internal long TransactionGeneration { get; }

    internal Guid WorldGuid { get; }

    internal ReadOnlyMemory<byte> WorldRecord => _worldRecord;
}
