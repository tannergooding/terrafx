// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

public sealed partial class World
{
    internal ref struct EntityStateCaptureEnumerator
    {
        private readonly World _world;
        private readonly WorldPersistenceCaptureLease _lease;
        private Entity _currentEntity;
        private WorldArchetype? _currentArchetype;
        private int _rowIndex;
        private int _workIndex;
        private bool _hasCurrent;

        internal EntityStateCaptureEnumerator(World world, WorldPersistenceCaptureLease lease)
        {
            _world = world;
            _lease = lease;
            _currentEntity = default;
            _currentArchetype = null;
            _rowIndex = 0;
            _workIndex = 0;
            _hasCurrent = false;
        }

        internal readonly uint CurrentAssignmentOrdinal
        {
            get
            {
                ValidateCurrent();
                var ordinal = _world._identityLedger.GetLiveOrdinal(IdentityKind.Entity, _currentEntity.Index);
                return (ordinal != 0)
                    ? ordinal
                    : throw new InvalidOperationException("The live Entity has no stable assignment ordinal.");
            }
        }

        internal readonly Entity CurrentEntity
        {
            get
            {
                ValidateCurrent();
                return _currentEntity;
            }
        }

        internal readonly Guid CurrentEntityId
        {
            get
            {
                ValidateCurrent();
                return _world.GetEntityId(_currentEntity);
            }
        }

        internal readonly int CurrentPersistentComponentCount
        {
            get
            {
                var archetype = GetCurrentArchetype();
                var count = 0;

                foreach (var registration in archetype.Components)
                {
                    count += registration.Descriptor.Flags == ComponentStorageFlags.Persistent ? 1 : 0;
                }

                return count;
            }
        }

        internal readonly int CurrentTagCount
            => GetCurrentArchetype().Tags.Length;

        internal readonly int CurrentTransientSchemaCount
        {
            get
            {
                var archetype = GetCurrentArchetype();
                var count = 0;

                foreach (var registration in archetype.Components)
                {
                    count += registration.Descriptor.Flags == ComponentStorageFlags.Transient ? 1 : 0;
                }

                return count;
            }
        }

        internal readonly ContentSchemaReference GetPersistentComponentSchema(int componentIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(componentIndex);
            var archetype = GetCurrentArchetype();
            var selectedIndex = 0;

            foreach (var registration in archetype.Components)
            {
                if (registration.Descriptor.Flags != ComponentStorageFlags.Persistent)
                {
                    continue;
                }

                if (selectedIndex++ == componentIndex)
                {
                    return registration.SchemaReference;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(componentIndex));
        }

        internal readonly ContentSchemaReference GetTagSchema(int tagIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(tagIndex);
            var archetype = GetCurrentArchetype();
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tagIndex, archetype.Tags.Length);
            var schemaId = archetype.Tags[tagIndex];

            foreach (var tag in _world.Registration.Tags)
            {
                if (tag.SchemaId == schemaId)
                {
                    return tag.Schema;
                }
            }

            throw new InvalidOperationException("The live Entity archetype references an unregistered tag.");
        }

        internal readonly ContentSchemaReference GetTransientComponentSchema(int schemaIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(schemaIndex);
            var archetype = GetCurrentArchetype();
            var selectedIndex = 0;

            foreach (var registration in archetype.Components)
            {
                if (registration.Descriptor.Flags != ComponentStorageFlags.Transient)
                {
                    continue;
                }

                if (selectedIndex++ == schemaIndex)
                {
                    return registration.SchemaReference;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(schemaIndex));
        }

        internal bool MoveNext()
        {
            _lease.ValidateCurrent();
            _hasCurrent = false;

            while (_workIndex < _world._componentTagWorkPlanCount)
            {
                var work = _world._componentTagWorkPlan[_workIndex];

                if (_rowIndex >= work.Length)
                {
                    _workIndex++;
                    _rowIndex = 0;
                    continue;
                }

                var archetype = _world._queryArchetypes[work.ArchetypeOrdinal]
                    ?? throw new InvalidOperationException("The component/tag work plan references an empty archetype.");
                var row = work.StartRow + _rowIndex++;
                _currentEntity = archetype.Storage.GetEntities(work.ChunkIndex, row, 1)[0];
                _currentArchetype = archetype;
                _hasCurrent = true;
                return true;
            }

            _currentArchetype = null;
            _currentEntity = default;
            return false;
        }

        private readonly WorldArchetype GetCurrentArchetype()
        {
            ValidateCurrent();
            return _currentArchetype
                ?? throw new InvalidOperationException("The Entity capture enumerator has no current archetype.");
        }

        private readonly void ValidateCurrent()
        {
            _lease.ValidateCurrent();

            if (!_hasCurrent)
            {
                throw new InvalidOperationException("The Entity capture enumerator has no current record.");
            }
        }
    }
}
