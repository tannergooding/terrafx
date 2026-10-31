// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using TerraFX;

namespace TerraFX.Input;

/// <summary>Owns isolated physical, context, and action state for one input recipient.</summary>
public sealed partial class InputRouter
{
    private const int MaximumActiveContexts = 256;
    private const int MaximumControlCommandsPerBoundary = 256;
    private const int MaximumMaps = 16;

    private TextInputSession? _activeTextSession;
    private int _controlCommandsInBoundary;
    private InputActionSnapshot _hostActionSnapshot;
    private int _isDisposed;
    private bool _isPaused;
    private long _lastInsertionSequence;
    private readonly Dictionary<Guid, InputActionMapState> _maps;
    private long _nextOpenTick;
    private readonly int _ownerThreadId;
    private bool _pauseControlPending;
    private readonly uint _recipientId;
    private readonly InputRecipientKind _recipientKind;
    private readonly HostEventSequenceLease _sequenceLease;
    private InputActionSnapshot _simulationActionSnapshot;
    private SimulationClockSegment _simulationClockSegment;
    private readonly TextInputBatchBudget _textInputBatchBudget;
    private readonly WindowHandle? _window;

    internal InputRouter(
        InputRecipientKind recipientKind,
        uint recipientId,
        int ownerThreadId,
        HostEventSequenceLease sequenceLease,
        WindowHandle? window = null)
    {
        if (!Enum.IsDefined(recipientKind))
        {
            throw new ArgumentOutOfRangeException(nameof(recipientKind));
        }

        ArgumentOutOfRangeException.ThrowIfZero(recipientId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerThreadId, 1);
        ArgumentNullException.ThrowIfNull(sequenceLease);

        if (recipientKind == InputRecipientKind.HostLiveUI)
        {
            if (window is not { IsValid: true })
            {
                throw new ArgumentException("A HostLiveUI router requires its exact valid window route.", nameof(window));
            }
        }
        else if (window.HasValue)
        {
            throw new ArgumentException("A SimulationTimeline router is not constructed with a physical window route.", nameof(window));
        }

        if (ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("An input router must be created on its host owner thread.");
        }

        _maps = [];
        _ownerThreadId = ownerThreadId;
        _recipientId = recipientId;
        _recipientKind = recipientKind;
        _sequenceLease = sequenceLease;
        _textInputBatchBudget = new TextInputBatchBudget(ownerThreadId);
        _window = window;
        _hostActionSnapshot = new InputActionSnapshot(recipientKind, recipientId, null, []);
        _simulationActionSnapshot = new InputActionSnapshot(recipientKind, recipientId, null, []);
    }

    /// <summary>Gets the recipient identity.</summary>
    public uint RecipientId => _recipientId;

    /// <summary>Gets the recipient clock kind.</summary>
    public InputRecipientKind RecipientKind => _recipientKind;

    internal bool IsPauseControlPending => _pauseControlPending;

    internal bool IsPaused => _isPaused;

    internal long NextOpenTick => _nextOpenTick;

    private static int CountSetBits(ulong mask)
    {
        var count = 0;

        while (mask != 0)
        {
            mask &= mask - 1;
            count++;
        }

        return count;
    }

    private static InputMapResponse CreateMapCapacityFailure(string path)
        => new InputMapResponse(
            InputMapStatus.CapacityExceeded,
            null,
            null,
            new ValidationIssue("CapacityExceeded", path));

    private static InputMapResponse CreateMapRejection(string code, string path)
        => new InputMapResponse(
            InputMapStatus.Rejected,
            null,
            null,
            new ValidationIssue(code, path));

    private static InputActionMapState CreateReplacementMapState(InputActionMapState current, InputActionMapV1 replacement)
    {
        var activeMask = 0UL;
        var insertionSequences = new long[64];

        foreach (var newContext in replacement.Contexts)
        {
            foreach (var oldContext in current.Map.Contexts)
            {
                if (oldContext.ContextId != newContext.ContextId)
                {
                    continue;
                }

                var oldContextMask = 1UL << oldContext.Bit;

                if ((current.ActiveContextMask & oldContextMask) != 0)
                {
                    activeMask |= 1UL << newContext.Bit;
                    insertionSequences[newContext.Bit] = current.ContextInsertionSequences[oldContext.Bit];
                }

                break;
            }
        }

        return new InputActionMapState(replacement, activeMask, insertionSequences);
    }

    private static ulong GetDeclaredContextMask(InputActionMapV1 map)
    {
        var mask = 0UL;

        foreach (var context in map.Contexts)
        {
            mask |= 1UL << context.Bit;
        }

        return mask;
    }

