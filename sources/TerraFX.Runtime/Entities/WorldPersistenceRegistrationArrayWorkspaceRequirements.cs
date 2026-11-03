// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceRegistrationArrayWorkspaceRequirements
{
    internal WorldPersistenceRegistrationArrayWorkspaceRequirements(
        int recordCount,
        int recordStorageBytes,
        int maximumCandidateBytes,
        int arrayPayloadBytes)
    {
        ArrayPayloadBytes = arrayPayloadBytes;
        MaximumCandidateBytes = maximumCandidateBytes;
        RecordCount = recordCount;
        RecordStorageBytes = recordStorageBytes;
        WorkspaceBytes = checked(
            ((long)recordCount * Unsafe.SizeOf<EbinPayloadSlice>())
            + recordStorageBytes
            + maximumCandidateBytes
            + arrayPayloadBytes);
    }

    internal int ArrayPayloadBytes { get; }

    internal int MaximumCandidateBytes { get; }

    internal int RecordCount { get; }

    internal int RecordStorageBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(WorldPersistenceRegistrationArrayWorkspaceRequirements other)
        => (ArrayPayloadBytes == other.ArrayPayloadBytes)
        && (MaximumCandidateBytes == other.MaximumCandidateBytes)
        && (RecordCount == other.RecordCount)
        && (RecordStorageBytes == other.RecordStorageBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
