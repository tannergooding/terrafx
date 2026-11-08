// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using TerraFX;

namespace TerraFX.Input;

public sealed partial class InputRouter
{
    internal static InputReplayResult PlayRecording(InputRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (recording.Version != InputRecording.CurrentVersion)
        {
            throw new InvalidDataException($"Input recording version {recording.Version} is not supported.");
        }

        var router = CreateReplayRouter(recording);
        var boundaries = new Dictionary<long, InputRecordingEntry>();
        var expectedBoundaryOrdinal = recording.Baseline.HostControlBoundaryOrdinal;

        foreach (var entry in recording.Entries)
        {
            if ((entry.Kind == InputRecordingEntryKind.None)
                || !Enum.IsDefined(entry.Kind)
                || (entry.BoundaryOrdinal < recording.Baseline.HostControlBoundaryOrdinal)
                || (entry.HostTimestamp < 0)
                || (entry.HostSequence < 0)
                || ((entry.HostSequence == 0)
                    && (entry.Kind is not InputRecordingEntryKind.HostControlBoundary
                        and not InputRecordingEntryKind.SimulationTick)))
            {
                throw new InvalidDataException("The input recording contains an invalid kind, timestamp, sequence, or boundary ordinal.");
            }

            if (entry.Kind == InputRecordingEntryKind.HostControlBoundary)
            {
                expectedBoundaryOrdinal = checked(expectedBoundaryOrdinal + 1);

                if ((entry.BoundaryOrdinal != expectedBoundaryOrdinal)
                    || !boundaries.TryAdd(entry.BoundaryOrdinal, entry))
                {
                    throw new InvalidDataException("The input recording contains a missing or duplicate host-control boundary.");
                }
            }
        }

        var snapshots = ImmutableArray.CreateBuilder<InputActionSnapshot>();
        var terminalTransitions = ImmutableArray<InputActionTransition>.Empty;
        var textBatches = ImmutableArray.CreateBuilder<TextInputBatch>();
        var recipientDisposed = false;

        foreach (var entry in recording.Entries)
        {
            if (recipientDisposed)
            {
                throw new InvalidDataException("The input recording contains records after recipient disposal.");
            }

            if (entry.Kind == InputRecordingEntryKind.ClockSegment)
            {
                if (entry.ClockSegment is not { IsValid: true } segment
                    || (recording.RecipientKind != InputRecipientKind.SimulationTimeline))
                {
                    throw new InvalidDataException("The input recording contains an invalid simulation clock segment.");
                }

                router.SetSimulationClockSegment(in segment);
                continue;
            }

            if (entry.Kind is not InputRecordingEntryKind.DeviceAssignment
                and not InputRecordingEntryKind.TextBatch
                and not InputRecordingEntryKind.SensitiveTextRedacted
                and not InputRecordingEntryKind.TextOmitted
                and not InputRecordingEntryKind.Discontinuity
                && (entry.BoundaryOrdinal > router._inputControlBoundaryOrdinal))
            {
                var boundary = boundaries.TryGetValue(entry.BoundaryOrdinal, out var recordedBoundary)
                    ? recordedBoundary
                    : null;
                var hostTimestamp = boundary?.HostTimestamp ?? entry.HostTimestamp;
                var nextOpenTick = boundary?.AssignedTick
                    ?? entry.AssignedTick
                    ?? router._nextOpenTick;
                router.BeginHostControlBoundary(nextOpenTick, hostTimestamp);
            }

            switch (entry.Kind)
            {
                case InputRecordingEntryKind.PhysicalInput:
                {
                    var inputEvent = entry.PhysicalEvent
                        ?? throw new InvalidDataException("A physical input record has no event payload.");
                    InputRoutingReceipt receipt;

                    if (recording.RecipientKind == InputRecipientKind.HostLiveUI)
                    {
                        receipt = router.EnqueueHostPhysicalEvent(inputEvent, out _);
                    }
                    else
                    {
                        receipt = router.EnqueuePhysicalEvent(
                            inputEvent,
                            entry.PlayerSlot,
                            entry.UiCapturedClasses,
                            out _);
                    }

                    if ((receipt.Status != InputRoutingStatus.Applied)
                        || (receipt.AssignedTick != entry.AssignedTick)
                        || (receipt.Late != entry.Late))
                    {
                        throw new InvalidDataException("A recorded physical input event could not be admitted with its original recipient assignment.");
                    }

                    break;
                }

                case InputRecordingEntryKind.HeadlessInjection:
                {
                    if (entry.Injection is not { } injection
                        || (recording.RecipientKind != InputRecipientKind.SimulationTimeline)
                        || (entry.AssignedTick is not { } assignedTick))
                    {
                        throw new InvalidDataException("A headless injection record is missing its simulation payload or tick.");
                    }

                    var receipt = router.EnqueueHeadlessInjection(
                        injection,
                        entry.HostTimestamp,
                        entry.HostSequence,
                        assignedTick,
                        entry.Late);

                    if ((receipt.Status != InputRoutingStatus.Applied)
                        || (receipt.AssignedTick != entry.AssignedTick)
                        || (receipt.Late != entry.Late))
                    {
                        throw new InvalidDataException("A recorded headless injection could not be admitted with its original tick assignment.");
                    }

                    break;
                }

                case InputRecordingEntryKind.Control:
                {
                    ReplayControl(router, entry);
                    break;
                }

                case InputRecordingEntryKind.MapRegistered:
                case InputRecordingEntryKind.MapReplaced:
                {
                    ReplayMapChange(router, entry);
                    break;
                }

                case InputRecordingEntryKind.HostControlBoundary:
                {
                    _ = router.ServiceInputControlBoundary();

                    if (recording.RecipientKind == InputRecipientKind.HostLiveUI)
                    {
                        snapshots.Add(router.GetHostActionSnapshot());
                    }

                    break;
                }

                case InputRecordingEntryKind.SimulationTick:
                {
                    if ((recording.RecipientKind != InputRecipientKind.SimulationTimeline)
                        || (entry.AssignedTick is not { } assignedTick))
                    {
                        throw new InvalidDataException("A simulation-tick record has no assigned timeline tick.");
                    }

                    snapshots.Add(router.PublishSimulationActionSnapshot(assignedTick));
                    break;
                }

                case InputRecordingEntryKind.TextBatch:
                {
                    textBatches.Add(entry.TextBatch
                        ?? throw new InvalidDataException("A text recording entry has no batch payload."));
                    break;
                }

                case InputRecordingEntryKind.SensitiveTextRedacted:
                case InputRecordingEntryKind.TextOmitted:
                case InputRecordingEntryKind.Discontinuity:
                case InputRecordingEntryKind.DeviceAssignment:
                case InputRecordingEntryKind.PausedInputDiscard:
                case InputRecordingEntryKind.NoEligibleTickDiscard:
                {
                    break;
                }

                case InputRecordingEntryKind.RecipientDisposed:
                {
                    terminalTransitions = router.DisposeReplayRecipient(entry.HostSequence);
                    recipientDisposed = true;
                    break;
                }

                default:
                {
                    throw new InvalidDataException("The input recording contains an unsupported entry kind.");
                }
            }
        }

        return new InputReplayResult(
            recording.RecipientKind,
            recording.IsComplete,
            snapshots.ToImmutable(),
            terminalTransitions,
            textBatches.ToImmutable());
    }

