// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;

namespace TerraFX.Scenes;

internal readonly struct ScenePersistenceControlRecordWorkspaceRequirements
{
    internal ScenePersistenceControlRecordWorkspaceRequirements(
        ScenePersistenceControlKind kind,
        int recordBytes,
        int rootPayloadBytes,
        int sourceAssetReferenceRecordBytes,
        int externalBindingRecordStorageBytes,
        int externalBindingCandidateBytes,
        int externalBindingArrayPayloadBytes,
        int externalBindingCount,
        int intentRecordStorageBytes,
        int intentCandidateBytes,
        int intentArrayPayloadBytes,
        int intentCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rootPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceAssetReferenceRecordBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(externalBindingRecordStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(externalBindingCandidateBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(externalBindingArrayPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(externalBindingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(intentRecordStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(intentCandidateBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(intentArrayPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(intentCount);
        Kind = kind;
        RecordBytes = recordBytes;
        RootPayloadBytes = rootPayloadBytes;
        SourceAssetReferenceRecordBytes = sourceAssetReferenceRecordBytes;
        ExternalBindingRecordStorageBytes = externalBindingRecordStorageBytes;
        ExternalBindingCandidateBytes = externalBindingCandidateBytes;
        ExternalBindingArrayPayloadBytes = externalBindingArrayPayloadBytes;
        ExternalBindingCount = externalBindingCount;
        IntentRecordStorageBytes = intentRecordStorageBytes;
        IntentCandidateBytes = intentCandidateBytes;
        IntentArrayPayloadBytes = intentArrayPayloadBytes;
        IntentCount = intentCount;
        WorkspaceBytes = checked(
            rootPayloadBytes
            + GetArrayWorkspaceBytes(
                externalBindingCount,
                externalBindingRecordStorageBytes,
                externalBindingCandidateBytes,
                externalBindingArrayPayloadBytes)
            + GetArrayWorkspaceBytes(
                intentCount,
                intentRecordStorageBytes,
                intentCandidateBytes,
                intentArrayPayloadBytes));
    }

    internal int ExternalBindingArrayPayloadBytes { get; }

    internal int ExternalBindingCandidateBytes { get; }

    internal int ExternalBindingCount { get; }

    internal int ExternalBindingRecordStorageBytes { get; }

    internal int IntentArrayPayloadBytes { get; }

    internal int IntentCandidateBytes { get; }

    internal int IntentCount { get; }

    internal int IntentRecordStorageBytes { get; }

    internal ScenePersistenceControlKind Kind { get; }

    internal int RecordBytes { get; }

    internal int RootPayloadBytes { get; }

    internal int SourceAssetReferenceRecordBytes { get; }

    internal long WorkspaceBytes { get; }

    private static long GetArrayWorkspaceBytes(
        int count,
        int recordStorageBytes,
        int maximumCandidateBytes,
        int arrayPayloadBytes)
        => checked(
            ((long)count * Unsafe.SizeOf<EbinPayloadSlice>())
            + recordStorageBytes
            + maximumCandidateBytes
            + arrayPayloadBytes);
}
