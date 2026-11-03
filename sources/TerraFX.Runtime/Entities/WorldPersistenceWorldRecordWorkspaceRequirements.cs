// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceWorldRecordWorkspaceRequirements
{
    internal WorldPersistenceWorldRecordWorkspaceRequirements(
        int assetIdCount,
        int controlRecordCount,
        bool hasFrame2,
        bool hasStrictLayout,
        int randomStreamCount,
        int rootPayloadStorageBytes,
        int recordBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assetIdCount);
        ArgumentOutOfRangeException.ThrowIfNegative(controlRecordCount);
        ArgumentOutOfRangeException.ThrowIfNegative(randomStreamCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rootPayloadStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordBytes);
        AssetIdCount = assetIdCount;
        ControlRecordCount = controlRecordCount;
        HasFrame2 = hasFrame2;
        HasStrictLayout = hasStrictLayout;
        RandomStreamCount = randomStreamCount;
        RootPayloadStorageBytes = rootPayloadStorageBytes;
        RecordBytes = recordBytes;
        WorkspaceBytes = checked(
            rootPayloadStorageBytes
            + (2L * recordBytes)
            + (12L * Unsafe.SizeOf<EbinFieldSource>()));
    }

    internal int AssetIdCount { get; }

    internal int ControlRecordCount { get; }

    internal bool HasFrame2 { get; }

    internal bool HasStrictLayout { get; }

    internal int RandomStreamCount { get; }

    internal int RecordBytes { get; }

    internal int RootPayloadStorageBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(WorldPersistenceWorldRecordWorkspaceRequirements other)
        => (AssetIdCount == other.AssetIdCount)
        && (ControlRecordCount == other.ControlRecordCount)
        && (HasFrame2 == other.HasFrame2)
        && (HasStrictLayout == other.HasStrictLayout)
        && (RandomStreamCount == other.RandomStreamCount)
        && (RecordBytes == other.RecordBytes)
        && (RootPayloadStorageBytes == other.RootPayloadStorageBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