    private static InputRouter CreateReplayRouter(InputRecording recording)
    {
        var baseline = recording.Baseline;

        if (!Enum.IsDefined(recording.RecipientKind)
            || (baseline.RecipientId == 0)
            || (recording.RecipientKind == InputRecipientKind.HostLiveUI != baseline.Window.HasValue)
            || (baseline.Window is { IsValid: false })
            || (baseline.LastInsertionSequence < 0)
            || (baseline.NextOpenTick < 0)
            || (baseline.LastAssignedInputTick < -1)
            || (baseline.HostControlTimestamp < 0)
            || (baseline.LastSeenHostSequence < 0)
            || (baseline.LastSeenHostTimestamp < 0)
            || (baseline.ControlCommandsInBoundary < 0)
            || (baseline.ControlCommandsInBoundary > MaximumControlCommandsPerBoundary)
            || (baseline.HostControlBoundaryOrdinal < 0)
            || baseline.InitialMaps.IsDefault)
        {
            throw new InvalidDataException("The input recording baseline is invalid.");
        }

        if ((recording.RecipientKind == InputRecipientKind.HostLiveUI)
            && baseline.ClockSegment.IsValid)
        {
            throw new InvalidDataException("The input recording baseline has an incompatible simulation clock segment.");
        }

        var router = new InputRouter(
            recording.RecipientKind,
            baseline.RecipientId,
            Environment.CurrentManagedThreadId,
            HostEventSequenceLease.CreateReplayLease(),
            baseline.Window) {
            _lastInsertionSequence = baseline.LastInsertionSequence,
            _nextOpenTick = baseline.NextOpenTick,
            _lastAssignedInputTick = baseline.LastAssignedInputTick,
            _simulationClockSegment = baseline.ClockSegment,
            _hostControlTimestamp = baseline.HostControlTimestamp,
            _lastSeenHostSequence = baseline.LastSeenHostSequence,
            _lastSeenHostTimestamp = baseline.LastSeenHostTimestamp,
            _controlCommandsInBoundary = baseline.ControlCommandsInBoundary,
            _inputControlBoundaryOrdinal = baseline.HostControlBoundaryOrdinal
        };
        var mapIds = new HashSet<Guid>();

        foreach (var mapSnapshot in baseline.InitialMaps)
        {
            var map = mapSnapshot.Map;
            var validation = InputActionMapValidator.Validate(map);

            if (validation is not null
                || !mapIds.Add(map.MapId)
                || !IsValidRecordedContextState(
                    map,
                    mapSnapshot.ActiveContextMask,
                    mapSnapshot.ContextInsertionSequences,
                    baseline.LastInsertionSequence))
            {
                throw new InvalidDataException("The input recording contains an invalid initial action-map snapshot.");
            }

            var mapState = new InputActionMapState(
                map,
                mapSnapshot.ActiveContextMask,
                [.. mapSnapshot.ContextInsertionSequences]);
            router.InitializeMapState(mapState);
            router._maps.Add(map.MapId, mapState);
        }

        router.RebuildOrderedContexts();
        router.PublishHostActionSnapshot();
        return router;
    }

