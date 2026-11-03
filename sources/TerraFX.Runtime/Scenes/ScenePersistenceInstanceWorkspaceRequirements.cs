// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal readonly struct ScenePersistenceInstanceWorkspaceRequirements
{
    internal ScenePersistenceInstanceWorkspaceRequirements(
        int entityCount,
        int bindingCount,
        int externalBindingCount,
        int occurrenceCount,
        int entityIdsPayloadBytes,
        int bindingArrayPayloadBytes,
        int externalBindingArrayPayloadBytes,
        int occurrenceArrayPayloadBytes,
        int sourceRecordBytes,
        int rootPayloadBytes,
        int recordBytes)
    {
        BindingArrayPayloadBytes = bindingArrayPayloadBytes;
        BindingCount = bindingCount;
        EntityCount = entityCount;
        EntityIdsPayloadBytes = entityIdsPayloadBytes;
        ExternalBindingArrayPayloadBytes = externalBindingArrayPayloadBytes;
        ExternalBindingCount = externalBindingCount;
        OccurrenceArrayPayloadBytes = occurrenceArrayPayloadBytes;
        OccurrenceCount = occurrenceCount;
        RecordBytes = recordBytes;
        RootPayloadBytes = rootPayloadBytes;
        SourceRecordBytes = sourceRecordBytes;
        WorkspaceBytes = checked(
            (long)entityIdsPayloadBytes
            + bindingArrayPayloadBytes
            + externalBindingArrayPayloadBytes
            + occurrenceArrayPayloadBytes
            + (2L * sourceRecordBytes)
            + rootPayloadBytes
            + (2L * recordBytes));
    }

    internal int BindingArrayPayloadBytes { get; }

    internal int BindingCount { get; }

    internal int EntityCount { get; }

    internal int EntityIdsPayloadBytes { get; }

    internal int ExternalBindingArrayPayloadBytes { get; }

    internal int ExternalBindingCount { get; }

    internal int OccurrenceArrayPayloadBytes { get; }

    internal int OccurrenceCount { get; }

    internal int RecordBytes { get; }

    internal int RootPayloadBytes { get; }

    internal int SourceRecordBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(ScenePersistenceInstanceWorkspaceRequirements other)
        => (BindingArrayPayloadBytes == other.BindingArrayPayloadBytes)
        && (BindingCount == other.BindingCount)
        && (EntityCount == other.EntityCount)
        && (EntityIdsPayloadBytes == other.EntityIdsPayloadBytes)
        && (ExternalBindingArrayPayloadBytes == other.ExternalBindingArrayPayloadBytes)
        && (ExternalBindingCount == other.ExternalBindingCount)
        && (OccurrenceArrayPayloadBytes == other.OccurrenceArrayPayloadBytes)
        && (OccurrenceCount == other.OccurrenceCount)
        && (RecordBytes == other.RecordBytes)
        && (RootPayloadBytes == other.RootPayloadBytes)
        && (SourceRecordBytes == other.SourceRecordBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
