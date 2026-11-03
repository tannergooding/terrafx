// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceControlRecordWorkspaceRequirements
{
    internal WorldPersistenceControlRecordWorkspaceRequirements(
        WorldPersistenceControlKind kind,
        int payloadRecordBytes,
        int rootPayloadBytes,
        int recordBytes,
        long payloadScratchBytes,
        WorldPersistenceSpatialFrameWorkspaceRequirements frameWorkspace)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payloadRecordBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rootPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(payloadScratchBytes);
        Kind = kind;
        PayloadRecordBytes = payloadRecordBytes;
        RootPayloadBytes = rootPayloadBytes;
        RecordBytes = recordBytes;
        PayloadScratchBytes = payloadScratchBytes;
        FrameWorkspace = frameWorkspace;
        WorkspaceBytes = checked(payloadScratchBytes + rootPayloadBytes + (2L * recordBytes));
    }

    internal WorldPersistenceSpatialFrameWorkspaceRequirements FrameWorkspace { get; }

    internal WorldPersistenceControlKind Kind { get; }

    internal int PayloadRecordBytes { get; }

    internal long PayloadScratchBytes { get; }

    internal int RecordBytes { get; }

    internal int RootPayloadBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(WorldPersistenceControlRecordWorkspaceRequirements other)
        => (FrameWorkspace.WorkspaceBytes == other.FrameWorkspace.WorkspaceBytes)
        && (Kind == other.Kind)
        && (PayloadRecordBytes == other.PayloadRecordBytes)
        && (PayloadScratchBytes == other.PayloadScratchBytes)
        && (RecordBytes == other.RecordBytes)
        && (RootPayloadBytes == other.RootPayloadBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