    private static bool IsValidRecordedContextState(
        InputActionMapV1 map,
        ulong activeContextMask,
        ImmutableArray<long> insertionSequences,
        long lastInsertionSequence)
    {
        if (insertionSequences.IsDefault
            || (insertionSequences.Length != 64)
            || (lastInsertionSequence < 0)
            || ((activeContextMask & ~GetDeclaredContextMask(map)) != 0))
        {
            return false;
        }

        for (var bit = 0; bit < insertionSequences.Length; bit++)
        {
            var insertionSequence = insertionSequences[bit];
            var isActive = (activeContextMask & (1UL << bit)) != 0;

            if (isActive
                ? (insertionSequence <= 0) || (insertionSequence > lastInsertionSequence)
                : (insertionSequence != 0))
            {
                return false;
            }
        }

        return true;
    }

    private static void ReplayControl(InputRouter router, InputRecordingEntry entry)
    {
        if (entry.ControlKind is not { } controlKind || !Enum.IsDefined(controlKind))
        {
            throw new InvalidDataException("A recorded input control has an invalid command kind.");
        }

        InputRoutingReceipt receipt;

        switch ((InputControlCommandKind)controlKind)
        {
            case InputControlCommandKind.CancelInput:
            {
                if (entry.CancellationReason is not { } cancellationReason)
                {
                    throw new InvalidDataException("A recorded cancellation has no reason.");
                }

                receipt = router.CancelInputState(
                    cancellationReason,
                    entry.InputClasses,
                    entry.PlayerSlot,
                    entry.HostTimestamp,
                    entry.HostSequence,
                    out _);
                break;
            }

            case InputControlCommandKind.ReconcileInput:
            {
                receipt = router.ReconcileInputState(
                    entry.ReconciliationInputs,
                    entry.InputClasses,
                    entry.PlayerSlot,
                    entry.HostTimestamp,
                    entry.HostSequence,
                    out _,
                    entry.PhysicalReconciliationInputs);
                break;
            }

            case InputControlCommandKind.ResetUiDisposition:
            {
                receipt = router.EnqueueUiDispositionReset(entry.HostTimestamp, entry.HostSequence);
                break;
            }

            case InputControlCommandKind.RemovePhysicalDevice:
            {
                if (entry.PhysicalSourceDevice is not { } device)
                {
                    throw new InvalidDataException("A recorded physical-device removal has no source device.");
                }

                receipt = router.QueuePhysicalDeviceRemoval(
                    device,
                    entry.HostTimestamp,
                    entry.HostSequence,
                    out _);
                break;
            }

            case InputControlCommandKind.SetActiveContextMask:
            {
                receipt = router.EnqueueRecordedContextMask(entry);
                break;
            }

            default:
            {
                throw new InvalidDataException("A recorded input control has an unsupported command kind.");
            }
        }

        if (receipt.Status != InputRoutingStatus.Applied)
        {
            throw new InvalidDataException("A recorded input-control command was refused during recipient replay.");
        }
    }

