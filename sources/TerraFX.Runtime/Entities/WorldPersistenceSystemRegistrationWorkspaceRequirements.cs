// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceSystemRegistrationWorkspaceRequirements
{
    internal WorldPersistenceSystemRegistrationWorkspaceRequirements(
        int accessCount,
        int accessStorageBytes,
        int accessCandidateStorageBytes,
        int accessArrayPayloadBytes,
        int dependencyScratchCount,
        int beforeArrayPayloadBytes,
        int afterArrayPayloadBytes,
        int schemaReferenceRecordBytes,
        int rootPayloadStorageBytes,
        int recordBytes)
    {
        AccessArrayPayloadBytes = accessArrayPayloadBytes;
        AccessCandidateStorageBytes = accessCandidateStorageBytes;
        AccessCount = accessCount;
        AccessStorageBytes = accessStorageBytes;
        AfterArrayPayloadBytes = afterArrayPayloadBytes;
        BeforeArrayPayloadBytes = beforeArrayPayloadBytes;
        DependencyScratchCount = dependencyScratchCount;
        RecordBytes = recordBytes;
        RootPayloadStorageBytes = rootPayloadStorageBytes;
        SchemaReferenceRecordBytes = schemaReferenceRecordBytes;
        WorkspaceBytes = GetWorkspaceBytes();
    }

    internal int AccessArrayPayloadBytes { get; }

    internal int AccessCandidateStorageBytes { get; }

    internal int AccessCount { get; }

    internal int AccessStorageBytes { get; }

    internal int AfterArrayPayloadBytes { get; }

    internal int BeforeArrayPayloadBytes { get; }

    internal int DependencyScratchCount { get; }

    internal int RecordBytes { get; }

    internal int RootPayloadStorageBytes { get; }

    internal int SchemaReferenceRecordBytes { get; }

    internal long WorkspaceBytes { get; }

    private static long AddBytes(long bytes, int count, int elementSize)
        => checked(bytes + (checked((long)count) * elementSize));

    internal bool Matches(WorldPersistenceSystemRegistrationWorkspaceRequirements other)
        => (AccessArrayPayloadBytes == other.AccessArrayPayloadBytes)
        && (AccessCandidateStorageBytes == other.AccessCandidateStorageBytes)
        && (AccessCount == other.AccessCount)
        && (AccessStorageBytes == other.AccessStorageBytes)
        && (AfterArrayPayloadBytes == other.AfterArrayPayloadBytes)
        && (BeforeArrayPayloadBytes == other.BeforeArrayPayloadBytes)
        && (DependencyScratchCount == other.DependencyScratchCount)
        && (RootPayloadStorageBytes == other.RootPayloadStorageBytes)
        && (SchemaReferenceRecordBytes == other.SchemaReferenceRecordBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);

    internal WorldPersistenceSystemRegistrationWorkspaceRequirements WithRecordBytes(int recordBytes)
        => new(
            AccessCount,
            AccessStorageBytes,
            AccessCandidateStorageBytes,
            AccessArrayPayloadBytes,
            DependencyScratchCount,
            BeforeArrayPayloadBytes,
            AfterArrayPayloadBytes,
            SchemaReferenceRecordBytes,
            RootPayloadStorageBytes,
            recordBytes);

    private long GetWorkspaceBytes()
    {
        long bytes = 0;
        bytes = AddBytes(bytes, AccessCount, Unsafe.SizeOf<SystemResourceAccess>());
        bytes = AddBytes(bytes, AccessCount, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + AccessStorageBytes);
        bytes = checked(bytes + AccessCandidateStorageBytes);
        bytes = checked(bytes + AccessArrayPayloadBytes);
        bytes = checked(bytes + AfterArrayPayloadBytes);
        bytes = checked(bytes + BeforeArrayPayloadBytes);
        bytes = AddBytes(bytes, DependencyScratchCount, Unsafe.SizeOf<Guid>());
        bytes = checked(bytes + RootPayloadStorageBytes);
        bytes = checked(bytes + (2L * SchemaReferenceRecordBytes));
        return bytes;
    }
}
