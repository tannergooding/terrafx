// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceControlRecordArrayWorkspaceRequirements
{
    internal WorldPersistenceControlRecordArrayWorkspaceRequirements(
        int controlRecordCount,
        int recordStorageBytes,
        int maximumCandidateRecordBytes,
        int maximumRootPayloadBytes,
        int arrayPayloadBytes,
        long maximumRecordWriterScratchBytes,
        long workspaceBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(controlRecordCount);
        ArgumentOutOfRangeException.ThrowIfNegative(recordStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCandidateRecordBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRootPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRecordWriterScratchBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceBytes);
        ControlRecordCount = controlRecordCount;
        RecordStorageBytes = recordStorageBytes;
        MaximumCandidateRecordBytes = maximumCandidateRecordBytes;
        MaximumRootPayloadBytes = maximumRootPayloadBytes;
        ArrayPayloadBytes = arrayPayloadBytes;
        MaximumRecordWriterScratchBytes = maximumRecordWriterScratchBytes;
        WorkspaceBytes = workspaceBytes;
    }

    internal int ArrayPayloadBytes { get; }

    internal int ControlRecordCount { get; }

    internal int MaximumCandidateRecordBytes { get; }

    internal long MaximumRecordWriterScratchBytes { get; }

    internal int MaximumRootPayloadBytes { get; }

    internal int RecordStorageBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(WorldPersistenceControlRecordArrayWorkspaceRequirements other)
        => (ArrayPayloadBytes == other.ArrayPayloadBytes)
        && (ControlRecordCount == other.ControlRecordCount)
        && (MaximumCandidateRecordBytes == other.MaximumCandidateRecordBytes)
        && (MaximumRootPayloadBytes == other.MaximumRootPayloadBytes)
        && (MaximumRecordWriterScratchBytes == other.MaximumRecordWriterScratchBytes)
        && (RecordStorageBytes == other.RecordStorageBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
