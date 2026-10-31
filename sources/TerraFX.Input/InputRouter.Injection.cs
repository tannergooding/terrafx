// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace TerraFX.Input;

public sealed partial class InputRouter
{
    private const int MaximumPendingInputEvents = 4096;

    private readonly Dictionary<InputControlKey, bool> _digitalControlStates = [];
    private long _firstUnacceptedHostSequence;
    private readonly Queue<InputEnvelope> _inputInbox = [];
    private bool _inputOverflowed;
    private long _lastAssignedInputTick = -1;
    private long _lastSeenHostSequence;
    private long _lastSeenHostTimestamp;
    private long _lastUnacceptedHostSequence;
    private bool _overflowServiced;

    private static InputControlKey GetControlKey(DigitalControl control)
        => control.Kind switch {
            DigitalControlKind.KeyboardHid => new InputControlKey(
                control.Kind,
                control.Keyboard!.UsagePage,
                control.Keyboard.Usage,
                null,
                0,
                0),
            DigitalControlKind.LogicalKey => new InputControlKey(
                control.Kind,
                0,
                0,
                control.Keyboard!.LogicalKeyName,
                0,
                0),
            DigitalControlKind.GamepadButton => new InputControlKey(
                control.Kind,
                0,
                0,
                null,
                control.PlayerSlot.GetValueOrDefault(),
                (int)control.GamepadButton.GetValueOrDefault()),
            _ => default,
        };

    private static InputControlKey GetControlKey(InputInjection injection)
    {
        if (injection.Kind == InputInjectionKind.KeyboardKey)
        {
            var keyboard = injection.Key!;

            return new InputControlKey(
                (keyboard.Kind == KeyboardControlKind.KeyboardHid) ? DigitalControlKind.KeyboardHid : DigitalControlKind.LogicalKey,
                keyboard.UsagePage,
                keyboard.Usage,
                keyboard.LogicalKeyName,
                0,
                0);
        }

        return new InputControlKey(
            DigitalControlKind.GamepadButton,
            0,
            0,
            null,
            injection.PlayerSlot.GetValueOrDefault(),
            (int)injection.Button.GetValueOrDefault());
    }

    internal InputRoutingReceipt EnqueueHeadlessInjection(
        InputInjection injection,
        long hostTimestamp,
        long hostSequence,
        long assignedTick)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(injection);
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequence, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(assignedTick);

        if (_recipientKind != InputRecipientKind.SimulationTimeline)
        {
            return CreateInputRejection("InvalidRecipient", "/RecipientKind");
        }

        if ((hostSequence <= _lastSeenHostSequence) || (hostTimestamp < _lastSeenHostTimestamp))
        {
            _lastSeenHostSequence = Math.Max(hostSequence, _lastSeenHostSequence);
            return CreateInputRejection("InvalidEventOrder", "/HostSequence");
        }

        _lastSeenHostSequence = hostSequence;
        _lastSeenHostTimestamp = hostTimestamp;

        if (injection.Kind is not (InputInjectionKind.KeyboardKey or InputInjectionKind.GamepadButton))
        {
            return CreateInputRejection("UnsupportedFeature", "/Kind");
        }

        if (_inputOverflowed)
        {
            RecordUnacceptedSequence(hostSequence);
            return CreateInputCapacityFailure(hostSequence, Math.Max(assignedTick, _nextOpenTick));
        }

        if (_inputInbox.Count >= MaximumPendingInputEvents)
        {
            _inputOverflowed = true;
            RecordUnacceptedSequence(hostSequence);
            return CreateInputCapacityFailure(hostSequence, Math.Max(assignedTick, _nextOpenTick));
        }

        var effectiveTick = Math.Max(assignedTick, _nextOpenTick);

        if (effectiveTick < _lastAssignedInputTick)
        {
            return CreateInputRejection("InvalidTickOrder", "/AssignedTick");
        }

        var control = GetControlKey(injection);
        var envelope = new InputEnvelope(injection, control, hostTimestamp, hostSequence, effectiveTick);
        _inputInbox.Enqueue(envelope);
        _lastAssignedInputTick = effectiveTick;