    /// <summary>Returns the latest immutable host-clock action snapshot.</summary>
    /// <returns>The latest action state for this recipient.</returns>
    /// <exception cref="InvalidOperationException">This is not a host-clock recipient.</exception>
    /// <exception cref="ObjectDisposedException">This router has been closed by its host.</exception>
    public InputActionSnapshot GetHostActionSnapshot()
    {
        ThrowIfDisposed();

        if (_recipientKind != InputRecipientKind.HostLiveUI)
        {
            throw new InvalidOperationException("A simulation timeline does not have a host-clock action snapshot.");
        }

        return Volatile.Read(ref _hostActionSnapshot);
    }

    /// <summary>Returns the immutable action snapshot published for one simulation tick.</summary>
    /// <param name="assignedTick">The nonnegative simulation tick.</param>
    /// <returns>The published action state for <paramref name="assignedTick"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="assignedTick"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">This is not a simulation recipient, or the requested tick has not been published.</exception>
    /// <exception cref="ObjectDisposedException">This router has been closed by its host.</exception>
    public InputActionSnapshot GetSimulationActionSnapshot(long assignedTick)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(assignedTick);

        if (_recipientKind != InputRecipientKind.SimulationTimeline)
        {
            throw new InvalidOperationException("A host-clock recipient does not have simulation-tick snapshots.");
        }

        var snapshot = Volatile.Read(ref _simulationActionSnapshot);

        if (snapshot.AssignedTick != assignedTick)
        {
            throw new InvalidOperationException("The requested simulation action snapshot has not been published.");
        }

