// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceWorldRecordCaptureWorkspaceRequirements
{
    internal WorldPersistenceWorldRecordCaptureWorkspaceRequirements(
        int assetIdReferenceCount,
        int assetIdCount,
        WorldPersistenceWorldRecordWorkspaceRequirements worldRecord,
        long childProducerWorkspaceBytes = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assetIdReferenceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(assetIdCount);
        ArgumentOutOfRangeException.ThrowIfNegative(childProducerWorkspaceBytes);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(assetIdCount, assetIdReferenceCount);

        if (worldRecord.AssetIdCount != assetIdCount)
        {
            throw new ArgumentException("The World record workspace must match the measured unique AssetId count.", nameof(worldRecord));
        }

        AssetIdCount = assetIdCount;
        AssetIdReferenceCount = assetIdReferenceCount;
        AssetIdWorkspaceBytes = checked((long)sizeof(uint) * assetIdReferenceCount);
        ChildProducerWorkspaceBytes = childProducerWorkspaceBytes;
        WorldRecord = worldRecord;
        WorkspaceBytes = checked(childProducerWorkspaceBytes + AssetIdWorkspaceBytes + worldRecord.WorkspaceBytes);
    }

    internal int AssetIdCount { get; }

    internal int AssetIdReferenceCount { get; }

    internal long AssetIdWorkspaceBytes { get; }

    internal long ChildProducerWorkspaceBytes { get; }

    internal long WorkspaceBytes { get; }

    internal WorldPersistenceWorldRecordWorkspaceRequirements WorldRecord { get; }

    internal bool Matches(WorldPersistenceWorldRecordCaptureWorkspaceRequirements other)
        => (AssetIdCount == other.AssetIdCount)
        && (AssetIdReferenceCount == other.AssetIdReferenceCount)
        && (AssetIdWorkspaceBytes == other.AssetIdWorkspaceBytes)
        && (ChildProducerWorkspaceBytes == other.ChildProducerWorkspaceBytes)
        && WorldRecord.Matches(other.WorldRecord)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