    private static void ReplayMapChange(InputRouter router, InputRecordingEntry entry)
    {
        var map = entry.ActionMap
            ?? throw new InvalidDataException("A recorded action-map change has no map.");
        var validation = InputActionMapValidator.Validate(map);

        if ((validation is not null)
            || !Enum.IsDefined(entry.Kind)
            || ((entry.Kind == InputRecordingEntryKind.MapReplaced)
                && (entry.ExpectedRevision <= 0))
            || (entry.HostSequence <= router._lastSeenHostSequence))
        {
            throw new InvalidDataException("A recorded action-map change is invalid or out of sequence.");
        }

        if ((entry.BoundaryOrdinal != checked(router._inputControlBoundaryOrdinal + 1))
            || (router._inputInbox.Count != 0)
            || (router._inputControlInbox.Count != 0))
        {
            throw new InvalidDataException("A recorded action-map change is outside its original recipient boundary.");
        }

        if (entry.Kind == InputRecordingEntryKind.MapRegistered)
        {
            if (router._maps.ContainsKey(map.MapId)
                || (entry.NewMask != map.InitialActiveContextMask)
                || !IsValidRecordedContextState(
                    map,
                    entry.NewMask,
                    entry.ContextInsertionSequences,
                    entry.LastInsertionSequence))
            {
                throw new InvalidDataException("A recorded action-map registration conflicts with its baseline.");
            }

            var priorEligibility = router.CaptureBindingEligibility();
            var mapState = new InputActionMapState(
                map,
                entry.NewMask,
                [.. entry.ContextInsertionSequences]);
            router.InitializeMapState(mapState);
            router._maps.Add(map.MapId, mapState);
            router.RebuildOrderedContexts();
            _ = router.RefreshContextEligibility(
                priorEligibility,
                entry.HostSequence,
                entry.AssignedTick,
                deferTransitions: router._recipientKind == InputRecipientKind.SimulationTimeline);
            router._lastInsertionSequence = entry.LastInsertionSequence;
        }
        else
        {
            if (!router._maps.TryGetValue(map.MapId, out var current)
                || (current.Map.Revision != entry.ExpectedRevision)
                || (map.InitialActiveContextMask != current.Map.InitialActiveContextMask))
            {
                throw new InvalidDataException("A recorded action-map replacement does not match its prior revision.");
            }

            var replacementState = CreateReplacementMapState(current, map);

            if (!IsValidRecordedContextState(
                map,
                replacementState.ActiveContextMask,
                entry.ContextInsertionSequences,
                entry.LastInsertionSequence)
                || (entry.NewMask != replacementState.ActiveContextMask)
                || !replacementState.ContextInsertionSequences.AsSpan().SequenceEqual(entry.ContextInsertionSequences.AsSpan()))
            {
                throw new InvalidDataException("A recorded action-map replacement has inconsistent context state.");
            }

            var priorEligibility = router.CaptureBindingEligibility();
            router.InitializeMapState(replacementState);
            var reboundActions = new List<PendingReboundAction>();

            foreach (var action in current.OrderedActions)
            {
                var actionState = current.ActionStates[action.ActionId];
                var isDown = actionState.IsDownAtEnd;

                if (replacementState.ActionStates.TryGetValue(action.ActionId, out var replacementActionState))
                {
                    replacementActionState.PendingTransitions.AddRange(actionState.PendingTransitions);

                    if (isDown)
                    {
                        reboundActions.Add(new PendingReboundAction(
                            current.Map.MapId,
                            action.ActionId,
                            current.Map.Revision,
                            action.Kind,
                            actionState.AxisX,
                            actionState.AxisY,
                            [],
                            isDown,
                            entry.HostSequence,
                            entry.AssignedTick));
                    }
                }
                else if (isDown || (actionState.PendingTransitions.Count != 0))
                {
                    reboundActions.Add(new PendingReboundAction(
                        current.Map.MapId,
                        action.ActionId,
                        current.Map.Revision,
                        action.Kind,
                        actionState.AxisX,
                        actionState.AxisY,
                        [.. actionState.PendingTransitions],
                        isDown,
                        entry.HostSequence,
                        entry.AssignedTick));
                }
            }

            router._pendingReboundActions.AddRange(reboundActions);
            router._maps[map.MapId] = replacementState;
            router.RebuildOrderedContexts();
            _ = router.RefreshContextEligibility(
                priorEligibility,
                entry.HostSequence,
                entry.AssignedTick,
                deferTransitions: router._recipientKind == InputRecipientKind.SimulationTimeline);
            router._lastInsertionSequence = entry.LastInsertionSequence;
        }

        router.RecordHostControlSequence(entry.HostSequence);
        router._controlCommandsInBoundary++;
        router.PublishHostActionSnapshot();
    }