        return snapshot;
    }

    /// <summary>Validates and publishes a complete new action map.</summary>
    /// <param name="map">The immutable version-one map candidate.</param>
    /// <returns>The registration result and assigned host sequence.</returns>
    /// <exception cref="InvalidOperationException">This method is called off the owner thread or after disposal.</exception>
    public InputMapResponse RegisterMap(InputActionMapV1 map)
    {
        EnsureOwnerThread();

        var rejection = InputActionMapValidator.Validate(map);

        if (rejection is not null)
        {
            return new InputMapResponse(InputMapStatus.Rejected, null, null, rejection);
        }

        if (_maps.ContainsKey(map.MapId))
        {
            return CreateMapRejection("DuplicateId", "/MapId");
        }

        if (_maps.Count >= MaximumMaps)
        {
            return CreateMapCapacityFailure("/Maps");
        }

        var activeContextCount = CountActiveContexts() + CountSetBits(map.InitialActiveContextMask);

        if (activeContextCount > MaximumActiveContexts)
        {
            return CreateMapCapacityFailure("/InitialActiveContextMask");
        }

        if (!TryCreateInitialContextInsertionSequences(map, out var insertionSequences, out var lastInsertionSequence))
        {
            return CreateMapCapacityFailure("/InitialActiveContextMask");
        }

        if (!CanAdmitControlCommand())
        {
            return CreateMapCapacityFailure("/InputControl");
        }

        if ((_inputInbox.Count != 0) || (_inputControlInbox.Count != 0))
        {
            return CreateMapCapacityFailure("/InputControl");
        }

        var priorEligibility = CaptureBindingEligibility();
        var hostSequence = _sequenceLease.Allocate();
        RecordHostControlSequence(hostSequence);
        var mapState = new InputActionMapState(map, map.InitialActiveContextMask, insertionSequences);
        InitializeMapState(mapState);
        _maps.Add(map.MapId, mapState);
        RebuildOrderedContexts();
        _ = RefreshContextEligibility(
            priorEligibility,
            hostSequence,
            (_recipientKind == InputRecipientKind.SimulationTimeline) ? _nextOpenTick : null,
            deferTransitions: _recipientKind == InputRecipientKind.SimulationTimeline);
        _lastInsertionSequence = lastInsertionSequence;
        _controlCommandsInBoundary++;
        PublishHostActionSnapshot();

        return new InputMapResponse(InputMapStatus.Registered, hostSequence, map.Revision, null);
    }

    /// <summary>Validates and atomically replaces one complete action-map revision.</summary>
    /// <param name="map">The immutable replacement candidate.</param>
    /// <param name="expectedRevision">The currently published revision expected by the caller.</param>
    /// <returns>The replacement result and assigned host sequence.</returns>
    /// <exception cref="InvalidOperationException">This method is called off the owner thread or after disposal.</exception>
    public InputMapResponse ReplaceMap(InputActionMapV1 map, long expectedRevision)
    {
        EnsureOwnerThread();

        var rejection = InputActionMapValidator.Validate(map);

        if (rejection is not null)
        {
            return new InputMapResponse(InputMapStatus.Rejected, null, null, rejection);
        }

        if (!_maps.TryGetValue(map.MapId, out var current))
        {
            return CreateMapRejection("UnknownReference", "/MapId");
        }

        if ((expectedRevision <= 0)
            || (expectedRevision != current.Map.Revision)
            || (map.Revision <= current.Map.Revision))
        {
            return CreateMapRejection("StaleRevision", "/Revision");
        }

        if (map.InitialActiveContextMask != current.Map.InitialActiveContextMask)
        {
            return CreateMapRejection("InvalidContextMask", "/InitialActiveContextMask");
        }

        var replacementState = CreateReplacementMapState(current, map);
        var activeContextCount = CountActiveContexts(map.MapId) + CountSetBits(replacementState.ActiveContextMask);

        if (activeContextCount > MaximumActiveContexts)
        {
            return CreateMapCapacityFailure("/InitialActiveContextMask");
        }

        if (!CanAdmitControlCommand())
        {
            return CreateMapCapacityFailure("/InputControl");
        }

        if ((_inputInbox.Count != 0) || (_inputControlInbox.Count != 0))
        {
            return CreateMapCapacityFailure("/InputControl");
        }

        var priorEligibility = CaptureBindingEligibility();
        InitializeMapState(replacementState);
        var hostSequence = _sequenceLease.Allocate();
        RecordHostControlSequence(hostSequence);
        long? assignedTick = (_recipientKind == InputRecipientKind.SimulationTimeline) ? _nextOpenTick : null;
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
                        hostSequence,
                        assignedTick));
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
                    hostSequence,
                    assignedTick));
            }
        }

        _pendingReboundActions.AddRange(reboundActions);
        _maps[map.MapId] = replacementState;
        RebuildOrderedContexts();
        _ = RefreshContextEligibility(
            priorEligibility,
            hostSequence,
            (_recipientKind == InputRecipientKind.SimulationTimeline) ? _nextOpenTick : null,
            deferTransitions: _recipientKind == InputRecipientKind.SimulationTimeline);
        _controlCommandsInBoundary++;
        PublishHostActionSnapshot();

        return new InputMapResponse(InputMapStatus.Replaced, hostSequence, map.Revision, null);
    }

    /// <summary>Atomically replaces one map's complete active context mask.</summary>
    /// <param name="mapId">The registered action map.</param>
    /// <param name="expectedRevision">The current map revision expected by the caller.</param>
    /// <param name="newMask">The complete replacement mask.</param>
    /// <returns>The routing result and its host sequence or assigned tick.</returns>
    /// <exception cref="InvalidOperationException">This method is called off the owner thread or after disposal.</exception>
    public InputRoutingReceipt SetActiveContextMask(Guid mapId, long expectedRevision, ulong newMask)
    {
        EnsureOwnerThread();

        if (!_maps.TryGetValue(mapId, out var current))
        {
            return CreateRoutingRejection("UnknownReference", "/MapId");
        }

        if ((expectedRevision <= 0) || (expectedRevision != current.Map.Revision))
        {
            return CreateRoutingRejection("StaleRevision", "/ExpectedRevision");
        }

        var declaredMask = GetDeclaredContextMask(current.Map);

        if ((newMask & ~declaredMask) != 0)
        {
            return CreateRoutingRejection("InvalidContextMask", "/NewMask");
        }

        var (plannedMask, plannedInsertionSequences, plannedLastInsertionSequence) = GetPlannedContextState(current);
        var activeContextCount = CountPlannedActiveContexts(mapId, newMask);

        if (activeContextCount > MaximumActiveContexts)
        {
            return CreateRoutingCapacityFailure("/NewMask");
        }

        var newlyActivatedBits = newMask & ~plannedMask;
        var newlyActivatedCount = CountSetBits(newlyActivatedBits);

        if (newlyActivatedCount > (long.MaxValue - plannedLastInsertionSequence))
        {
            return CreateRoutingCapacityFailure("/InsertionSequence");
        }

        if (!CanAdmitControlCommand() || (_inputControlInbox.Count >= MaximumControlCommandsPerBoundary))
        {
            return CreateRoutingCapacityFailure("/InputControl");
        }

        if (_hostControlTimestamp < _lastSeenHostTimestamp)
        {
            return CreateRoutingRejection("InvalidEventOrder", "/HostTimestamp");
        }

        var insertionSequences = (long[])plannedInsertionSequences.Clone();
        var lastInsertionSequence = plannedLastInsertionSequence;

        for (var bit = 0; bit < insertionSequences.Length; bit++)
        {
            var bitMask = 1UL << bit;

            if ((newMask & bitMask) == 0)
            {
                insertionSequences[bit] = 0;
            }
            else if ((newlyActivatedBits & bitMask) != 0)
            {
                insertionSequences[bit] = checked(++lastInsertionSequence);
            }
        }

        var hostSequence = _sequenceLease.Allocate();
        RecordHostControlSequence(hostSequence);
        _lastSeenHostTimestamp = _hostControlTimestamp;
        _inputControlInbox.Enqueue(new InputControlEnvelope(
            InputControlCommandKind.SetActiveContextMask,
            mapId,
            expectedRevision,
            newMask,
            insertionSequences,
            lastInsertionSequence,
            InputClass.None,
            null,
            null,
            [],
            _hostControlTimestamp,
            hostSequence,
            (_recipientKind == InputRecipientKind.SimulationTimeline) ? _nextOpenTick : null));
        _controlCommandsInBoundary++;

        return new InputRoutingReceipt(
            InputRoutingStatus.Applied,
            _recipientId,
            hostSequence,
            (_recipientKind == InputRecipientKind.SimulationTimeline) ? _nextOpenTick : null,
            null);
    }

    internal void BeginHostControlBoundary(long nextOpenTick, long hostTimestamp)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(nextOpenTick);
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);

        if (nextOpenTick < _nextOpenTick)
        {
            throw new InvalidOperationException("The next open input tick cannot regress.");
        }

        if (hostTimestamp < _lastSeenHostTimestamp)
        {
            throw new InvalidOperationException("The host input timestamp cannot regress.");
        }

        _controlCommandsInBoundary = 0;
        _hostControlTimestamp = hostTimestamp;
        _nextOpenTick = nextOpenTick;
    }

    internal void Dispose()
    {
        _ = DisposeAndGetTransitions();
    }

    internal ImmutableArray<InputActionTransition> DisposeAndGetTransitions()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("An input router must be disposed on its host owner thread.");
        }

        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return [];
        }

        var hostSequence = _sequenceLease.Allocate();
        RecordHostControlSequence(hostSequence);
        _activeTextSession?.EndForRecipientDisposal(hostSequence, _hostControlTimestamp);
        _unreconciledControls.Clear();
        ClearInputValuesForScope(InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad, playerSlot: null);
        UpdateBindingsForScope(InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad, playerSlot: null);
        RefreshAllConsumptionLeases();
        var transitions = RecomputeAllActions(
            hostSequence,
            assignedTick: null,
            InputCancellationReason.RecipientDisposed);

        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return [];
        }

        _maps.Clear();
        _controlCommandsInBoundary = 0;
        ClearInputState();
        return transitions;
    }

    internal InputActionSnapshot PublishSimulationActionSnapshot(long assignedTick)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(assignedTick);

        if (_recipientKind != InputRecipientKind.SimulationTimeline)
        {
            throw new InvalidOperationException("Only a simulation timeline can publish a tick snapshot.");
        }

        return ProcessSimulationInputStage(assignedTick);
    }

    internal void UpdateHostControlTimestamp(long hostTimestamp)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);

        if ((hostTimestamp < _hostControlTimestamp) || (hostTimestamp < _lastSeenHostTimestamp))
        {
            throw new InvalidOperationException("The host input timestamp cannot regress.");
        }

        _hostControlTimestamp = hostTimestamp;
    }

    private bool CanAdmitControlCommand() => _controlCommandsInBoundary < MaximumControlCommandsPerBoundary;

    private int CountActiveContexts()
    {
        var count = 0;

        foreach (var mapState in _maps.Values)
        {
            count += CountSetBits(mapState.ActiveContextMask);
        }

        return count;
    }

    private int CountActiveContexts(Guid excludedMapId)
    {
        var count = 0;

        foreach (var mapState in _maps.Values)
        {
            if (mapState.Map.MapId != excludedMapId)
            {
                count += CountSetBits(mapState.ActiveContextMask);
            }
        }

        return count;
    }

    private InputActionSnapshot CreateActionSnapshot(long? assignedTick)
    {
        var actionCount = 0;

        foreach (var mapState in _maps.Values)
        {
            actionCount += mapState.OrderedActions.Length;
        }

        actionCount += _terminalActionStates.Count;
        var actions = new List<InputActionState>(actionCount);

        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            foreach (var action in mapState.OrderedActions)
            {
                var actionState = mapState.ActionStates[action.ActionId];
                actions.Add(new InputActionState(
                    mapState.Map.MapId,
                    action.ActionId,
                    mapState.Map.Revision,
                    action.Kind,
                    actionState.IsDownAtEnd,
                    actionState.PressedThisTick,
                    actionState.ReleasedThisTick,
                    actionState.ValueChangedThisTick,
                    actionState.AxisX,
                    actionState.AxisY,
                    [.. actionState.Transitions]));
            }
        }

        actions.AddRange(_terminalActionStates);
        _terminalActionStates.Clear();
        return new InputActionSnapshot(
            _recipientKind,
            _recipientId,
            assignedTick,
            [
                .. actions.OrderBy(static state => state.MapId.ToString("D"), StringComparer.Ordinal)
                    .ThenBy(static state => state.ActionId.ToString("D"), StringComparer.Ordinal),
            ]);
    }

    private InputRoutingReceipt CreateRoutingCapacityFailure(string path)
        => new InputRoutingReceipt(
            InputRoutingStatus.CapacityExceeded,
            _recipientId,
            null,
            null,
            new ValidationIssue("CapacityExceeded", path));

    private InputRoutingReceipt CreateRoutingRejection(string code, string path)
        => new InputRoutingReceipt(
            InputRoutingStatus.Rejected,
            _recipientId,
            null,
            null,
            new ValidationIssue(code, path));

    private void EnsureOwnerThread()
    {
        ThrowIfDisposed();

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("The input router may only be changed on its host owner thread.");
        }
    }

    private void PublishHostActionSnapshot()
    {
        ApplyPendingReboundActions(assignedTick: null);
        Volatile.Write(ref _hostActionSnapshot, CreateActionSnapshot(assignedTick: null));
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

    private bool TryCreateInitialContextInsertionSequences(
        InputActionMapV1 map,
        out long[] insertionSequences,
        out long lastInsertionSequence)
    {
        insertionSequences = new long[64];
        var activeCount = CountSetBits(map.InitialActiveContextMask);

        if (activeCount > (long.MaxValue - _lastInsertionSequence))
        {
            lastInsertionSequence = _lastInsertionSequence;
            return false;
        }

        var lastSequence = _lastInsertionSequence;

        for (var bit = 0; bit < insertionSequences.Length; bit++)
        {
            if ((map.InitialActiveContextMask & (1UL << bit)) != 0)
            {
                insertionSequences[bit] = checked(++lastSequence);
            }
        }

        lastInsertionSequence = lastSequence;
        return true;
    }

    private sealed class InputActionMapState
    {
        internal InputActionMapState(InputActionMapV1 map, ulong activeContextMask, long[] contextInsertionSequences)
        {
            ActiveContextMask = activeContextMask;
            ActionStates = [];
            BindingStates = [];
            BindingsByControl = [];
            ContextInsertionSequences = contextInsertionSequences;
            Map = map;
            OrderedActions = [.. map.Actions.OrderBy(static action => action.ActionId.ToString("D"), StringComparer.Ordinal)];

            foreach (var action in OrderedActions)
            {
                ActionStates.Add(action.ActionId, new InputActionRuntimeState(action));
            }

            foreach (var binding in map.Bindings)
            {
                var bindingState = new InputBindingRuntimeState(map.MapId, binding);
                BindingStates.Add(binding.BindingId, bindingState);

                foreach (var control in bindingState.ControlKeys)
                {
                    if (!BindingsByControl.TryGetValue(control, out var bindings))
                    {
                        bindings = [];
                        BindingsByControl.Add(control, bindings);
                    }

                    bindings.Add(bindingState);
                }
            }
        }

        internal Dictionary<Guid, InputActionRuntimeState> ActionStates { get; }

        internal ulong ActiveContextMask { get; private set; }

        internal Dictionary<Guid, InputBindingRuntimeState> BindingStates { get; }

        internal Dictionary<InputControlKey, List<InputBindingRuntimeState>> BindingsByControl { get; }

        internal long[] ContextInsertionSequences { get; private set; }

        internal InputActionMapV1 Map { get; }

        internal ImmutableArray<ActionDefinition> OrderedActions { get; }

        internal void SetContextMask(ulong activeContextMask, long[] contextInsertionSequences)
        {
            ActiveContextMask = activeContextMask;
            ContextInsertionSequences = contextInsertionSequences;
        }
    }
}
