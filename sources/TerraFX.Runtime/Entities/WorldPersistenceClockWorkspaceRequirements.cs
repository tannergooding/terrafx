// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceClockWorkspaceRequirements
{
    internal WorldPersistenceClockWorkspaceRequirements(
        int controlRecordCount,
        int segmentCount,
        int segmentRecordBytes,
        int segmentStorageBytes,
        int segmentArrayPayloadBytes,
        bool hasInitialFrame2,
        WorldPersistenceSpatialFrameWorkspaceRequirements initialFrame2,
        WorldPersistenceSpatialFrameWorkspaceRequirements initialFrame3,
        int framePayloadStorageBytes,
        int frameCandidateStorageBytes,
        int clockPayloadStorageBytes,
        int recordBytes,
        long workspaceBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(controlRecordCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentRecordBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(segmentStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(segmentArrayPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(framePayloadStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(frameCandidateStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clockPayloadStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(workspaceBytes);
        ControlRecordCount = controlRecordCount;
        SegmentCount = segmentCount;
        SegmentRecordBytes = segmentRecordBytes;
        SegmentStorageBytes = segmentStorageBytes;
        SegmentArrayPayloadBytes = segmentArrayPayloadBytes;
        HasInitialFrame2 = hasInitialFrame2;
        InitialFrame2 = initialFrame2;
        InitialFrame3 = initialFrame3;
        FramePayloadStorageBytes = framePayloadStorageBytes;
        FrameCandidateStorageBytes = frameCandidateStorageBytes;
        ClockPayloadStorageBytes = clockPayloadStorageBytes;
        RecordBytes = recordBytes;
        WorkspaceBytes = workspaceBytes;
    }

    internal int ClockPayloadStorageBytes { get; }

    internal int ControlRecordCount { get; }

    internal int FrameCandidateStorageBytes { get; }

    internal int FramePayloadStorageBytes { get; }

    internal bool HasInitialFrame2 { get; }

    internal WorldPersistenceSpatialFrameWorkspaceRequirements InitialFrame2 { get; }

    internal WorldPersistenceSpatialFrameWorkspaceRequirements InitialFrame3 { get; }

    internal int RecordBytes { get; }

    internal int SegmentArrayPayloadBytes { get; }

    internal int SegmentCount { get; }

    internal int SegmentRecordBytes { get; }

    internal int SegmentStorageBytes { get; }

    internal long WorkspaceBytes { get; }
}
