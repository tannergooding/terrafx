// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldAttachmentMembershipSource : IDisposable
{
    private readonly long _canonicalMutationRevision;
    private readonly long _currentSourceToken;
    private readonly StableIdentityLedger.PreparedChange? _identityChange;
    private int _isActive;
    private readonly int _ownerThreadId;
    private readonly Entity[] _projectedEntities;
    private readonly IReadOnlySet<Guid> _retiredEntityIds;
    private readonly int _sceneAssignmentOffset;
    private readonly WorldCommandStaging? _sealedCommandRoot;
    private readonly World _world;

    private WorldAttachmentMembershipSource(
        World world,
        long currentSourceToken,
        StableIdentityLedger.PreparedChange? identityChange,
        WorldCommandStaging? sealedCommandRoot,
        int sceneAssignmentOffset,
        Entity[] projectedEntities,
        IReadOnlySet<Guid> retiredEntityIds)
    {
        _world = world;
        _currentSourceToken = currentSourceToken;
        _identityChange = identityChange;
        _sealedCommandRoot = sealedCommandRoot;
        _sceneAssignmentOffset = sceneAssignmentOffset;
        _projectedEntities = projectedEntities;
        _retiredEntityIds = retiredEntityIds;
        _canonicalMutationRevision = world.CanonicalMutationRevision;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _isActive = 1;
    }

    internal static WorldAttachmentMembershipSource CreateCurrent(World world, long currentSourceToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentSourceToken);
        world.ValidateAttachmentMembershipSourceIssuance(
            projected: false,
            currentSourceToken: currentSourceToken);
        return new WorldAttachmentMembershipSource(
            world,
            currentSourceToken,
            null,
            null,
            0,
            [],
            ImmutableHashSet<Guid>.Empty);
    }

    internal static WorldAttachmentMembershipSource CreateProjected(
        World world,
        WorldCommandStaging sealedCommandRoot,
        StableIdentityLedger.PreparedChange? identityChange,
        int sceneAssignmentOffset,
        Entity[] projectedEntities,
        IReadOnlySet<Guid> retiredEntityIds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sealedCommandRoot);
        ArgumentNullException.ThrowIfNull(projectedEntities);
        ArgumentNullException.ThrowIfNull(retiredEntityIds);
        ArgumentOutOfRangeException.ThrowIfNegative(sceneAssignmentOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sceneAssignmentOffset, 1);

        if (!sealedCommandRoot.IsSealed)
        {
            throw new InvalidOperationException("Projected membership requires a sealed owner-command root.");
        }

        world.ValidateAttachmentMembershipSourceIssuance(projected: true, sealedCommandRoot: sealedCommandRoot);
        identityChange?.ValidateOwner(world.IdentityLedger);
        identityChange?.ValidateForCommit();
        var assignmentCount = identityChange?.AssignmentCount ?? 0;

        if (assignmentCount != checked(sceneAssignmentOffset + projectedEntities.Length))
        {
            throw new InvalidOperationException("The prepared identity assignments do not match projected Entity creation order.");
        }

        return new WorldAttachmentMembershipSource(
            world,
            0,
            identityChange,
            sealedCommandRoot,
            sceneAssignmentOffset,
            projectedEntities,
            retiredEntityIds);
    }

    private static void ThrowIfExcluded(
        Entity entity,
        Entity? excludedEntity,
        IReadOnlySet<Entity>? excludedEntities)
    {
        if (((excludedEntity is { } singleExcludedEntity) && entity.Equals(singleExcludedEntity))
            || (excludedEntities?.Contains(entity) ?? false))
        {
            throw new InvalidOperationException("A live attachment reference prevents destruction of its target Entity.");
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _isActive) == 0)
        {
            return;
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("The attachment membership source is expired or used from another thread.");
        }

        if (_currentSourceToken != 0)
        {
            _world.RevokeAttachmentMembershipSource(_currentSourceToken);
        }

        Volatile.Write(ref _isActive, 0);
    }

    internal void ValidateEntity(Entity entity, Entity? excludedEntity, IReadOnlySet<Entity>? excludedEntities)
    {
        EnsureActive();

        if (_identityChange is { } identityChange)
        {
            for (var index = 0; index < _projectedEntities.Length; index++)
            {
                if (!entity.Equals(_projectedEntities[index]))
                {
                    continue;
                }

                var assignmentIndex = checked(index + _sceneAssignmentOffset);
                var stableEntityId = identityChange.GetAssignedId(assignmentIndex);

                if (!identityChange.TryGetNewEntityAssignmentIndex(stableEntityId, out var actualAssignmentIndex)
                    || (actualAssignmentIndex != assignmentIndex)
                    || !entity.IsValid
                    || (entity.OwnerId != _world.OwnerId)
                    || (entity.Index != identityChange.GetAssignedLocator(assignmentIndex))
                    || _retiredEntityIds.Contains(stableEntityId))
                {
                    throw new InvalidOperationException("The projected Entity handle does not match its prepared identity assignment.");
                }

                ThrowIfExcluded(entity, excludedEntity, excludedEntities);
                return;
            }
        }

        var installedEntityId = _world.ValidateAttachmentEntityReference(entity);

        if (_retiredEntityIds.Contains(installedEntityId))
        {
            throw new InvalidOperationException("A live attachment reference prevents destruction of its target Entity.");
        }

        ThrowIfExcluded(entity, excludedEntity, excludedEntities);
    }

    internal void ValidateEntityId(Guid stableEntityId, Entity? excludedEntity, IReadOnlySet<Entity>? excludedEntities)
    {
        EnsureActive();

        if (stableEntityId == Guid.Empty)
        {
            throw new ArgumentException("A nonempty stable Entity identity is required.", nameof(stableEntityId));
        }

        if (_retiredEntityIds.Contains(stableEntityId))
        {
            throw new InvalidOperationException("A live attachment reference prevents destruction of its target Entity.");
        }

        if (_identityChange is not null
            && _identityChange.TryGetNewEntityAssignmentIndex(stableEntityId, out var assignmentIndex))
        {
            var projectedIndex = assignmentIndex - _sceneAssignmentOffset;

            if ((projectedIndex < 0) || (projectedIndex >= _projectedEntities.Length))
            {
                throw new InvalidOperationException("The prepared Entity identity does not match the projected creation order.");
            }

            var entity = _projectedEntities[projectedIndex];

            if (!entity.IsValid
                || (entity.OwnerId != _world.OwnerId)
                || (entity.Index != _identityChange.GetAssignedLocator(assignmentIndex)))
            {
                throw new InvalidOperationException("The prepared Entity identity does not match its projected World handle.");
            }

            ThrowIfExcluded(entity, excludedEntity, excludedEntities);
            return;
        }

        if (!_world.TryResolveAttachmentEntityReference(stableEntityId, out var currentEntity))
        {
            throw new InvalidOperationException("The attachment Entity reference does not identify a live World Entity.");
        }

        ValidateEntity(currentEntity, excludedEntity, excludedEntities);
    }

    internal void ValidateForCommit()
    {
        EnsureActive();
    }

    private void EnsureActive()
    {
        if ((Volatile.Read(ref _isActive) == 0)
            || (Environment.CurrentManagedThreadId != _ownerThreadId))
        {
            throw new InvalidOperationException("The attachment membership source is expired or used from another thread.");
        }

        if (_currentSourceToken != 0)
        {
            _world.ValidateAttachmentMembershipSourceUse(
                projected: false,
                currentSourceToken: _currentSourceToken);
        }
        else if (_sealedCommandRoot is { } commandRoot)
        {
            if (!commandRoot.IsSealed)
            {
                throw new InvalidOperationException("The sealed owner-command root changed during attachment validation.");
            }

            _world.ValidateAttachmentMembershipSourceUse(
                projected: true,
                sealedCommandRoot: commandRoot);
        }

        _identityChange?.ValidateForCommit();

        if (_world.CanonicalMutationRevision != _canonicalMutationRevision)
        {
            throw new InvalidOperationException("The attachment membership source is stale.");
        }
    }
}
