// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceControlCaptureRow
{
    internal WorldPersistenceControlCaptureRow(
        long sequence,
        long appliedAtNextTick,
        WorldPersistenceControlKind kind,
        float timeScaleValue,
        long segmentSequence,
        SpatialFrame2? frame2,
        SpatialFrame3? frame3,
        Guid branchId,
        Guid lineageId,
        ushort branchIndex)
    {
        Sequence = sequence;
        AppliedAtNextTick = appliedAtNextTick;
        Kind = kind;
        TimeScaleValue = timeScaleValue;
        SegmentSequence = segmentSequence;
        Frame2 = frame2;
        Frame3 = frame3;
        BranchId = branchId;
        LineageId = lineageId;
        BranchIndex = branchIndex;
    }

    internal long AppliedAtNextTick { get; }

    internal Guid BranchId { get; }

    internal ushort BranchIndex { get; }

    internal SpatialFrame2? Frame2 { get; }

    internal SpatialFrame3? Frame3 { get; }

    internal WorldPersistenceControlKind Kind { get; }

    internal Guid LineageId { get; }

    internal long SegmentSequence { get; }

    internal long Sequence { get; }

    internal float TimeScaleValue { get; }
}
