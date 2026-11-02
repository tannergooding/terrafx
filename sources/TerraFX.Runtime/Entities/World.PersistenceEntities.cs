// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;

namespace TerraFX.Entities;

public sealed partial class World
{
    private static void ReadIdentityAssignment(World world, uint ordinal, out Guid stableId, out uint metadata)
    {
        Span<byte> record = stackalloc byte[StableIdentityLedger.RecordSize];
        world._identityLedger.CopyRecord(ordinal, record);
        var locator = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        metadata = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
        var kind = (metadata >> 10) & 3;
        var origin = (metadata >> 12) & 3;
        var directoryIndex = metadata & 0x3FF;
        var isLive = (metadata & (1u << 14)) != 0;
        stableId = new Guid(record[..16], bigEndian: true);

        if ((metadata & ~0x7FFFu) != 0
            || (kind is not 1 and not 2)
            || (origin is < 1 or > 3)
            || (directoryIndex >= (uint)world._identityBranchCount)
            || (isLive != (locator != 0))
            || (stableId == Guid.Empty))
        {
            throw new InvalidOperationException("The identity ledger contains an invalid persistence record.");
        }
    }

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

        internal readonly Guid? CurrentSceneInstanceId
        {
            get
            {
                ValidateCurrent();

                if (_world._sceneParticipant is not { } sceneParticipant
                    || !sceneParticipant.TryGetSceneInstanceId(_world, _currentEntity, out var sceneInstanceId))
                {
                    return null;
                }

                return (sceneInstanceId != Guid.Empty)
                    ? sceneInstanceId
                    : throw new InvalidOperationException("The Scene participant returned an empty live SceneInstance identity.");
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

    internal ref struct IdentityAssignmentCaptureEnumerator
    {
        private const uint LiveMask = 1u << 14;

        private readonly World _world;
        private readonly WorldPersistenceCaptureLease _lease;
        private uint _currentOrdinal;
        private uint _currentMetadata;
        private Guid _currentStableId;
        private bool _hasCurrent;

        internal IdentityAssignmentCaptureEnumerator(World world, WorldPersistenceCaptureLease lease)
        {
            _world = world;
            _lease = lease;
            _currentOrdinal = 0;
            _currentMetadata = 0;
            _currentStableId = Guid.Empty;
            _hasCurrent = false;
        }

        internal readonly uint CurrentAssignmentOrdinal
        {
            get
            {
                ValidateCurrent();
                return _currentOrdinal;
            }
        }

        internal readonly int CurrentDirectoryIndex
        {
            get
            {
                ValidateCurrent();
                return (int)(_currentMetadata & 0x3FF);
            }
        }

        internal readonly bool CurrentIsLive
        {
            get
            {
                ValidateCurrent();
                return (_currentMetadata & LiveMask) != 0;
            }
        }

        internal readonly byte CurrentKind
        {
            get
            {
                ValidateCurrent();
                return (byte)((_currentMetadata >> 10) & 3);
            }
        }

        internal readonly uint CurrentMetadata
        {
            get
            {
                ValidateCurrent();
                return _currentMetadata;
            }
        }

        internal readonly byte CurrentOrigin
        {
            get
            {
                ValidateCurrent();
                return (byte)((_currentMetadata >> 12) & 3);
            }
        }

        internal readonly int CurrentPageIndex
        {
            get
            {
                ValidateCurrent();
                return checked((int)((_currentOrdinal - 1) / StableIdentityLedger.RecordsPerPage));
            }
        }

        internal readonly int CurrentPageOffset
        {
            get
            {
                ValidateCurrent();
                return (int)((_currentOrdinal - 1) % StableIdentityLedger.RecordsPerPage);
            }
        }

        internal readonly Guid CurrentStableId
        {
            get
            {
                ValidateCurrent();
                return _currentStableId;
            }
        }

        internal bool MoveNext()
        {
            _lease.ValidateCurrent();
            _hasCurrent = false;
            var assignmentCount = _world._identityLedger.AssignmentCount;

            if (_currentOrdinal >= (uint)assignmentCount)
            {
                return false;
            }

            var nextOrdinal = checked(_currentOrdinal + 1);
            ReadIdentityAssignment(_world, nextOrdinal, out var stableId, out var metadata);

            _currentOrdinal = nextOrdinal;
            _currentMetadata = metadata;
            _currentStableId = stableId;
            _hasCurrent = true;
            return true;
        }

        private readonly void ValidateCurrent()
        {
            _lease.ValidateCurrent();

            if (!_hasCurrent)
            {
                throw new InvalidOperationException("The identity assignment enumerator has no current record.");
            }
        }
    }

    internal ref struct IdentityBranchCaptureEnumerator
    {
        private readonly World _world;
        private readonly WorldPersistenceCaptureLease _lease;
        private int _branchIndex;
        private bool _hasCurrent;

        internal IdentityBranchCaptureEnumerator(World world, WorldPersistenceCaptureLease lease)
        {
            _world = world;
            _lease = lease;
            _branchIndex = -1;
            _hasCurrent = false;
        }

        internal readonly Guid CurrentBranchId
        {
            get
            {
                var branch = GetCurrentBranch();
                return branch.BranchId;
            }
        }

        internal readonly long CurrentCreatedAtNextTick
        {
            get
            {
                var branch = GetCurrentBranch();
                return branch.CreatedAtNextTick;
            }
        }

        internal readonly int CurrentDirectoryIndex
        {
            get
            {
                var branch = GetCurrentBranch();
                return branch.DirectoryIndex;
            }
        }

        internal readonly uint CurrentFirstAssignmentOrdinal
        {
            get
            {
                var branch = GetCurrentBranch();
                return branch.FirstAssignmentOrdinal;
            }
        }

        internal readonly byte CurrentKind
        {
            get
            {
                var branch = GetCurrentBranch();

                if (_branchIndex == 0 != (branch.ParentBranchId is null))
                {
                    throw new InvalidOperationException("The identity branch parent does not match its directory position.");
                }

                return (byte)((_branchIndex == 0) ? 1 : 2);
            }
        }

        internal readonly Guid CurrentLineageId
        {
            get
            {
                var branch = GetCurrentBranch();
                return branch.LineageId;
            }
        }

        internal readonly Guid CurrentNamespaceId
        {
            get
            {
                var branch = GetCurrentBranch();
                return branch.NamespaceId;
            }
        }

        internal readonly Guid? CurrentParentBranchId
        {
            get
            {
                var branch = GetCurrentBranch();
                return branch.ParentBranchId;
            }
        }

        internal bool MoveNext()
        {
            _lease.ValidateCurrent();
            _hasCurrent = false;

            if (++_branchIndex >= _world._identityBranchCount)
            {
                return false;
            }

            _hasCurrent = true;
            return true;
        }

        private readonly WorldIdentityBranchRecord GetCurrentBranch()
        {
            _lease.ValidateCurrent();

            if (!_hasCurrent)
            {
                throw new InvalidOperationException("The identity branch enumerator has no current record.");
            }

            var branch = _world._identityBranches[_branchIndex];

            if (branch.DirectoryIndex != _branchIndex)
            {
                throw new InvalidOperationException("The identity branch directory is not in canonical index order.");
            }

            return branch;
        }
    }

    internal readonly ref struct IdentityHistoryCapture
    {
        private readonly World _world;
        private readonly WorldPersistenceCaptureLease _lease;

        internal IdentityHistoryCapture(World world, WorldPersistenceCaptureLease lease)
        {
            ArgumentNullException.ThrowIfNull(world);
            _world = world;
            _lease = lease;
        }

        internal readonly Guid ActiveBranchId
        {
            get
            {
                ValidateCurrent();
                return _world._identityBranchId;
            }
        }

        internal readonly int AssignmentCount
        {
            get
            {
                ValidateCurrent();
                return _world._identityLedger.AssignmentCount;
            }
        }

        internal readonly int BranchCount
        {
            get
            {
                ValidateCurrent();
                return _world._identityBranchCount;
            }
        }

        internal readonly Guid IssuanceNamespaceId
        {
            get
            {
                ValidateCurrent();

                if ((uint)_world._currentIdentityBranchIndex >= (uint)_world._identityBranchCount)
                {
                    throw new InvalidOperationException("The active identity branch index is outside the World history.");
                }

                var branch = _world._identityBranches[_world._currentIdentityBranchIndex];

                if (branch.BranchId != _world._identityBranchId)
                {
                    throw new InvalidOperationException("The active identity branch does not match its directory entry.");
                }

                return branch.NamespaceId;
            }
        }

        internal readonly long NextAssignmentOrdinal
        {
            get
            {
                ValidateCurrent();
                return checked((long)_world._identityLedger.AssignmentCount + 1L);
            }
        }

        internal readonly int PageCount
            => GetValidatedPageCount();

        internal readonly Guid ReplayLineageId
        {
            get
            {
                ValidateCurrent();
                return _world._replayLineageId;
            }
        }

        internal readonly Guid WorldGuid
        {
            get
            {
                ValidateCurrent();
                return _world._worldGuid;
            }
        }

        internal readonly long CopyPage(int pageIndex, scoped Span<Guid> stableIds, scoped Span<uint> metadata)
        {
            var recordCount = GetPageRecordCount(pageIndex);

            if (stableIds.Length != recordCount)
            {
                throw new ArgumentException("The stable-ID destination must match the exact identity-page length.", nameof(stableIds));
            }

            if (metadata.Length != recordCount)
            {
                throw new ArgumentException("The metadata destination must match the exact identity-page length.", nameof(metadata));
            }

            var firstOrdinal = checked(((long)pageIndex * StableIdentityLedger.RecordsPerPage) + 1);

            for (var index = 0; index < recordCount; index++)
            {
                var ordinal = checked((uint)(firstOrdinal + index));
                ReadIdentityAssignment(_world, ordinal, out stableIds[index], out metadata[index]);
            }

            return firstOrdinal;
        }

        internal readonly IdentityAssignmentCaptureEnumerator GetAssignmentEnumerator()
        {
            ValidateCurrent();
            return new IdentityAssignmentCaptureEnumerator(_world, _lease);
        }

        internal readonly IdentityBranchCaptureEnumerator GetBranchEnumerator()
        {
            ValidateCurrent();
            return new IdentityBranchCaptureEnumerator(_world, _lease);
        }

        internal readonly int GetPageRecordCount(int pageIndex)
        {
            var pageCount = GetValidatedPageCount();
            ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, pageCount);

            var firstAssignmentIndex = checked(pageIndex * StableIdentityLedger.RecordsPerPage);
            return Math.Min(
                StableIdentityLedger.RecordsPerPage,
                _world._identityLedger.AssignmentCount - firstAssignmentIndex);
        }

        private readonly int GetValidatedPageCount()
        {
            ValidateCurrent();

            var assignmentCount = _world._identityLedger.AssignmentCount;
            var expectedPageCount = checked((int)(((long)assignmentCount + StableIdentityLedger.RecordsPerPage - 1) / StableIdentityLedger.RecordsPerPage));

            if (_world._identityLedger.LedgerPageCount != expectedPageCount)
            {
                throw new InvalidOperationException("The identity ledger page count does not match its assignment count.");
            }

            return expectedPageCount;
        }

        private readonly void ValidateCurrent()
            => _lease.ValidateCurrent();
    }
}
