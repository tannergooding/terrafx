// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldResourceReferenceBinding : IWorldResourceReferences
{
    private ValidatedWorldIdentityHistory? _history;
    private readonly EbinReadLimits _limits;
    private readonly WorldResourceOperationControl _operation;
    private readonly long? _requiredRequestTick;
    private WorldRestoreSourceControl? _sourceControl;
    private World? _world;

    internal WorldResourceReferenceBinding(
        WorldResourceOperationControl operation,
        ValidatedWorldIdentityHistory sourceHistory,
        long completedTick,
        EbinReadLimits limits,
        WorldRestoreSourceControl sourceControl)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(sourceHistory);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(sourceControl);
        ArgumentOutOfRangeException.ThrowIfNegative(completedTick);
        _operation = operation;
        _history = sourceHistory;
        _requiredRequestTick = completedTick;
        _limits = limits;
        _sourceControl = sourceControl;
    }

    internal WorldResourceReferenceBinding(WorldResourceOperationControl operation, World world, long? requiredRequestTick)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(world);

        if (requiredRequestTick is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredRequestTick));
        }

        _operation = operation;
        _world = world;
        _requiredRequestTick = requiredRequestTick;
        _limits = WorldPersistenceEbinLimits.Create(world.Options);
    }

    public EbinReadLimits PersistenceLimits
    {
        get
        {
            _operation.EnsureActive(_operation.Operation);
            return _limits;
        }
    }

    public long? RequiredRequestTick
    {
        get
        {
            _operation.EnsureActive(_operation.Operation);
            return _requiredRequestTick;
        }
    }

    public Guid WorldGuid
    {
        get
        {
            _operation.EnsureActive(_operation.Operation);
            return _world?.WorldGuid ?? _history?.WorldGuid
                ?? throw _operation.RecordFailure(new InvalidOperationException("The resource reference scope has expired."));
        }
    }

    public bool TryResolveHistoricalEntity(
        Guid entityId, Guid creationLineageId, Guid creationBranchId, out WorldEntityIdentity identity)
    {
        _operation.EnsureActive(_operation.Operation);

        if ((entityId == Guid.Empty) || (creationLineageId == Guid.Empty) || (creationBranchId == Guid.Empty))
        {
            throw _operation.RecordFailure(new ArgumentException("Historical resource identities require all three nonempty creation facts."));
        }

        if (_world is { } world)
        {
            return world.TryResolveResourceHistoricalEntity(entityId, creationLineageId, creationBranchId, out identity);
        }

        return (_sourceControl
            ?? throw _operation.RecordFailure(new InvalidOperationException("The resource source reference scope has expired.")))
            .TryResolveHistoricalEntity(entityId, creationLineageId, creationBranchId, out identity);
    }

    internal void Revoke()
    {
        _world = null;
        _history = null;
        _sourceControl = null;
    }
}
