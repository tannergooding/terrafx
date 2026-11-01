// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Entities;

internal ref struct WorldFrameReadLease
{
    private readonly long _canonicalMutationRevision;
    private readonly long _cohortGeneration;
    private readonly long _committedMutationRevision;
    private readonly long _completedTick;
    private readonly SpatialFrame2? _frame2;
    private readonly SpatialFrame3 _frame3;
    private readonly long _publicationSequence;
    private readonly long _sequence;
    private readonly World _world;
    private readonly Guid _worldGuid;
    private bool _isDisposed;

    internal WorldFrameReadLease(
        World world,
        long sequence,
        long cohortGeneration,
        long canonicalMutationRevision,
        long committedMutationRevision,
        long publicationSequence,
        long completedTick,
        Guid worldGuid,
        SpatialFrame3 frame3,
        SpatialFrame2? frame2)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cohortGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(canonicalMutationRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(committedMutationRevision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(publicationSequence);
        ArgumentOutOfRangeException.ThrowIfNegative(completedTick);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        _world = world;
        _sequence = sequence;
        _cohortGeneration = cohortGeneration;
        _canonicalMutationRevision = canonicalMutationRevision;
        _committedMutationRevision = committedMutationRevision;
        _publicationSequence = publicationSequence;
        _completedTick = completedTick;
        _worldGuid = worldGuid;
        _frame3 = frame3;
        _frame2 = frame2;
        _isDisposed = false;
    }

    internal readonly long CanonicalMutationRevision => _canonicalMutationRevision;

    internal readonly long CohortGeneration => _cohortGeneration;

    internal readonly long CommittedMutationRevision => _committedMutationRevision;

    internal readonly long CompletedTick => _completedTick;

    internal readonly SpatialFrame2? Frame2 => _frame2;

    internal readonly SpatialFrame3 Frame3 => _frame3;

    internal readonly bool IsDisposed => _isDisposed;

    internal readonly long PublicationSequence => _publicationSequence;

    internal readonly long Sequence => _sequence;

    internal readonly Guid WorldGuid => _worldGuid;

    internal void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _world.ReleaseFrameReadLease(this);
        _isDisposed = true;
    }

    internal readonly void ValidateCurrent()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(WorldFrameReadLease));
        }

        _world.ValidateFrameReadLease(this);
    }
}
