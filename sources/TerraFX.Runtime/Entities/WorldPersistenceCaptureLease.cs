// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Entities;

internal ref struct WorldPersistenceCaptureLease
{
    private readonly long _canonicalMutationRevision;
    private readonly long _cohortGeneration;
    private readonly long _committedMutationRevision;
    private readonly long _completedTick;
    private bool _isDisposed;
    private readonly long _nextTickIndex;
    private readonly long _publicationSequence;
    private readonly long _sequence;
    private readonly World _world;
    private readonly Guid _worldGuid;

    internal WorldPersistenceCaptureLease(
        World world,
        long sequence,
        long cohortGeneration,
        long canonicalMutationRevision,
        long committedMutationRevision,
        long publicationSequence,
        long completedTick,
        long nextTickIndex,
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
        ArgumentOutOfRangeException.ThrowIfNegative(nextTickIndex);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        _world = world;
        _sequence = sequence;
        _cohortGeneration = cohortGeneration;
        _canonicalMutationRevision = canonicalMutationRevision;
        _committedMutationRevision = committedMutationRevision;
        _publicationSequence = publicationSequence;
        _completedTick = completedTick;
        _nextTickIndex = nextTickIndex;
        _worldGuid = worldGuid;
        Frame3 = frame3;
        Frame2 = frame2;
        _isDisposed = false;
    }

    internal readonly long CanonicalMutationRevision => _canonicalMutationRevision;

    internal readonly long CohortGeneration => _cohortGeneration;

    internal readonly long CommittedMutationRevision => _committedMutationRevision;

    internal readonly long CompletedTick => _completedTick;

    internal readonly SpatialFrame2? Frame2 { get; }

    internal readonly SpatialFrame3 Frame3 { get; }

    internal readonly bool IsDisposed => _isDisposed;

    internal readonly long NextTickIndex => _nextTickIndex;

    internal readonly long PublicationSequence => _publicationSequence;

    internal readonly long Sequence => _sequence;

    internal readonly Guid WorldGuid => _worldGuid;

    internal readonly nint CountComponentStateRecord(
        Entity entity,
        ContentSchemaReference schema,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return _world.CountComponentStateRecord(this, entity, schema, cancellationToken);
    }

    internal void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _world.ReleasePersistenceCaptureLease(this);
        _isDisposed = true;
    }

    internal readonly bool IsBoundTo(World world)
        => ReferenceEquals(_world, world);

    internal readonly void ValidateCurrent()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(WorldPersistenceCaptureLease));
        }

        _world.ValidatePersistenceCaptureLease(this);
    }

    internal readonly nint WriteComponentStateRecord(
        Entity entity,
        ContentSchemaReference schema,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return _world.WriteComponentStateRecord(this, entity, schema, destination, cancellationToken);
    }
}
