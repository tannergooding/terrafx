// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TerraFX;

namespace TerraFX.Entities;

/// <summary>Reports cleanup failures while retiring a previously installed frame cohort.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Frame retirement failures retain the exact committed frame facts and cleanup causes.")]
public sealed class WorldFrameRetirementException : InvalidOperationException
{
    internal WorldFrameRetirementException(
        Guid worldGuid,
        int dimension,
        SpatialFrame3? committedFrame3,
        SpatialFrame2? committedFrame2,
        long committedFrameRevision,
        long controlSequence,
        long appliedAtNextTick,
        ImmutableArray<Exception> cleanupExceptions)
        : base("The frame change committed, but retirement of the previous cohort failed.")
    {
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);

        if (dimension is not 2 and not 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimension));
        }

        var frameMatchesDimension = dimension switch {
            2 => committedFrame2.HasValue && !committedFrame3.HasValue,
            3 => committedFrame3.HasValue && !committedFrame2.HasValue,
            _ => false,
        };

        if (!frameMatchesDimension)
        {
            throw new ArgumentException("The committed frame does not match the selected dimension.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(committedFrameRevision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(controlSequence);
        ArgumentOutOfRangeException.ThrowIfNegative(appliedAtNextTick);

        if (cleanupExceptions.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A frame retirement failure requires initialized cleanup exceptions.", nameof(cleanupExceptions));
        }

        WorldGuid = worldGuid;
        Dimension = dimension;
        CommittedFrame3 = committedFrame3;
        CommittedFrame2 = committedFrame2;
        CommittedFrameRevision = committedFrameRevision;
        ControlSequence = controlSequence;
        AppliedAtNextTick = appliedAtNextTick;
        CleanupExceptions = ImmutableArrayCopy.Create(cleanupExceptions);
    }

    /// <summary>Gets the next tick index to which the committed frame change is visible.</summary>
    public long AppliedAtNextTick { get; }

    /// <summary>Gets the cleanup failures from retiring the previous cohort.</summary>
    public ImmutableArray<Exception> CleanupExceptions { get; }

    /// <summary>Gets the committed 2D frame when dimension is 2.</summary>
    public SpatialFrame2? CommittedFrame2 { get; }

    /// <summary>Gets the committed 3D frame when dimension is 3.</summary>
    public SpatialFrame3? CommittedFrame3 { get; }

    /// <summary>Gets the committed dimensional frame revision.</summary>
    public long CommittedFrameRevision { get; }

    /// <summary>Gets the control sequence of the already-committed frame change.</summary>
    public long ControlSequence { get; }

    /// <summary>Gets the affected dimensional frame.</summary>
    public int Dimension { get; }

    /// <summary>Gets the exact World identity that committed the frame change.</summary>
    public Guid WorldGuid { get; }
}