    /// <summary>Starts a bounded in-memory recording for this exact input recipient.</summary>
    /// <param name="recordTextBatches">Whether to retain nonsensitive text-session batches.</param>
    /// <returns>The owner-thread recorder.</returns>
    /// <exception cref="InvalidOperationException">The router is not at a neutral boundary or already has a recorder.</exception>
    /// <exception cref="ObjectDisposedException">This router has been closed by its host.</exception>
    public InputRecorder StartRecording(bool recordTextBatches = false)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();

        if (_inputRecorder is not null)
        {
            throw new InvalidOperationException("This input router already has an active recorder.");
        }

        if (_isPaused
            || _pauseControlPending
            || _inputOverflowed
            || (_inputInbox.Count != 0)
            || (_inputControlInbox.Count != 0)
            || (_activeTextSession is not null)
            || (_pendingReboundActions.Count != 0)
            || (_terminalActionStates.Count != 0)
            || (_unreconciledControls.Count != 0)
            || (_uiConsumedControls.Count != 0)
            || (_consumptionLeases.Count != 0)
            || _digitalControlStates.Values.Any(static isDown => isDown)
            || _scalarControlValues.Values.Any(static value => value != 0.0f)
            || _vectorControlValues.Values.Any(static value => (value.X != 0.0f) || (value.Y != 0.0f)))
        {
            throw new InvalidOperationException("Input recording must start at a neutral recipient boundary with no pending work.");
        }

        foreach (var mapState in _maps.Values)
        {
            foreach (var actionState in mapState.ActionStates.Values)
            {
                if (actionState.IsDownAtEnd
                    || (actionState.PendingTransitions.Count != 0)
                    || (actionState.Transitions.Count != 0))
                {
                    throw new InvalidOperationException("Input recording cannot start while an action is active or has pending transitions.");
                }
            }
        }