        return new InputRoutingReceipt(InputRoutingStatus.Applied, _recipientId, hostSequence, effectiveTick, null);
    }

    internal InputRoutingReceipt ReconcileHeadlessInputState(
        ImmutableArray<InputInjection> heldInputs,
        long hostTimestamp,
        long hostSequence)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequence, 1);

        if (!_inputOverflowed || !_overflowServiced)
        {
            return CreateInputRejection("InvalidReconciliation", "/InputOverflow");
        }

        if (heldInputs.IsDefault || (heldInputs.Length > MaximumPendingInputEvents))
        {
            return CreateInputRejection("InvalidReconciliation", "/State");
        }

        if ((hostSequence <= _lastSeenHostSequence) || (hostTimestamp < _lastSeenHostTimestamp))
        {
            return CreateInputRejection("InvalidEventOrder", "/HostSequence");
        }

        var reconciledControls = new HashSet<InputControlKey>();

        foreach (var input in heldInputs)
        {
            ArgumentNullException.ThrowIfNull(input);

            var isHeldDigitalInput = input.Kind switch {
                InputInjectionKind.KeyboardKey => input.KeyEventKind == InputKeyEventKind.Down,
                InputInjectionKind.GamepadButton => input.IsDown.GetValueOrDefault(),
                _ => false,
            };

            if (!isHeldDigitalInput || !reconciledControls.Add(GetControlKey(input)))
            {
                return CreateInputRejection("InvalidReconciliation", "/State");
            }
        }

        _digitalControlStates.Clear();

        foreach (var control in reconciledControls)
        {
            _digitalControlStates.Add(control, true);
        }

        _inputInbox.Clear();
        _inputOverflowed = false;
        _overflowServiced = false;
        _firstUnacceptedHostSequence = 0;
        _lastAssignedInputTick = _nextOpenTick;
        _lastSeenHostSequence = hostSequence;
        _lastSeenHostTimestamp = hostTimestamp;
        _lastUnacceptedHostSequence = 0;

        foreach (var mapState in _maps.Values)
        {
            foreach (var actionState in mapState.ActionStates.Values)
            {
                actionState.IsDownAtEnd = false;
                actionState.PressedThisTick = false;
                actionState.ReleasedThisTick = false;
                actionState.Transitions.Clear();
            }

            foreach (var bindingState in mapState.BindingStates.Values)
            {
                bindingState.IsDown = false;
            }
        }

        return new InputRoutingReceipt(InputRoutingStatus.Applied, _recipientId, hostSequence, null, null);
    }

    internal InputOverflowResult? ServiceHostControlBoundary()
    {
        EnsureOwnerThread();

        if (!_inputOverflowed || _overflowServiced)
        {
            return null;
        }

        return HandleInputOverflow(assignedTick: null);
    }

    private void ApplyInputEnvelope(InputEnvelope envelope)
    {
        if (envelope.Injection.KeyEventKind == InputKeyEventKind.Repeat)
        {
            return;
        }

        var isDown = (envelope.Injection.Kind == InputInjectionKind.KeyboardKey)
            ? envelope.Injection.KeyEventKind == InputKeyEventKind.Down
            : envelope.Injection.IsDown.GetValueOrDefault();

        if (_digitalControlStates.TryGetValue(envelope.Control, out var currentState) && (currentState == isDown))
        {
            return;
        }

        _digitalControlStates[envelope.Control] = isDown;
        RecomputeAffectedDigitalActions(envelope.Control, envelope.HostSequence, envelope.AssignedTick);
    }

    private void ClearInputState()
    {
        _digitalControlStates.Clear();
        _inputInbox.Clear();
        _inputOverflowed = false;
        _firstUnacceptedHostSequence = 0;
        _lastAssignedInputTick = -1;
        _lastSeenHostSequence = 0;
        _lastSeenHostTimestamp = 0;
        _lastUnacceptedHostSequence = 0;
        _overflowServiced = false;
    }

    private InputRoutingReceipt CreateInputCapacityFailure(long hostSequence, long assignedTick)
        => new InputRoutingReceipt(
            InputRoutingStatus.CapacityExceeded,
            _recipientId,
            hostSequence,
            assignedTick,
            new ValidationIssue("CapacityExceeded", "/InputInbox"));

    private InputRoutingReceipt CreateInputRejection(string code, string path)
        => new InputRoutingReceipt(
            InputRoutingStatus.Rejected,
            _recipientId,
            null,
            null,
            new ValidationIssue(code, path));

    private InputOverflowResult HandleInputOverflow(long? assignedTick)
    {
        _inputInbox.Clear();
        _digitalControlStates.Clear();
        var transitions = ImmutableArray.CreateBuilder<InputActionTransition>();

        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            foreach (var action in mapState.OrderedActions)
            {
                var actionState = mapState.ActionStates[action.ActionId];

                if (!actionState.IsDownAtEnd)
                {
                    continue;
                }

                actionState.IsDownAtEnd = false;
                actionState.ReleasedThisTick = false;
                var transition = new InputActionTransition(
                    mapState.Map.MapId,
                    action.ActionId,
                    InputTransitionKind.Canceled,
                    InputCancellationReason.InputOverflow,
                    _lastUnacceptedHostSequence,
                    assignedTick,
                    actionState.AxisX,
                    actionState.AxisY);
                actionState.Transitions.Add(transition);
                transitions.Add(transition);
            }
        }

        _overflowServiced = true;
        return new InputOverflowResult(_firstUnacceptedHostSequence, _lastUnacceptedHostSequence, transitions.ToImmutable());
    }

    private InputActionSnapshot ProcessSimulationInputStage(long assignedTick)
    {
        foreach (var mapState in _maps.Values)
        {
            foreach (var actionState in mapState.ActionStates.Values)
            {
                actionState.ResetTick();
            }
        }

        if (_inputOverflowed)
        {
            if (!_overflowServiced)
            {
                _ = HandleInputOverflow(assignedTick);
            }
        }
        else
        {
            while (_inputInbox.TryPeek(out var envelope) && (envelope.AssignedTick <= assignedTick))
            {
                _ = _inputInbox.Dequeue();
                ApplyInputEnvelope(envelope);
            }
        }

        _nextOpenTick = Math.Max(_nextOpenTick, checked(assignedTick + 1));
        var snapshot = CreateActionSnapshot(assignedTick);
        Volatile.Write(ref _simulationActionSnapshot, snapshot);
        return snapshot;
    }

    private void RecomputeAffectedDigitalActions(InputControlKey changedControl, long hostSequence, long assignedTick)
    {
        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            foreach (var action in mapState.OrderedActions)
            {
                if (action.Kind != InputActionKind.Digital)
                {
                    continue;
                }

                var isAffected = false;

                foreach (var binding in mapState.Map.Bindings)
                {
                    if ((binding.ActionId == action.ActionId)
                        && binding.Enabled
                        && ((binding.ContextMask & mapState.ActiveContextMask) != 0)
                        && (binding.Kind == BindingKind.Button)
                        && (binding.Control is { } control)
                        && (GetControlKey(control) == changedControl))
                    {
                        isAffected = true;
                        break;
                    }
                }

                if (!isAffected)
                {
                    continue;
                }

                var isDown = false;

                foreach (var binding in mapState.Map.Bindings)
                {
                    if ((binding.ActionId != action.ActionId)
                        || !binding.Enabled
                        || ((binding.ContextMask & mapState.ActiveContextMask) == 0)
                        || (binding.Kind != BindingKind.Button)
                        || (binding.Control is not { } control))
                    {
                        continue;
                    }

                    if (_digitalControlStates.TryGetValue(GetControlKey(control), out var bindingIsDown) && bindingIsDown)
                    {
                        isDown = true;
                        break;
                    }
                }

                var actionState = mapState.ActionStates[action.ActionId];

                if (actionState.IsDownAtEnd == isDown)
                {
                    continue;
                }

                actionState.IsDownAtEnd = isDown;
                actionState.PressedThisTick |= isDown;
                actionState.ReleasedThisTick |= !isDown;
                actionState.Transitions.Add(new InputActionTransition(
                    mapState.Map.MapId,
                    action.ActionId,
                    isDown ? InputTransitionKind.Pressed : InputTransitionKind.Released,
                    null,
                    hostSequence,
                    assignedTick,
                    0.0f,
                    0.0f));
            }
        }
    }

    private void RecordUnacceptedSequence(long hostSequence)
    {
        if (_firstUnacceptedHostSequence == 0)
        {
            _firstUnacceptedHostSequence = hostSequence;
        }

        _lastUnacceptedHostSequence = hostSequence;
    }

    internal sealed class InputOverflowResult
    {
        internal InputOverflowResult(
            long firstUnacceptedHostSequence,
            long lastUnacceptedHostSequence,
            ImmutableArray<InputActionTransition> transitions)
        {
            FirstUnacceptedHostSequence = firstUnacceptedHostSequence;
            LastUnacceptedHostSequence = lastUnacceptedHostSequence;
            Transitions = transitions;
        }

        internal long FirstUnacceptedHostSequence { get; }

        internal long LastUnacceptedHostSequence { get; }

        internal ImmutableArray<InputActionTransition> Transitions { get; }
    }

    private sealed class InputActionRuntimeState
    {
        internal InputActionRuntimeState(ActionDefinition definition)
        {
            Definition = definition;
            Transitions = [];
        }

        internal float AxisX { get; set; }

        internal float AxisY { get; set; }

        internal ActionDefinition Definition { get; }

        internal bool IsDownAtEnd { get; set; }

        internal bool PressedThisTick { get; set; }

        internal bool ReleasedThisTick { get; set; }

        internal List<InputActionTransition> Transitions { get; }

        internal bool ValueChangedThisTick { get; set; }

        internal void ResetTick()
        {
            PressedThisTick = false;
            ReleasedThisTick = false;
            Transitions.Clear();
            ValueChangedThisTick = false;
        }
    }

    private sealed class InputBindingRuntimeState
    {
        internal InputBindingRuntimeState(BindingDefinition definition)
        {
            Definition = definition;
        }

        internal BindingDefinition Definition { get; }

        internal bool IsDown { get; set; }
    }

    private readonly record struct InputControlKey(
        DigitalControlKind Kind,
        ushort UsagePage,
        ushort Usage,
        string? LogicalKeyName,
        ushort PlayerSlot,
        int Value);

    private sealed class InputEnvelope(
        InputInjection injection,
        InputControlKey control,
        long hostTimestamp,
        long hostSequence,
        long assignedTick)
    {
        internal long AssignedTick { get; } = assignedTick;

        internal InputControlKey Control { get; } = control;

        internal long HostSequence { get; } = hostSequence;

        internal long HostTimestamp { get; } = hostTimestamp;

        internal InputInjection Injection { get; } = injection;
    }
}
