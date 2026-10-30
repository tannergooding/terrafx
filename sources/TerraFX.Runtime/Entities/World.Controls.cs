// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Entities;

public sealed partial class World
{
    private const long ControlAppendPreparationBytes = 128;
    private const long FrameReceiptPreparationBytes = 128;

    internal float CurrentTimeScale => _timeScale;

    internal float InitialTimeScale => _initialTimeScale;

    private static int GetExpandedControlCapacity(int currentCapacity, int requiredCapacity, int maximumCapacity)
    {
        if (requiredCapacity > maximumCapacity)
        {
            throw new InvalidOperationException("The configured World control capacity was exceeded.");
        }

        var capacity = currentCapacity;

        if (capacity == 0)
        {
            capacity = Math.Min(4, maximumCapacity);
        }

        while (capacity < requiredCapacity)
        {
            capacity = Math.Min(checked(capacity * 2), maximumCapacity);
        }

        return capacity;
    }

    /// <summary>Starts a compatible identity branch for future stable Entity assignments.</summary>
    /// <param name="identityBranchId">The nonempty, never-before-used branch identity.</param>
    /// <exception cref="ArgumentException"><paramref name="identityBranchId"/> is empty or already present in this World history.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread, or branch/control capacity is exhausted.</exception>
    public void BeginIdentityBranch(Guid identityBranchId)
    {
        EnterStructuralOperation();

        try
        {
            if (identityBranchId == Guid.Empty)
            {
                throw new ArgumentException("An identity branch cannot be empty.", nameof(identityBranchId));
            }

            if (IsIdentityBranchAdmitted(identityBranchId))
            {
                throw new ArgumentException("An identity branch can be admitted only once per World history.", nameof(identityBranchId));
            }

            if (_identityBranchCount >= _maximumIdentityBranches)
            {
                throw new InvalidOperationException("The configured identity branch limit was reached.");
            }

            var controlSequence = GetNextControlSequence();
            var nextMutationRevision = checked(_canonicalMutationRevision + 1);
            var nextBranchIndex = _identityBranchCount;
            var firstAssignmentOrdinal = checked((uint)(_identityLedger.AssignmentCount + 1));
            var branch = new WorldIdentityBranchRecord(
                identityBranchId,
                identityBranchId,
                _replayLineageId,
                _identityBranchId,
                _nextTickIndex,
                firstAssignmentOrdinal,
                nextBranchIndex);
            var control = new WorldControlRecord(
                controlSequence,
                _nextTickIndex,
                WorldControlKind.Branch,
                branchId: identityBranchId,
                lineageId: _replayLineageId,
                branchIndex: checked((ushort)nextBranchIndex));
            var preparedAppend = TryPrepareControlAppend(appendBranch: true, extraPreparationBytes: 0)
                ?? throw new InvalidOperationException("The configured recorded-control capacity or shared World budget was reached.");
            using (preparedAppend)
            {
                _canonicalMutationRevision = nextMutationRevision;
                _identityBranchId = identityBranchId;
                _currentIdentityBranchIndex = nextBranchIndex;
                preparedAppend.Commit(control, branch);
            }
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    /// <summary>Creates an immutable inspection candidate for the next 2D frame revision.</summary>
    /// <param name="origin">The canonical spatial origin.</param>
    /// <param name="minimum">The closed relative minimum.</param>
    /// <param name="maximum">The closed relative maximum.</param>
    /// <returns>A copied candidate that carries no mutation authority.</returns>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or 2D frame admission is disabled.</exception>
    /// <exception cref="ArgumentException">The origin or relative extents are invalid.</exception>
    public SpatialFrame2 CreateNextFrame2(SpatialPosition2 origin, Vector2 minimum, Vector2 maximum)
    {
        EnsureWorldReadAccess();

        var currentCohort = _currentFrameCohort;
        var current = currentCohort.Frame2
            ?? throw new InvalidOperationException("The World has 2D frame admission disabled.");
        var revision = checked(current.Revision + 1);
        return new SpatialFrame2(_ownerId, _spatialProfileId, _spatialSettings, origin, revision, minimum, maximum);
    }

    /// <summary>Creates an immutable inspection candidate for the next 3D frame revision.</summary>
    /// <param name="origin">The canonical spatial origin.</param>
    /// <param name="minimum">The closed relative minimum.</param>
    /// <param name="maximum">The closed relative maximum.</param>
    /// <returns>A copied candidate that carries no mutation authority.</returns>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    /// <exception cref="ArgumentException">The origin or relative extents are invalid.</exception>
    public SpatialFrame3 CreateNextFrame3(SpatialPosition3 origin, Vector3 minimum, Vector3 maximum)
    {
        EnsureWorldReadAccess();
        var revision = checked(_currentFrameCohort.Frame3.Revision + 1);
        return new SpatialFrame3(_ownerId, _spatialProfileId, _spatialSettings, origin, revision, minimum, maximum);
    }

    /// <summary>Processes the currently admitted standalone owner-control boundary without advancing a tick.</summary>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public void PumpControlBoundary()
    {
        EnterStructuralOperation();

        try
        {
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    /// <summary>Installs one 2D frame candidate without advancing the simulation clock.</summary>
    /// <param name="frame">The exact-owner candidate at the current 2D revision plus one.</param>
    /// <returns>The committed frame facts or bounded rejection diagnostics.</returns>
    /// <exception cref="ArgumentException">The frame belongs to another World, profile, or spatial settings.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or 2D admission is disabled.</exception>
    public FrameChangeReceipt SetFrame(SpatialFrame2 frame)
    {
        EnterStructuralOperation();

        try
        {
            var currentCohort = _currentFrameCohort;
            var current = currentCohort.Frame2
                ?? throw new InvalidOperationException("The World has 2D frame admission disabled.");
            ValidateFrameOwner(frame.OwnerId, frame.ProfileId, frame.Settings, nameof(frame));

            var expectedRevision = checked(current.Revision + 1);

            if (frame.Revision != expectedRevision)
            {
                return CreateRejectedFrameReceipt(2, "WorldFrameRevisionStale", "The 2D frame candidate is not the current revision plus one.");
            }

            return CommitFrame2(frame);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    /// <summary>Installs one 3D frame candidate without advancing the simulation clock.</summary>
    /// <param name="frame">The exact-owner candidate at the current 3D revision plus one.</param>
    /// <returns>The committed frame facts or bounded rejection diagnostics.</returns>
    /// <exception cref="ArgumentException">The frame belongs to another World, profile, or spatial settings.</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread.</exception>
    public FrameChangeReceipt SetFrame(SpatialFrame3 frame)
    {
        EnterStructuralOperation();

        try
        {
            ValidateFrameOwner(frame.OwnerId, frame.ProfileId, frame.Settings, nameof(frame));
            var expectedRevision = checked(_currentFrameCohort.Frame3.Revision + 1);

            if (frame.Revision != expectedRevision)
            {
                return CreateRejectedFrameReceipt(3, "WorldFrameRevisionStale", "The 3D frame candidate is not the current revision plus one.");
            }

            return CommitFrame3(frame);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    /// <summary>Records the next simulation time scale without changing the fixed tick duration.</summary>
    /// <param name="timeScale">The finite multiplier in the closed interval [0, 4].</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeScale"/> is nonfinite or outside [0, 4].</exception>
    /// <exception cref="InvalidOperationException">The World is not idle on its owner thread or control capacity is exhausted.</exception>
    public void SetTimeScale(float timeScale)
    {
        EnterStructuralOperation();

        try
        {
            if (!float.IsFinite(timeScale) || (timeScale < 0) || (timeScale > 4))
            {
                throw new ArgumentOutOfRangeException(nameof(timeScale));
            }

            var controlSequence = GetNextControlSequence();
            var segmentSequence = checked(_timeScaleSegmentSequence + 1);
            var nextMutationRevision = checked(_canonicalMutationRevision + 1);
            var control = new WorldControlRecord(
                controlSequence,
                _nextTickIndex,
                WorldControlKind.TimeScale,
                timeScaleValue: timeScale,
                segmentSequence: segmentSequence);
            var preparedAppend = TryPrepareControlAppend(appendBranch: false, extraPreparationBytes: 0)
                ?? throw new InvalidOperationException("The configured recorded-control capacity or shared World budget was reached.");
            using (preparedAppend)
            {
                _canonicalMutationRevision = nextMutationRevision;
                _timeScale = timeScale;
                _timeScaleSegmentSequence = segmentSequence;
                preparedAppend.Commit(control, branch: null);
            }
        }
        finally
        {
            _ = Interlocked.Exchange(ref _isStructuralOperationActive, 0);
        }
    }

    internal Guid GetCreationBranchId(uint entityIndex)
    {
        var branchIndex = _identityLedger.GetBranchIndexForLiveLocator(IdentityKind.Entity, entityIndex);

        if ((uint)branchIndex >= (uint)_identityBranchCount)
        {
            throw new InvalidOperationException("A live Entity references a missing identity branch record.");
        }

        return _identityBranches[branchIndex].BranchId;
    }

    internal bool IsIdentityBranchAdmitted(Guid identityBranchId)
    {
        for (var index = 0; index < _identityBranchCount; index++)
        {
            if (_identityBranches[index].BranchId == identityBranchId)
            {
                return true;
            }
        }

        return false;
    }

    private FrameChangeReceipt CommitFrame2(SpatialFrame2 frame)
    {
        if ((_controlRecordCount >= _maximumRecordedControls) || (_nextControlSequence == long.MaxValue))
        {
            return CreateRejectedFrameReceipt(2, "WorldFramePreflightCapacityExceeded", "The World cannot admit another frame control.");
        }

        var controlSequence = _nextControlSequence;
        var nextControlSequence = checked(controlSequence + 1);
        var nextMutationRevision = checked(_canonicalMutationRevision + 1);
        var control = new WorldControlRecord(
            controlSequence,
            _nextTickIndex,
            WorldControlKind.Frame2,
            frame2: frame);
        var currentCohort = _currentFrameCohort;
        var nextCohort = new WorldFrameCohort(
            currentCohort.Frame3,
            frame,
            checked(currentCohort.CohortGeneration + 1));
        using var preparedAppend = TryPrepareControlAppend(
            appendBranch: false,
            extraPreparationBytes: FrameReceiptPreparationBytes);

        if (preparedAppend is null)
        {
            return CreateRejectedFrameReceipt(2, "WorldFramePreflightCapacityExceeded", "The World cannot reserve the next frame control.");
        }

        var receipt = new FrameChangeReceipt(
            FrameChangeStatus.Applied,
            _worldGuid,
            committedFrame3: null,
            frame,
            frame.Revision,
            controlSequence,
            _nextTickIndex,
            [],
            Task.CompletedTask);
        _canonicalMutationRevision = nextMutationRevision;
        _currentFrameCohort = nextCohort;
        preparedAppend.Commit(control, branch: null, nextControlSequence);
        return receipt;
    }

    private FrameChangeReceipt CommitFrame3(SpatialFrame3 frame)
    {
        if ((_controlRecordCount >= _maximumRecordedControls) || (_nextControlSequence == long.MaxValue))
        {
            return CreateRejectedFrameReceipt(3, "WorldFramePreflightCapacityExceeded", "The World cannot admit another frame control.");
        }

        var controlSequence = _nextControlSequence;
        var nextControlSequence = checked(controlSequence + 1);
        var nextMutationRevision = checked(_canonicalMutationRevision + 1);
        var control = new WorldControlRecord(
            controlSequence,
            _nextTickIndex,
            WorldControlKind.Frame3,
            frame3: frame);
        var currentCohort = _currentFrameCohort;
        var nextCohort = new WorldFrameCohort(
            frame,
            currentCohort.Frame2,
            checked(currentCohort.CohortGeneration + 1));
        using var preparedAppend = TryPrepareControlAppend(
            appendBranch: false,
            extraPreparationBytes: FrameReceiptPreparationBytes);

        if (preparedAppend is null)
        {
            return CreateRejectedFrameReceipt(3, "WorldFramePreflightCapacityExceeded", "The World cannot reserve the next frame control.");
        }

        var receipt = new FrameChangeReceipt(
            FrameChangeStatus.Applied,
            _worldGuid,
            frame,
            committedFrame2: null,
            frame.Revision,
            controlSequence,
            _nextTickIndex,
            [],
            Task.CompletedTask);
        _canonicalMutationRevision = nextMutationRevision;
        _currentFrameCohort = nextCohort;
        preparedAppend.Commit(control, branch: null, nextControlSequence);
        return receipt;
    }

    private FrameChangeReceipt CreateRejectedFrameReceipt(int dimension, string code, string message)
    {
        var operation = (dimension == 2)
            ? "TerraFX.Entities.World.SetFrame2"
            : "TerraFX.Entities.World.SetFrame3";
        var diagnostic = new Diagnostic(
            code,
            DiagnosticSeverity.Warning,
            _diagnosticsCapability.GetTimestamp(),
            null,
            _worldGuid,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            operation,
            null,
            message,
            []);
        _diagnosticsCapability.Write(diagnostic);
        return new FrameChangeReceipt(
            FrameChangeStatus.Rejected,
            _worldGuid,
            committedFrame3: null,
            committedFrame2: null,
            committedFrameRevision: null,
            controlSequence: null,
            appliedAtNextTick: null,
            [diagnostic],
            Task.CompletedTask);
    }

    private long GetNextControlSequence()
    {
        if (_nextControlSequence == long.MaxValue)
        {
            throw new InvalidOperationException("The World control-sequence cursor is exhausted.");
        }

        return _nextControlSequence;
    }

    private PreparedControlAppend? TryPrepareControlAppend(bool appendBranch, long extraPreparationBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(extraPreparationBytes);

        if ((_controlRecordCount >= _maximumRecordedControls)
            || (appendBranch && (_identityBranchCount >= _maximumIdentityBranches)))
        {
            return null;
        }

        var requiredControlCount = checked(_controlRecordCount + 1);
        var requiredBranchCount = appendBranch
            ? checked(_identityBranchCount + 1)
            : _identityBranchCount;
        var controlCapacity = GetExpandedControlCapacity(
            _controlRecords.Length,
            requiredControlCount,
            _maximumRecordedControls);
        var branchCapacity = GetExpandedControlCapacity(
            _identityBranches.Length,
            requiredBranchCount,
            _maximumIdentityBranches);
        var preparedControlBytes = (controlCapacity > _controlRecords.Length)
            ? ArrayBytes(controlCapacity, Unsafe.SizeOf<WorldControlRecord>())
            : 0;
        var preparedBranchBytes = (branchCapacity > _identityBranches.Length)
            ? ArrayBytes(branchCapacity, Unsafe.SizeOf<WorldIdentityBranchRecord>())
            : 0;
        var preparationBytes = checked(ControlAppendPreparationBytes + extraPreparationBytes);
        var reservedBytes = checked(preparedControlBytes + preparedBranchBytes + preparationBytes);

        if ((checked(GetWorldOwnedStateBytes() + reservedBytes) > _maximumOwnedStateBytes)
            || !_domainReservation.TryReserveAdditional(reservedBytes))
        {
            return null;
        }

        try
        {
            WorldControlRecord[]? preparedControls = null;
            WorldIdentityBranchRecord[]? preparedBranches = null;

            if (preparedControlBytes != 0)
            {
                preparedControls = new WorldControlRecord[controlCapacity];
                Array.Copy(_controlRecords, preparedControls, _controlRecordCount);
            }

            if (preparedBranchBytes != 0)
            {
                preparedBranches = new WorldIdentityBranchRecord[branchCapacity];
                Array.Copy(_identityBranches, preparedBranches, _identityBranchCount);
            }

            return new PreparedControlAppend(
                this,
                preparedControls,
                preparedBranches,
                reservedBytes,
                preparationBytes);
        }
        catch
        {
            _domainReservation.ReleaseAdditional(reservedBytes);
            throw;
        }
    }

    private void ValidateFrameOwner(
        uint ownerId,
        SpatialProfileId profileId,
        SpatialSettings settings,
        string parameterName)
    {
        if ((ownerId != _ownerId)
            || (profileId != _spatialProfileId)
            || (settings != _spatialSettings))
        {
            throw new ArgumentException("The frame candidate does not match this World, profile, or spatial settings.", parameterName);
        }
    }

    private sealed class PreparedControlAppend : IDisposable
    {
        private bool _committed;
        private readonly long _preparationBytes;
        private readonly WorldIdentityBranchRecord[]? _preparedBranches;
        private readonly WorldControlRecord[]? _preparedControls;
        private readonly long _reservedBytes;
        private readonly World _world;

        internal PreparedControlAppend(
            World world,
            WorldControlRecord[]? preparedControls,
            WorldIdentityBranchRecord[]? preparedBranches,
            long reservedBytes,
            long preparationBytes)
        {
            _world = world;
            _preparedControls = preparedControls;
            _preparedBranches = preparedBranches;
            _reservedBytes = reservedBytes;
            _preparationBytes = preparationBytes;
        }

        public void Dispose()
        {
            if (!_committed && (_reservedBytes != 0))
            {
                _world._domainReservation.ReleaseAdditional(_reservedBytes);
            }
        }

        internal void Commit(
            WorldControlRecord record,
            WorldIdentityBranchRecord? branch,
            long? nextControlSequence = null)
        {
            if (_committed)
            {
                throw new InvalidOperationException("The prepared World control append was already committed.");
            }

            var existingControlBytes = (_world._controlRecords.Length == 0)
                ? 0
                : ArrayBytes(_world._controlRecords.Length, Unsafe.SizeOf<WorldControlRecord>());
            var existingBranchBytes = (_world._identityBranchCount <= 1)
                ? 0
                : ArrayBytes(_world._identityBranches.Length, Unsafe.SizeOf<WorldIdentityBranchRecord>());
            var oldControlBytes = (_preparedControls is null) ? 0 : existingControlBytes;
            var oldBranchBytes = (_preparedBranches is null) ? 0 : existingBranchBytes;

            if (_preparedControls is not null)
            {
                _world._controlRecords = _preparedControls;
            }

            if (_preparedBranches is not null)
            {
                _world._identityBranches = _preparedBranches;
            }

            if (_world._controlRecordCount >= _world._controlRecords.Length)
            {
                throw new InvalidOperationException("The prepared World control buffer has no append capacity.");
            }

            _world._controlRecords[_world._controlRecordCount++] = record;

            if (branch is { } branchRecord)
            {
                if (_world._identityBranchCount >= _world._identityBranches.Length)
                {
                    throw new InvalidOperationException("The prepared identity-branch buffer has no append capacity.");
                }

                _world._identityBranches[_world._identityBranchCount++] = branchRecord;
            }

            var newControlBytes = (_preparedControls is null)
                ? existingControlBytes
                : ArrayBytes(_world._controlRecords.Length, Unsafe.SizeOf<WorldControlRecord>());
            var newBranchBytes = (_preparedBranches is null)
                ? existingBranchBytes
                : ArrayBytes(_world._identityBranches.Length, Unsafe.SizeOf<WorldIdentityBranchRecord>());
            _world._controlStorageByteReservation = checked(newControlBytes + newBranchBytes);
            _world._nextControlSequence = nextControlSequence
                ?? checked(record.Sequence + 1);
            _committed = true;

            var releaseBytes = checked(oldControlBytes + oldBranchBytes + _preparationBytes);

            if (releaseBytes != 0)
            {
                _world._domainReservation.ReleaseAdditional(releaseBytes);
            }
        }
    }

    private enum WorldControlKind : byte
    {
        Branch,
        Frame2,
        Frame3,
        TimeScale,
    }

    private readonly struct WorldControlRecord
    {
        internal WorldControlRecord(
            long sequence,
            long appliedAtNextTick,
            WorldControlKind kind,
            float timeScaleValue = default,
            long segmentSequence = 0,
            SpatialFrame2? frame2 = null,
            SpatialFrame3? frame3 = null,
            Guid branchId = default,
            Guid lineageId = default,
            ushort branchIndex = 0)
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

        internal WorldControlKind Kind { get; }

        internal Guid LineageId { get; }

        internal long SegmentSequence { get; }

        internal long Sequence { get; }

        internal float TimeScaleValue { get; }
    }

    private readonly struct WorldFrameCohort
    {
        internal WorldFrameCohort(SpatialFrame3 frame3, SpatialFrame2? frame2, long cohortGeneration)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cohortGeneration);
            Frame3 = frame3;
            Frame2 = frame2;
            CohortGeneration = cohortGeneration;
        }

        internal long CohortGeneration { get; }

        internal SpatialFrame2? Frame2 { get; }

        internal SpatialFrame3 Frame3 { get; }
    }

    private readonly struct WorldIdentityBranchRecord
    {
        internal WorldIdentityBranchRecord(
            Guid branchId,
            Guid namespaceId,
            Guid lineageId,
            Guid? parentBranchId,
            long createdAtNextTick,
            uint firstAssignmentOrdinal,
            int directoryIndex)
        {
            BranchId = branchId;
            NamespaceId = namespaceId;
            LineageId = lineageId;
            ParentBranchId = parentBranchId;
            CreatedAtNextTick = createdAtNextTick;
            FirstAssignmentOrdinal = firstAssignmentOrdinal;
            DirectoryIndex = directoryIndex;
        }

        internal Guid BranchId { get; }

        internal long CreatedAtNextTick { get; }

        internal int DirectoryIndex { get; }

        internal uint FirstAssignmentOrdinal { get; }

        internal Guid LineageId { get; }

        internal Guid NamespaceId { get; }

        internal Guid? ParentBranchId { get; }
    }
}