        var initialMaps = ImmutableArray.CreateBuilder<InputRecordingMapSnapshot>(_maps.Count);

        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId))
        {
            initialMaps.Add(new InputRecordingMapSnapshot(
                mapState.Map,
                mapState.ActiveContextMask,
                [.. mapState.ContextInsertionSequences]));
        }

        var baseline = new InputRecordingBaseline(
            _recipientId,
            _window,
            initialMaps.ToImmutable(),
            _lastInsertionSequence,
            _nextOpenTick,
            _lastAssignedInputTick,
            _simulationClockSegment,
            _hostControlTimestamp,
            _lastSeenHostSequence,
            _lastSeenHostTimestamp,
            _controlCommandsInBoundary,
            _inputControlBoundaryOrdinal);
        var recorder = new InputRecorder(this, recordTextBatches, baseline);
        _inputRecorder = recorder;
        return recorder;
    }

    internal InputRecording CompleteRecording(InputRecorder recorder, bool allowPending = false)
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(recorder);

        if (!ReferenceEquals(_inputRecorder, recorder) || recorder.IsDisposed)
        {
            throw new InvalidOperationException("The recorder is not active on this input router.");
        }

        if (!allowPending && ((_inputInbox.Count != 0) || (_inputControlInbox.Count != 0)))
        {
            throw new InvalidOperationException("Input recording can complete only after accepted recipient work reaches its boundary.");
        }

        if (_inputOverflowed || (_unreconciledControls.Count != 0))
        {
            recorder.MarkDiscontinuity(_lastSeenHostSequence);
        }

        var recording = new InputRecording(
            _recipientKind,
            recorder.Baseline,
            recorder.Entries,
            recorder.IsComplete,
            recorder.HasTextOmission,
            recorder.HasSensitiveTextRedaction,
            recorder.FirstMissingHostSequence,
            recorder.LastMissingHostSequence);
        _inputRecorder = null;
        recorder.MarkCompleted(recording);
        return recording;
    }

    internal void DiscardRecording(InputRecorder recorder)
    {
        EnsureOwnerThread();

        if (ReferenceEquals(_inputRecorder, recorder))
        {
            _inputRecorder = null;
            recorder.MarkDisposed();
        }
    }

    internal void MarkRecordingDiscontinuity(long hostSequence)
        => _inputRecorder?.MarkDiscontinuity(hostSequence);

    internal void RecordDeviceAssignment(
        InputDeviceHandle device,
        ushort playerSlot,
        bool isAssigned,
        long hostTimestamp,
        long hostSequence)
    {
        EnsureOwnerThread();

        if (!device.IsValid)
        {
            throw new ArgumentException("A valid physical-device generation is required.", nameof(device));
        }

        var recorder = _inputRecorder;

        if (recorder is null)
        {
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostSequence);

        if ((hostSequence < _lastSeenHostSequence) || (hostTimestamp < _lastSeenHostTimestamp))
        {
            recorder.MarkDiscontinuity(hostSequence);
            return;
        }

        recorder.Record(new InputRecordingEntry(
            InputRecordingEntryKind.DeviceAssignment,
            hostTimestamp,
            hostSequence,
            (_recipientKind == InputRecipientKind.SimulationTimeline) ? _nextOpenTick : null,
            checked(_inputControlBoundaryOrdinal + 1),
            playerSlot: playerSlot,
            device: device,
            isAssigned: isAssigned));
    }

    internal void RecordMapChange(
        InputRecordingEntryKind kind,
        InputActionMapV1 map,
        long hostSequence,
        long? assignedTick,
        long expectedRevision = 0)
    {
        _inputRecorder?.Record(new InputRecordingEntry(
            kind,
            _hostControlTimestamp,
            hostSequence,
            assignedTick,
            checked(_inputControlBoundaryOrdinal + 1),
            expectedRevision: expectedRevision,
            actionMap: map,
            newMask: _maps[map.MapId].ActiveContextMask,
            lastInsertionSequence: _lastInsertionSequence,
            contextInsertionSequences: [.. _maps[map.MapId].ContextInsertionSequences]));
    }

    internal void RecordNoEligibleInjectionDiscard(InputInjection injection, long hostTimestamp, long hostSequence)
    {
        _inputRecorder?.Record(new InputRecordingEntry(
            InputRecordingEntryKind.NoEligibleTickDiscard,
            hostTimestamp,
            hostSequence,
            assignedTick: null,
            checked(_inputControlBoundaryOrdinal + 1),
            injection: injection));
    }

    internal void RecordNoEligibleTickDiscard(
        PhysicalInputEvent inputEvent,
        ushort? playerSlot,
        InputClass uiCapturedClasses)
    {
        _inputRecorder?.Record(new InputRecordingEntry(
            InputRecordingEntryKind.NoEligibleTickDiscard,
            inputEvent.HostTimestamp,
            inputEvent.HostSequence,
            assignedTick: null,
            checked(_inputControlBoundaryOrdinal + 1),
            physicalEvent: inputEvent,
            uiCapturedClasses: uiCapturedClasses,
            playerSlot: playerSlot));
    }

    internal void RecordPausedInjectionDiscard(InputInjection injection, long hostTimestamp, long hostSequence)
    {
        _inputRecorder?.Record(new InputRecordingEntry(
            InputRecordingEntryKind.PausedInputDiscard,
            hostTimestamp,
            hostSequence,
            assignedTick: null,
            checked(_inputControlBoundaryOrdinal + 1),
            injection: injection));
    }

    internal void RecordPausedPhysicalDiscard(
        PhysicalInputEvent inputEvent,
        ushort? playerSlot,
        InputClass uiCapturedClasses)
    {
        _inputRecorder?.Record(new InputRecordingEntry(
            InputRecordingEntryKind.PausedInputDiscard,
            inputEvent.HostTimestamp,
            inputEvent.HostSequence,
            assignedTick: null,
            checked(_inputControlBoundaryOrdinal + 1),
            physicalEvent: inputEvent,
            uiCapturedClasses: uiCapturedClasses,
            playerSlot: playerSlot));
    }

    internal void RecordTextInputBatch(TextInputBatch batch, bool isSensitive)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(batch);
        _inputRecorder?.RecordText(batch, isSensitive, checked(_inputControlBoundaryOrdinal + 1));
    }

    private ImmutableArray<InputActionTransition> DisposeReplayRecipient(long hostSequence)
    {
        _inputInbox.Clear();
        _inputControlInbox.Clear();
        _inputInboxTail = null;
        return DisposeAndGetTransitions(hostSequence);
    }

    private void EnqueueInputControlEnvelope(InputControlEnvelope envelope)
    {
        _inputControlInbox.Enqueue(envelope);
        _inputRecorder?.Record(new InputRecordingEntry(
            InputRecordingEntryKind.Control,
            envelope.HostTimestamp,
            envelope.HostSequence,
            envelope.AssignedTick,
            checked(_inputControlBoundaryOrdinal + 1),
            mapId: envelope.MapId,
            expectedRevision: envelope.ExpectedRevision,
            newMask: envelope.NewMask,
            lastInsertionSequence: envelope.LastInsertionSequence,
            controlKind: (InputRecordingControlKind)envelope.Kind,
            inputClasses: envelope.InputClasses,
            playerSlot: envelope.PlayerSlot,
            cancellationReason: envelope.CancellationReason,
            reconciliationInputs: envelope.ReconciliationInputs,
            physicalReconciliationInputs: envelope.PhysicalReconciliationInputs,
            physicalSourceDevice: envelope.PhysicalSourceDevice,
            contextInsertionSequences: [.. envelope.ContextInsertionSequences]));
    }

    private InputRoutingReceipt EnqueueRecordedContextMask(InputRecordingEntry entry)
    {
        if (entry.MapId is not { } mapId
            || !_maps.TryGetValue(mapId, out var mapState)
            || (mapState.Map.Revision != entry.ExpectedRevision)
            || ((entry.NewMask & ~GetDeclaredContextMask(mapState.Map)) != 0)
            || (entry.ContextInsertionSequences.Length != 64)
            || (entry.HostSequence <= _lastSeenHostSequence))
        {
            throw new InvalidDataException("A recorded context-mask change is invalid or out of sequence.");
        }

        if ((entry.HostTimestamp < _hostControlTimestamp)
            || (entry.HostTimestamp < _lastSeenHostTimestamp))
        {
            throw new InvalidDataException("A recorded context-mask change regresses the recipient host timestamp.");
        }

        if (!CanAdmitControlCommand()
            || (_inputControlInbox.Count >= MaximumControlCommandsPerBoundary))
        {
            throw new InvalidDataException("A recorded context-mask change exceeds recipient control capacity.");
        }

        EnqueueInputControlEnvelope(new InputControlEnvelope(
            InputControlCommandKind.SetActiveContextMask,
            mapId,
            entry.ExpectedRevision,
            entry.NewMask,
            [.. entry.ContextInsertionSequences],
            entry.LastInsertionSequence,
            InputClass.None,
            null,
            null,
            [],
            entry.HostTimestamp,
            entry.HostSequence,
            entry.AssignedTick));
        RecordHostControlSequence(entry.HostSequence);
        _lastSeenHostTimestamp = entry.HostTimestamp;
        _controlCommandsInBoundary++;
        return new InputRoutingReceipt(
            InputRoutingStatus.Applied,
            _recipientId,
            entry.HostSequence,
            entry.AssignedTick,
            rejection: null);
    }

    private void RecordInputEnvelope(InputEnvelope envelope, long? assignedTick, bool late)
        => _inputRecorder?.Record(new InputRecordingEntry(
            (envelope.PhysicalEvent is null)
                ? InputRecordingEntryKind.HeadlessInjection
                : InputRecordingEntryKind.PhysicalInput,
            envelope.HostTimestamp,
            envelope.HostSequence,
            assignedTick,
            checked(_inputControlBoundaryOrdinal + 1),
            late,
            physicalEvent: envelope.PhysicalEvent,
            injection: envelope.Injection,
            uiCapturedClasses: envelope.UiCapturedClasses,
            playerSlot: GetInputClass(envelope.Control) == InputClass.Gamepad
                ? envelope.Control.PlayerSlot
                : null));
}
