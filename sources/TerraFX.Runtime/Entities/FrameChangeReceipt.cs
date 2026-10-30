// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using TerraFX;

namespace TerraFX.Entities;

/// <summary>Describes the committed frame facts or bounded diagnostics for one direct World frame change.</summary>
public sealed class FrameChangeReceipt
{
    internal FrameChangeReceipt(
        FrameChangeStatus status,
        Guid worldGuid,
        SpatialFrame3? committedFrame3,
        SpatialFrame2? committedFrame2,
        long? committedFrameRevision,
        long? controlSequence,
        long? appliedAtNextTick,
        ImmutableArray<Diagnostic> diagnostics,
        Task retirement)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentNullException.ThrowIfNull(retirement);

        if (diagnostics.IsDefault)
        {
            throw new ArgumentException("Frame-change diagnostics must be initialized.", nameof(diagnostics));
        }

        switch (status)
        {
            case FrameChangeStatus.Applied:
            {
                var hasFrame3 = committedFrame3 is not null;
                var hasFrame2 = committedFrame2 is not null;

                if ((hasFrame3 == hasFrame2)
                    || (committedFrameRevision is null)
                    || (controlSequence is null or <= 0)
                    || (appliedAtNextTick is null or < 0)
                    || !diagnostics.IsEmpty)
                {
                    throw new ArgumentException("An applied frame receipt requires exactly one complete frame and committed facts.");
                }

                var frameRevision = hasFrame3
                    ? committedFrame3!.Value.Revision
                    : committedFrame2!.Value.Revision;

                if (frameRevision != committedFrameRevision)
                {
                    throw new ArgumentException("The committed revision does not match the installed frame.", nameof(committedFrameRevision));
                }

                break;
            }

            case FrameChangeStatus.Rejected:
            {
                if ((committedFrame3 is not null)
                    || (committedFrame2 is not null)
                    || (committedFrameRevision is not null)
                    || (controlSequence is not null)
                    || (appliedAtNextTick is not null)
                    || diagnostics.IsEmpty
                    || (retirement.Status != TaskStatus.RanToCompletion))
                {
                    throw new ArgumentException("A rejected frame receipt cannot contain commit facts and requires completed retirement.");
                }

                break;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        Status = status;
        WorldGuid = worldGuid;
        CommittedFrame3 = committedFrame3;
        CommittedFrame2 = committedFrame2;
        CommittedFrameRevision = committedFrameRevision;
        ControlSequence = controlSequence;
        AppliedAtNextTick = appliedAtNextTick;
        Diagnostics = ImmutableArrayCopy.Create(diagnostics);
        Retirement = retirement;
    }

    /// <summary>Gets the next tick index to which an applied frame change is visible.</summary>
    public long? AppliedAtNextTick { get; }

    /// <summary>Gets the installed 2D frame, or null when the operation was not an applied 2D change.</summary>
    public SpatialFrame2? CommittedFrame2 { get; }

    /// <summary>Gets the installed 3D frame, or null when the operation was not an applied 3D change.</summary>
    public SpatialFrame3? CommittedFrame3 { get; }

    /// <summary>Gets the installed dimensional frame revision.</summary>
    public long? CommittedFrameRevision { get; }

    /// <summary>Gets the signed control sequence assigned to an applied frame change.</summary>
    public long? ControlSequence { get; }

    /// <summary>Gets initialized diagnostics for a rejected candidate; applied receipts have an empty array.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets the task that completes when the retired frame cohort releases its actual uses.</summary>
    public Task Retirement { get; }

    /// <summary>Gets whether the frame candidate was installed or rejected.</summary>
    public FrameChangeStatus Status { get; }

    /// <summary>Gets the exact World identity that owned the operation.</summary>
    public Guid WorldGuid { get; }
}
