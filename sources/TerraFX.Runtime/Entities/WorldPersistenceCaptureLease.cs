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
    private readonly int _controlRecordCount;
    private readonly SpatialFrame2? _initialFrame2;
    private readonly SpatialFrame3 _initialFrame3;
    private readonly float _initialTimeScale;
    private bool _isDisposed;
    private readonly long _nextControlSequence;
    private readonly long _nextTickIndex;
    private readonly long _publicationSequence;
    private readonly long _sequence;
    private readonly IWorldSceneParticipant? _sceneParticipant;
    private readonly int _tickRate;
    private readonly float _timeScale;
    private readonly long _timeScaleSegmentSequence;
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
        int tickRate,
        float initialTimeScale,
        float timeScale,
        long timeScaleSegmentSequence,
        long nextControlSequence,
        int controlRecordCount,
        SpatialFrame3 initialFrame3,
        SpatialFrame2? initialFrame2,
        SpatialFrame3 frame3,
        SpatialFrame2? frame2,
        IWorldSceneParticipant? sceneParticipant)
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeScaleSegmentSequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextControlSequence);
        ArgumentOutOfRangeException.ThrowIfNegative(controlRecordCount);
        _world = world;
        _sequence = sequence;
        _cohortGeneration = cohortGeneration;
        _canonicalMutationRevision = canonicalMutationRevision;
        _committedMutationRevision = committedMutationRevision;
        _publicationSequence = publicationSequence;
        _completedTick = completedTick;
        _nextTickIndex = nextTickIndex;
        _worldGuid = worldGuid;
        _tickRate = tickRate;
        _initialTimeScale = initialTimeScale;
        _timeScale = timeScale;
        _timeScaleSegmentSequence = timeScaleSegmentSequence;
        _nextControlSequence = nextControlSequence;
        _controlRecordCount = controlRecordCount;
        _initialFrame3 = initialFrame3;
        _initialFrame2 = initialFrame2;
        Frame3 = frame3;
        Frame2 = frame2;
        _sceneParticipant = sceneParticipant;
        _isDisposed = false;
    }

    internal readonly long CanonicalMutationRevision => _canonicalMutationRevision;

    internal readonly long CohortGeneration => _cohortGeneration;

    internal readonly long CommittedMutationRevision => _committedMutationRevision;

    internal readonly long CompletedTick => _completedTick;

    internal readonly int ControlRecordCount => _controlRecordCount;

    internal readonly SpatialFrame2? Frame2 { get; }

    internal readonly SpatialFrame3 Frame3 { get; }

    internal readonly SpatialFrame2? InitialFrame2 => _initialFrame2;

    internal readonly SpatialFrame3 InitialFrame3 => _initialFrame3;

    internal readonly float InitialTimeScale => _initialTimeScale;

    internal readonly bool IsDisposed => _isDisposed;

    internal readonly long NextControlSequence => _nextControlSequence;

    internal readonly long NextTickIndex => _nextTickIndex;

    internal readonly uint OwnerId => _world.OwnerId;

    internal readonly long PublicationSequence => _publicationSequence;

    internal readonly long Sequence => _sequence;

    internal readonly int TickRate => _tickRate;

    internal readonly float TimeScale => _timeScale;

    internal readonly long TimeScaleSegmentSequence => _timeScaleSegmentSequence;

    internal readonly Guid WorldGuid => _worldGuid;

    internal readonly nint CountAttachmentStateRecord(int attachmentIndex, CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return _world.CountAttachmentStateRecord(this, attachmentIndex, cancellationToken);
    }

    internal readonly nint CountComponentStateRecord(
        Entity entity,
        ContentSchemaReference schema,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return _world.CountComponentStateRecord(this, entity, schema, cancellationToken);
    }

    internal readonly nint CountResourceStateRecord(int resourceIndex, CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return _world.CountResourceStateRecord(this, resourceIndex, cancellationToken);
    }

    internal readonly nint CountSystemStateRecord(int systemIndex, CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return _world.CountSystemStateRecord(this, systemIndex, cancellationToken);
    }

    internal void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _world.ReleasePersistenceCaptureLease(this);
        _isDisposed = true;
        _sceneParticipant?.EndPersistenceCapture();
    }

    internal readonly int GetAttachmentStateCount()
    {
        ValidateCurrent();
        return _world.GetPersistenceAttachmentCount(this);
    }

    internal readonly World.AttachmentStateCaptureEnumerator GetAttachmentStateEnumerator()
    {
        ValidateCurrent();
        return _world.CreateAttachmentStateEnumerator(this);
    }

    internal readonly World.AttachmentStateCaptureEnumerator GetCanonicalAttachmentStateEnumerator(
        Span<World.AttachmentStateCaptureRow> destination)
    {
        ValidateCurrent();
        return _world.CreateCanonicalAttachmentStateEnumerator(this, destination);
    }

    internal readonly World.EntityStateCaptureEnumerator GetCanonicalEntityStateEnumerator(
        Span<World.EntityStateCaptureRow> destination)
    {
        ValidateCurrent();
        return _world.CreateCanonicalEntityStateEnumerator(this, destination);
    }

    internal readonly World.ComponentStateCaptureEnumerator GetComponentStateEnumerator()
    {
        ValidateCurrent();
        return _world.CreateComponentStateEnumerator(this);
    }

    internal readonly WorldPersistenceControlCaptureRow GetControlCapture(int index)
    {
        ValidateCurrent();
        return _world.GetPersistenceControlCapture(this, index);
    }

    internal readonly int GetEntityStateCount()
    {
        ValidateCurrent();
        return _world.GetPersistenceEntityCount(this);
    }

    internal readonly World.EntityStateCaptureEnumerator GetEntityStateEnumerator()
    {
        ValidateCurrent();
        return _world.CreateEntityStateEnumerator(this);
    }

    internal readonly World.IdentityHistoryCapture GetIdentityHistoryCapture()
    {
        ValidateCurrent();
        return new World.IdentityHistoryCapture(_world, this);
    }

    internal readonly int GetResourceStateCount()
    {
        ValidateCurrent();
        return _world.Registration.Resources.Length;
    }

    internal readonly World.ResourceStateCaptureEnumerator GetResourceStateEnumerator()
    {
        ValidateCurrent();
        return _world.CreateResourceStateEnumerator(this);
    }

    internal readonly int GetSystemStateCount()
    {
        ValidateCurrent();
        var count = 0;

        foreach (var registration in _world.Registration.Systems)
        {
            count += registration.Factory.Descriptor.StateSchema.HasValue ? 1 : 0;
        }

        return count;
    }

    internal readonly World.SystemStateCaptureEnumerator GetSystemStateEnumerator()
    {
        ValidateCurrent();
        return _world.CreateSystemStateEnumerator(this);
    }

    internal readonly bool IsBoundTo(World world)
        => ReferenceEquals(_world, world);

    internal readonly EbinWriteStatus MeasureClockRecord(
        out WorldPersistenceClockWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return WorldPersistenceClockWriter.MeasureWorkspace(
            this,
            WorldPersistenceEbinLimits.Create(_world.Options),
            out workspace,
            out diagnostic,
            cancellationToken);
    }

    internal readonly EbinWriteStatus MeasureConfigurationRecord(
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ValidateCurrent();
        return WorldPersistenceConfigurationWriter.Measure(
            _world.Options,
            WorldPersistenceEbinLimits.Create(_world.Options),
            out recordBytes,
            out diagnostic);
    }

    internal readonly EbinWriteStatus MeasureEcsRecord(
        ref WorldPersistenceEcsBuffers buffers,
        out WorldPersistenceEcsWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return WorldPersistenceEcsWriter.MeasureWorkspace(
            this,
            ref buffers,
            WorldPersistenceEbinLimits.Create(_world.Options),
            out workspace,
            out diagnostic,
            cancellationToken);
    }

    internal readonly EbinWriteStatus MeasureEntityRecord(
        in World.EntityStateCaptureEnumerator entity,
        scoped Span<EbinPayloadSlice> componentSlices,
        scoped Span<EbinPayloadSlice> tagSlices,
        scoped Span<EbinPayloadSlice> transientSchemaSlices,
        out WorldPersistenceEntityWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();

        if (!entity.IsBoundTo(_world))
        {
            throw new ArgumentException("The Entity capture belongs to another World.", nameof(entity));
        }

        return WorldPersistenceEntityWriter.Measure(
            this,
            in entity,
            WorldPersistenceEbinLimits.Create(_world.Options),
            componentSlices,
            tagSlices,
            transientSchemaSlices,
            out workspace,
            out diagnostic,
            cancellationToken);
    }

    internal readonly EbinWriteStatus MeasureIdentityBranchRecord(
        in World.IdentityBranchCaptureEnumerator branch,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ValidateCurrent();

        if (!branch.IsBoundTo(_world))
        {
            throw new ArgumentException("The identity-branch capture belongs to another World.", nameof(branch));
        }

        return WorldPersistenceIdentityBranchWriter.Measure(
            in branch,
            WorldPersistenceEbinLimits.Create(_world.Options),
            out recordBytes,
            out diagnostic);
    }

    internal readonly EbinWriteStatus MeasureIdentityHistoryRecord(
        in World.IdentityHistoryCapture history,
        scoped Span<EbinPayloadSlice> branchSlices,
        scoped Span<EbinPayloadSlice> pageSlices,
        scoped Span<Guid> pageStableIds,
        scoped Span<uint> pageMetadata,
        scoped Span<byte> pagePayloadStorage,
        out WorldPersistenceIdentityHistoryWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ValidateCurrent();

        if (!history.IsBoundTo(_world))
        {
            throw new ArgumentException("The identity-history capture belongs to another World.", nameof(history));
        }

        return WorldPersistenceIdentityHistoryWriter.Measure(
            in history,
            WorldPersistenceEbinLimits.Create(_world.Options),
            branchSlices,
            pageSlices,
            pageStableIds,
            pageMetadata,
            pagePayloadStorage,
            out workspace,
            out diagnostic);
    }

    internal readonly EbinWriteStatus MeasureIdentityPageRecord(
        in World.IdentityHistoryCapture history,
        int pageIndex,
        scoped Span<Guid> stableIds,
        scoped Span<uint> metadata,
        scoped Span<byte> payloadStorage,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ValidateCurrent();

        if (!history.IsBoundTo(_world))
        {
            throw new ArgumentException("The identity-history capture belongs to another World.", nameof(history));
        }

        return WorldPersistenceIdentityPageWriter.Measure(
            in history,
            WorldPersistenceEbinLimits.Create(_world.Options),
            pageIndex,
            stableIds,
            metadata,
            payloadStorage,
            out recordBytes,
            out diagnostic);
    }

    internal readonly bool TryReserveAdmissionBytes(
        long bytes,
        out WorldPersistenceAdmissionReservation? reservation)
    {
        ValidateCurrent();
        return _world.TryReservePersistenceAdmissionBytes(bytes, out reservation);
    }

    internal readonly void ValidateCurrent()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(WorldPersistenceCaptureLease));
        }

        _world.ValidatePersistenceCaptureLease(this);
    }

    internal readonly nint WriteAttachmentStateRecord(
        int attachmentIndex,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return _world.WriteAttachmentStateRecord(this, attachmentIndex, destination, cancellationToken);
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

    internal readonly EbinWriteStatus WriteConfigurationRecord(
        WorldPersistenceAdmissionReservation reservation,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ValidateCurrent();
        ArgumentNullException.ThrowIfNull(reservation);
        return reservation.WriteConfigurationRecord(
            _world,
            _world.Options,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal readonly EbinWriteStatus WriteEcsRecord(
        WorldPersistenceAdmissionReservation reservation,
        ref WorldPersistenceEcsBuffers buffers,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        ArgumentNullException.ThrowIfNull(reservation);
        return reservation.WriteEcsRecord(
            _world,
            this,
            ref buffers,
            out bytesWritten,
            out diagnostic,
            cancellationToken);
    }

    internal readonly EbinWriteStatus WriteEntityRecord(
        WorldPersistenceAdmissionReservation reservation,
        in World.EntityStateCaptureEnumerator entity,
        scoped Span<EbinPayloadSlice> componentSlices,
        scoped Span<byte> componentStorage,
        scoped Span<EbinPayloadSlice> tagSlices,
        scoped Span<byte> tagStorage,
        scoped Span<EbinPayloadSlice> transientSchemaSlices,
        scoped Span<byte> transientSchemaStorage,
        scoped Span<byte> childCandidateStorage,
        scoped Span<byte> payloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        ArgumentNullException.ThrowIfNull(reservation);

        if (!entity.IsBoundTo(_world))
        {
            throw new ArgumentException("The Entity capture belongs to another World.", nameof(entity));
        }

        return reservation.WriteEntityRecord(
            _world,
            this,
            in entity,
            componentSlices,
            componentStorage,
            tagSlices,
            tagStorage,
            transientSchemaSlices,
            transientSchemaStorage,
            childCandidateStorage,
            payloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic,
            cancellationToken);
    }

    internal readonly EbinWriteStatus WriteIdentityBranchRecord(
        WorldPersistenceAdmissionReservation reservation,
        in World.IdentityBranchCaptureEnumerator branch,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ValidateCurrent();
        ArgumentNullException.ThrowIfNull(reservation);
        return reservation.WriteIdentityBranchRecord(
            _world,
            in branch,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal readonly EbinWriteStatus WriteIdentityHistoryRecord(
        WorldPersistenceAdmissionReservation reservation,
        in World.IdentityHistoryCapture history,
        scoped Span<EbinPayloadSlice> branchSlices,
        scoped Span<byte> branchStorage,
        scoped Span<EbinPayloadSlice> pageSlices,
        scoped Span<byte> pageStorage,
        scoped Span<Guid> pageStableIds,
        scoped Span<uint> pageMetadata,
        scoped Span<byte> pagePayloadStorage,
        scoped Span<byte> childCandidateStorage,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ValidateCurrent();
        ArgumentNullException.ThrowIfNull(reservation);
        return reservation.WriteIdentityHistoryRecord(
            _world,
            in history,
            branchSlices,
            branchStorage,
            pageSlices,
            pageStorage,
            pageStableIds,
            pageMetadata,
            pagePayloadStorage,
            childCandidateStorage,
            rootPayloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal readonly EbinWriteStatus WriteIdentityPageRecord(
        WorldPersistenceAdmissionReservation reservation,
        in World.IdentityHistoryCapture history,
        int pageIndex,
        scoped Span<Guid> stableIds,
        scoped Span<uint> metadata,
        scoped Span<byte> payloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ValidateCurrent();
        ArgumentNullException.ThrowIfNull(reservation);
        return reservation.WriteIdentityPageRecord(
            _world,
            in history,
            pageIndex,
            stableIds,
            metadata,
            payloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal readonly nint WriteResourceStateRecord(
        int resourceIndex,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return _world.WriteResourceStateRecord(this, resourceIndex, destination, cancellationToken);
    }

    internal readonly nint WriteSystemStateRecord(
        int systemIndex,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        ValidateCurrent();
        return _world.WriteSystemStateRecord(this, systemIndex, destination, cancellationToken);
    }
}
