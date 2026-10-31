// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using TerraFX.Numerics;

namespace TerraFX.Input;

public sealed partial class InputRouter
{
    private const int MaximumPendingInputEvents = 4096;

    private readonly Dictionary<InputControlKey, List<InputConsumptionLease>> _consumptionLeases = [];
    private readonly Dictionary<InputControlKey, long> _digitalControlDownTimestamps = [];
    private readonly Dictionary<InputControlKey, bool> _digitalControlStates = [];
    private long _firstUnacceptedHostSequence;
    private readonly Queue<InputControlEnvelope> _inputControlInbox = [];
    private readonly Queue<InputEnvelope> _inputInbox = [];
    private long _hostControlTimestamp;
    private bool _inputOverflowed;
    private long _lastAssignedInputTick = -1;
    private long _lastSeenHostSequence;
    private long _lastSeenHostTimestamp;
    private long _lastUnacceptedHostSequence;
    private bool _overflowServiced;
    private ImmutableArray<InputContextOrder> _orderedContexts = [];
    private ImmutableArray<InputBindingRuntimeState> _orderedConsumableBindings = [];
    private readonly Dictionary<InputControlKey, float> _scalarControlValues = [];
    private readonly Dictionary<InputControlKey, (float X, float Y)> _vectorControlValues = [];

    private static float GetAxisThresholdMetric(
        BindingDefinition binding,
        Dictionary<InputControlKey, float> scalarControlValues)
    {
        var value = (binding.ThresholdAxisSource is { } axisSource)
            ? GetProcessedAxis1Value(axisSource, scalarControlValues)
            : GetProcessedTriggerValue(binding.ThresholdTriggerSource!, scalarControlValues);

        return binding.ThresholdDirection switch {
            AxisThresholdDirection.Negative => MathF.Max(0.0f, -value),
            AxisThresholdDirection.Positive => MathF.Max(0.0f, value),
            AxisThresholdDirection.Magnitude => MathF.Abs(value),
            _ => 0.0f,
        };
    }

    private static ImmutableArray<InputControlKey> GetBindingControlKeys(BindingDefinition binding)
    {
        var controls = ImmutableArray.CreateBuilder<InputControlKey>();

        switch (binding.Kind)
        {
            case BindingKind.Button:
            {
                controls.Add(GetControlKey(binding.Control!));
                break;
            }

            case BindingKind.Chord:
            {
                foreach (var control in binding.Controls)
                {
                    controls.Add(GetControlKey(control));
                }

                break;
            }

            case BindingKind.Axis1Keys:
            {
                controls.Add(GetControlKey(binding.Negative!));
                controls.Add(GetControlKey(binding.Positive!));
                break;
            }

            case BindingKind.Axis2Keys:
            {
                controls.Add(GetControlKey(binding.Left!));
                controls.Add(GetControlKey(binding.Right!));
                controls.Add(GetControlKey(binding.Down!));
                controls.Add(GetControlKey(binding.Up!));
                break;
            }

            case BindingKind.GamepadAxis1:
            {
                controls.Add(GetControlKey(binding.Axis1Source!));
                break;
            }

            case BindingKind.GamepadStick2:
            {
                controls.Add(GetControlKey(binding.Stick2Source!));
                break;
            }

            case BindingKind.GamepadTrigger:
            {
                controls.Add(GetControlKey(binding.TriggerSource!));
                break;
            }

            case BindingKind.AxisThreshold:
            {
                if (binding.ThresholdAxisSource is { } axisSource)
                {
                    controls.Add(GetControlKey(axisSource));
                }
                else
                {
                    controls.Add(GetControlKey(binding.ThresholdTriggerSource!));
                }

                break;
            }
        }

        return controls.ToImmutable();
    }

    private static InputControlKey GetControlKey(DigitalControl control)
        => control.Kind switch {
            DigitalControlKind.KeyboardHid => GetControlKey(control.Keyboard!),
            DigitalControlKind.LogicalKey => GetControlKey(control.Keyboard!),
            DigitalControlKind.MouseButton => new InputControlKey(InputControlKind.MouseButton, 0, 0, null, 0, (int)control.MouseButton.GetValueOrDefault()),
            DigitalControlKind.GamepadButton => new InputControlKey(InputControlKind.GamepadButton, 0, 0, null, control.PlayerSlot.GetValueOrDefault(), (int)control.GamepadButton.GetValueOrDefault()),
            _ => default,
        };

    private static InputControlKey GetControlKey(GamepadAxis1Source source)
        => new InputControlKey(InputControlKind.GamepadAxis, 0, 0, null, source.PlayerSlot, (int)source.Axis);

    private static InputControlKey GetControlKey(GamepadStick2Source source)
        => new InputControlKey(InputControlKind.GamepadStick, 0, 0, null, source.PlayerSlot, (int)source.Stick);

    private static InputControlKey GetControlKey(GamepadTriggerSource source)
        => new InputControlKey(InputControlKind.GamepadTrigger, 0, 0, null, source.PlayerSlot, (int)source.Trigger);

    private static InputControlKey GetControlKey(InputInjection injection)
        => injection.Kind switch {
            InputInjectionKind.KeyboardKey => GetControlKey(injection.Key!),
            InputInjectionKind.GamepadButton => new InputControlKey(InputControlKind.GamepadButton, 0, 0, null, injection.PlayerSlot.GetValueOrDefault(), (int)injection.Button.GetValueOrDefault()),
            InputInjectionKind.GamepadAxis => new InputControlKey(InputControlKind.GamepadAxis, 0, 0, null, injection.PlayerSlot.GetValueOrDefault(), (int)injection.Axis.GetValueOrDefault()),
            InputInjectionKind.GamepadStick => new InputControlKey(InputControlKind.GamepadStick, 0, 0, null, injection.PlayerSlot.GetValueOrDefault(), (int)injection.Stick.GetValueOrDefault()),
            InputInjectionKind.GamepadTrigger => new InputControlKey(InputControlKind.GamepadTrigger, 0, 0, null, injection.PlayerSlot.GetValueOrDefault(), (int)injection.Trigger.GetValueOrDefault()),
            _ => default,
        };

    private static InputControlKey GetControlKey(KeyboardControl key)
        => key.Kind switch {
            KeyboardControlKind.KeyboardHid => new InputControlKey(InputControlKind.KeyboardHid, key.UsagePage, key.Usage, null, 0, 0),
            KeyboardControlKind.LogicalKey => new InputControlKey(InputControlKind.LogicalKey, 0, 0, key.LogicalKeyName, 0, 0),
            _ => default,
        };

    private static InputClass GetInputClass(InputControlKey control)
        => control.Kind switch {
            InputControlKind.KeyboardHid or InputControlKind.LogicalKey => InputClass.Keyboard,
            InputControlKind.MouseButton => InputClass.Pointing,
            InputControlKind.GamepadButton or InputControlKind.GamepadAxis or InputControlKind.GamepadStick or InputControlKind.GamepadTrigger => InputClass.Gamepad,
            _ => InputClass.None,
        };

    private static float GetProcessedAxis1Value(
        GamepadAxis1Source source,
        Dictionary<InputControlKey, float> scalarControlValues)
    {
        var value = Math.Clamp(GetScalarControlValue(GetControlKey(source), scalarControlValues), -1.0f, 1.0f);
        var magnitude = MathF.Abs(value);

        if (magnitude <= source.Deadzone)
        {
            value = 0.0f;
        }
        else
        {
            value = MathF.Sign(value) * ((magnitude - source.Deadzone) / (1.0f - source.Deadzone));
        }

        if (source.Invert)
        {
            value = -value;
        }

        return NormalizeZero(value);
    }

    private static (float X, float Y) GetProcessedStick2Value(
        GamepadStick2Source source,
        Dictionary<InputControlKey, (float X, float Y)> vectorControlValues)
    {
        var (rawX, rawY) = GetVectorControlValue(GetControlKey(source), vectorControlValues);
        var value = NormalizeStick(new Vector2(Math.Clamp(rawX, -1.0f, 1.0f), Math.Clamp(rawY, -1.0f, 1.0f)));
        var radius = MathF.Sqrt(Vector2.LengthSquared(value));

        if (radius <= source.RadialDeadzone)
        {
            return (0.0f, 0.0f);
        }

        var remappedRadius = MathF.Min(1.0f, (radius - source.RadialDeadzone) / (1.0f - source.RadialDeadzone));
        var scale = remappedRadius / radius;
        var x = value.X * scale;
        var y = value.Y * scale;

        if (source.InvertY)
        {
            y = -y;
        }

        return (NormalizeZero(x), NormalizeZero(y));
    }

    private static float GetProcessedTriggerValue(
        GamepadTriggerSource source,
        Dictionary<InputControlKey, float> scalarControlValues)
    {
        var value = Math.Clamp(GetScalarControlValue(GetControlKey(source), scalarControlValues), 0.0f, 1.0f);
        return NormalizeZero(MathF.Max(0.0f, (value - source.Deadzone) / (1.0f - source.Deadzone)));
    }

    private static float GetScalarControlValue(
        InputControlKey control,
        Dictionary<InputControlKey, float> scalarControlValues)
        => scalarControlValues.TryGetValue(control, out var value) ? value : 0.0f;

    private static (float X, float Y) GetVectorControlValue(
        InputControlKey control,
        Dictionary<InputControlKey, (float X, float Y)> vectorControlValues)
        => vectorControlValues.TryGetValue(control, out var value) ? value : (0.0f, 0.0f);

    private static bool IsDigitalRelease(InputEnvelope envelope)
        => envelope.Injection.Kind switch {
            InputInjectionKind.KeyboardKey => envelope.Injection.KeyEventKind == InputKeyEventKind.Up,
            InputInjectionKind.GamepadButton => !envelope.Injection.IsDown.GetValueOrDefault(),
            _ => false,
        };

    private static bool IsInputEnvelopeBeforeControl(InputEnvelope input, InputControlEnvelope control)
        => (input.HostTimestamp < control.HostTimestamp)
            || ((input.HostTimestamp == control.HostTimestamp) && (input.HostSequence < control.HostSequence));

    private static Vector2 NormalizeStick(Vector2 value)
    {
        var lengthSquared = Vector2.LengthSquared(value);

        if (lengthSquared > 1.0f)
        {
            var inverseLength = 1.0f / MathF.Sqrt(lengthSquared);
            value = new Vector2(value.X * inverseLength, value.Y * inverseLength);
        }

        return new Vector2(NormalizeZero(value.X), NormalizeZero(value.Y));
    }

    private static float NormalizeZero(float value) => (value == 0.0f) ? 0.0f : value;

    private static void ProcessActionEvent(
        InputActionMapState mapState,
        ActionDefinition action,
        long hostSequence,
        long? assignedTick,
        InputCancellationReason? cancellationReason = null)
    {
        var actionState = mapState.ActionStates[action.ActionId];

        if (action.Kind == InputActionKind.Digital)
        {
            var isDown = false;

            foreach (var binding in mapState.Map.Bindings)
            {
                var bindingState = mapState.BindingStates[binding.BindingId];

                if ((binding.ActionId == action.ActionId)
                    && bindingState.Definition.Enabled
                    && bindingState.IsContextEligible
                    && !bindingState.IsDisarmed
                    && bindingState.IsDown)
                {
                    isDown = true;
                    break;
                }
            }

            if (actionState.IsDownAtEnd != isDown)
            {
                var wasDown = actionState.IsDownAtEnd;
                actionState.IsDownAtEnd = isDown;
                var transitionKind = (wasDown && !isDown && (cancellationReason is not null))
                    ? InputTransitionKind.Canceled
                    : isDown ? InputTransitionKind.Pressed : InputTransitionKind.Released;

                if (transitionKind == InputTransitionKind.Canceled)
                {
                    actionState.Transitions.Add(new InputActionTransition(
                        mapState.Map.MapId,
                        action.ActionId,
                        transitionKind,
                        cancellationReason,
                        hostSequence,
                        assignedTick,
                        0.0f,
                        0.0f));
                    return;
                }

                actionState.PressedThisTick |= isDown;
                actionState.ReleasedThisTick |= !isDown;
                actionState.Transitions.Add(new InputActionTransition(
                    mapState.Map.MapId,
                    action.ActionId,
                    transitionKind,
                    null,
                    hostSequence,
                    assignedTick,
                    0.0f,
                    0.0f));
            }

            return;
        }

        var hasWinner = false;
        var winnerPriority = 0;
        var winnerId = string.Empty;
        var winnerMagnitudeSquared = 0.0f;
        var axisX = 0.0f;
        var axisY = 0.0f;

        foreach (var binding in mapState.Map.Bindings)
        {
            var bindingState = mapState.BindingStates[binding.BindingId];

            if ((binding.ActionId != action.ActionId)
                || !binding.Enabled
                || !bindingState.IsContextEligible
                || bindingState.IsDisarmed)
            {
                continue;
            }

            var candidateX = bindingState.AxisX;
            var candidateY = (action.Kind == InputActionKind.Axis2) ? bindingState.AxisY : 0.0f;
            var magnitudeSquared = (action.Kind == InputActionKind.Axis1)
                ? (candidateX * candidateX)
                : ((candidateX * candidateX) + (candidateY * candidateY));

            if (!hasWinner
                || (magnitudeSquared > winnerMagnitudeSquared)
                || ((magnitudeSquared == winnerMagnitudeSquared)
                    && ((binding.Priority < winnerPriority)
                        || ((binding.Priority == winnerPriority)
                            && (string.CompareOrdinal(bindingState.CanonicalBindingId, winnerId) < 0)))))
            {
                hasWinner = true;
                winnerId = bindingState.CanonicalBindingId;
                winnerMagnitudeSquared = magnitudeSquared;
                winnerPriority = binding.Priority;
                axisX = candidateX;
                axisY = candidateY;
            }
        }

        UpdateAxisActionState(mapState.Map.MapId, actionState, hostSequence, assignedTick, axisX, axisY, cancellationReason);
    }

    private static void UpdateAxisActionState(
        Guid mapId,
        InputActionRuntimeState actionState,
        long hostSequence,
        long? assignedTick,
        float axisX,
        float axisY,
        InputCancellationReason? cancellationReason = null)
    {
        axisX = NormalizeZero(axisX);
        axisY = (actionState.Definition.Kind == InputActionKind.Axis2) ? NormalizeZero(axisY) : 0.0f;

        if ((actionState.AxisX != axisX) || (actionState.AxisY != axisY))
        {
            actionState.AxisX = axisX;
            actionState.AxisY = axisY;
            actionState.ValueChangedThisTick = true;
            actionState.Transitions.Add(new InputActionTransition(
                mapId,
                actionState.Definition.ActionId,
                InputTransitionKind.ValueChanged,
                null,
                hostSequence,
                assignedTick,
                axisX,
                axisY));
        }

        var magnitude = MathF.Sqrt((axisX * axisX) + (axisY * axisY));
        var pressThreshold = actionState.Definition.DownPressThreshold ?? 0.55f;
        var releaseThreshold = actionState.Definition.DownReleaseThreshold ?? 0.45f;
        var isDown = actionState.IsDownAtEnd ? magnitude > releaseThreshold : magnitude >= pressThreshold;

        if (actionState.IsDownAtEnd != isDown)
        {
            var wasDown = actionState.IsDownAtEnd;
            actionState.IsDownAtEnd = isDown;

            if (wasDown && !isDown && (cancellationReason is not null))
            {
                actionState.Transitions.Add(new InputActionTransition(
                    mapId,
                    actionState.Definition.ActionId,
                    InputTransitionKind.Canceled,
                    cancellationReason,
                    hostSequence,
                    assignedTick,
                    axisX,
                    axisY));
                return;
            }

            actionState.PressedThisTick |= isDown;
            actionState.ReleasedThisTick |= !isDown;
            actionState.Transitions.Add(new InputActionTransition(
                mapId,
                actionState.Definition.ActionId,
                isDown ? InputTransitionKind.Pressed : InputTransitionKind.Released,
                null,
                hostSequence,
                assignedTick,
                axisX,
                axisY));
        }
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

            var isNonNeutralInput = input.Kind switch {
                InputInjectionKind.KeyboardKey => input.KeyEventKind == InputKeyEventKind.Down,
                InputInjectionKind.GamepadButton => input.IsDown.GetValueOrDefault(),
                InputInjectionKind.GamepadAxis => input.AxisValue.GetValueOrDefault() != 0.0f,
                InputInjectionKind.GamepadStick => (input.StickValue.GetValueOrDefault().X != 0.0f)
                    || (input.StickValue.GetValueOrDefault().Y != 0.0f),
                InputInjectionKind.GamepadTrigger => input.TriggerValue.GetValueOrDefault() != 0.0f,
                _ => false,
            };

            if (!isNonNeutralInput || !reconciledControls.Add(GetControlKey(input)))
            {
                return CreateInputRejection("InvalidReconciliation", "/State");
            }
        }

        _digitalControlStates.Clear();
        _digitalControlDownTimestamps.Clear();
        _scalarControlValues.Clear();
        _vectorControlValues.Clear();

        foreach (var input in heldInputs)
        {
            var control = GetControlKey(input);

            switch (input.Kind)
            {
                case InputInjectionKind.KeyboardKey:
                case InputInjectionKind.GamepadButton:
                {
                    _digitalControlStates.Add(control, true);
                    _digitalControlDownTimestamps.Add(control, hostTimestamp);
                    break;
                }

                case InputInjectionKind.GamepadAxis:
                {
                    _scalarControlValues.Add(control, NormalizeZero(input.AxisValue.GetValueOrDefault()));
                    break;
                }

                case InputInjectionKind.GamepadStick:
                {
                    var stick = NormalizeStick(input.StickValue.GetValueOrDefault());
                    _vectorControlValues.Add(control, (stick.X, stick.Y));
                    break;
                }

                case InputInjectionKind.GamepadTrigger:
                {
                    _scalarControlValues.Add(control, NormalizeZero(input.TriggerValue.GetValueOrDefault()));
                    break;
                }
            }
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
                actionState.ValueChangedThisTick = false;
                actionState.AxisX = 0.0f;
                actionState.AxisY = 0.0f;
                actionState.Transitions.Clear();
            }

            InitializeMapState(mapState);
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

    internal ImmutableArray<InputActionTransition> ServiceInputControlBoundary()
    {
        EnsureOwnerThread();
        var transitions = ImmutableArray.CreateBuilder<InputActionTransition>();
        var appliedCommand = false;

        if ((_recipientKind == InputRecipientKind.HostLiveUI) && (_inputControlInbox.Count != 0))
        {
            foreach (var mapState in _maps.Values)
            {
                foreach (var actionState in mapState.ActionStates.Values)
                {
                    actionState.ResetTick();
                }
            }
        }

        while (_inputControlInbox.TryPeek(out var control))
        {
            if (_inputInbox.TryPeek(out var input) && IsInputEnvelopeBeforeControl(input, control))
            {
                break;
            }

            _ = _inputControlInbox.Dequeue();
            transitions.AddRange(ApplyInputControlEnvelope(control, assignedTick: null));
            appliedCommand = true;
        }

        if (appliedCommand && (_recipientKind == InputRecipientKind.HostLiveUI))
        {
            PublishHostActionSnapshot();
        }

        return transitions.ToImmutable();
    }

    private ImmutableArray<InputActionTransition> ApplyInputControlEnvelope(
        InputControlEnvelope control,
        long? assignedTick)
    {
        if (!_maps.TryGetValue(control.MapId, out var mapState)
            || (mapState.Map.Revision != control.ExpectedRevision))
        {
            throw new InvalidOperationException("An admitted input-control command no longer matches its map revision.");
        }

        var priorEligibility = CaptureBindingEligibility();
        mapState.SetContextMask(control.NewMask, control.ContextInsertionSequences);
        _lastInsertionSequence = control.LastInsertionSequence;
        RebuildOrderedContexts();
        return RefreshContextEligibility(
            priorEligibility,
            control.HostSequence,
            assignedTick,
            deferTransitions: false);
    }

    private void ApplyInputEnvelope(InputEnvelope envelope)
    {
        var changed = false;

        switch (envelope.Injection.Kind)
        {
            case InputInjectionKind.KeyboardKey:
            {
                if (envelope.Injection.KeyEventKind == InputKeyEventKind.Repeat)
                {
                    return;
                }

                var isDown = envelope.Injection.KeyEventKind == InputKeyEventKind.Down;
                changed = SetDigitalControlState(envelope.Control, isDown, envelope.HostTimestamp);
                break;
            }

            case InputInjectionKind.GamepadButton:
            {
                changed = SetDigitalControlState(
                    envelope.Control,
                    envelope.Injection.IsDown.GetValueOrDefault(),
                    envelope.HostTimestamp);
                break;
            }

            case InputInjectionKind.GamepadAxis:
            {
                changed = SetScalarControlValue(envelope.Control, envelope.Injection.AxisValue.GetValueOrDefault());
                break;
            }

            case InputInjectionKind.GamepadStick:
            {
                var value = NormalizeStick(envelope.Injection.StickValue.GetValueOrDefault());
                changed = SetVectorControlValue(envelope.Control, value.X, value.Y);
                break;
            }

            case InputInjectionKind.GamepadTrigger:
            {
                changed = SetScalarControlValue(envelope.Control, envelope.Injection.TriggerValue.GetValueOrDefault());
                break;
            }
        }

        if (changed)
        {
            UpdateAffectedBindingStates(envelope.Control, envelope);
            RecomputeAffectedActions(envelope.Control, envelope.HostSequence, envelope.AssignedTick);
        }
    }

    private Dictionary<InputBindingRuntimeState, bool> CaptureBindingEligibility()
    {
        var eligibility = new Dictionary<InputBindingRuntimeState, bool>();

        foreach (var mapState in _maps.Values)
        {
            foreach (var bindingState in mapState.BindingStates.Values)
            {
                eligibility.Add(bindingState, bindingState.Definition.Enabled && bindingState.IsContextEligible);
            }
        }

        return eligibility;
    }

    private void ClearInputState()
    {
        _digitalControlDownTimestamps.Clear();
        _digitalControlStates.Clear();
        _inputControlInbox.Clear();
        _inputInbox.Clear();
        _scalarControlValues.Clear();
        _vectorControlValues.Clear();
        _inputOverflowed = false;
        _firstUnacceptedHostSequence = 0;
        _lastAssignedInputTick = -1;
        _lastSeenHostSequence = 0;
        _lastSeenHostTimestamp = 0;
        _lastUnacceptedHostSequence = 0;
        _overflowServiced = false;
        _hostControlTimestamp = 0;
        _orderedContexts = [];
    }

    private int CountPlannedActiveContexts(Guid excludedMapId, ulong replacementMask)
    {
        var count = CountSetBits(replacementMask);

        foreach (var mapState in _maps.Values)
        {
            if (mapState.Map.MapId == excludedMapId)
            {
                continue;
            }

            var activeMask = mapState.ActiveContextMask;

            foreach (var control in _inputControlInbox)
            {
                if (control.MapId == mapState.Map.MapId)
                {
                    activeMask = control.NewMask;
                }
            }

            count += CountSetBits(activeMask);
        }

        return count;
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

    private (ulong ActiveMask, long[] InsertionSequences, long LastInsertionSequence) GetPlannedContextState(InputActionMapState mapState)
    {
        var activeMask = mapState.ActiveContextMask;
        var insertionSequences = mapState.ContextInsertionSequences;
        var lastInsertionSequence = _lastInsertionSequence;

        foreach (var control in _inputControlInbox)
        {
            lastInsertionSequence = control.LastInsertionSequence;

            if (control.MapId == mapState.Map.MapId)
            {
                activeMask = control.NewMask;
                insertionSequences = control.ContextInsertionSequences;
            }
        }

        return (activeMask, insertionSequences, lastInsertionSequence);
    }

    private InputOverflowResult HandleInputOverflow(long? assignedTick)
    {
        _digitalControlDownTimestamps.Clear();
        _inputInbox.Clear();
        _digitalControlStates.Clear();
        _scalarControlValues.Clear();
        _vectorControlValues.Clear();
        var transitions = ImmutableArray.CreateBuilder<InputActionTransition>();

        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            foreach (var action in mapState.OrderedActions)
            {
                var actionState = mapState.ActionStates[action.ActionId];

                if ((action.Kind != InputActionKind.Digital) && ((actionState.AxisX != 0.0f) || (actionState.AxisY != 0.0f)))
                {
                    actionState.AxisX = 0.0f;
                    actionState.AxisY = 0.0f;
                    actionState.ValueChangedThisTick = true;
                    var valueChanged = new InputActionTransition(
                        mapState.Map.MapId,
                        action.ActionId,
                        InputTransitionKind.ValueChanged,
                        null,
                        _lastUnacceptedHostSequence,
                        assignedTick,
                        0.0f,
                        0.0f);
                    actionState.Transitions.Add(valueChanged);
                    transitions.Add(valueChanged);
                }

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

    private void InitializeMapState(InputActionMapState mapState)
    {
        foreach (var bindingState in mapState.BindingStates.Values)
        {
            UpdateBindingState(bindingState, envelope: null);
        }
    }

    private bool IsBindingNeutral(InputBindingRuntimeState bindingState)
    {
        var binding = bindingState.Definition;

        switch (binding.Kind)
        {
            case BindingKind.Button:
            {
                return !IsControlDown(GetControlKey(binding.Control!));
            }

            case BindingKind.Chord:
            {
                foreach (var control in binding.Controls)
                {
                    if (IsControlDown(GetControlKey(control)))
                    {
                        return false;
                    }
                }

                return true;
            }

            case BindingKind.Axis1Keys:
            {
                return !IsControlDown(GetControlKey(binding.Negative!))
                    && !IsControlDown(GetControlKey(binding.Positive!));
            }

            case BindingKind.Axis2Keys:
            {
                return !IsControlDown(GetControlKey(binding.Left!))
                    && !IsControlDown(GetControlKey(binding.Right!))
                    && !IsControlDown(GetControlKey(binding.Down!))
                    && !IsControlDown(GetControlKey(binding.Up!));
            }

            case BindingKind.GamepadAxis1:
            {
                return GetProcessedAxis1Value(binding.Axis1Source!, _scalarControlValues) == 0.0f;
            }

            case BindingKind.GamepadStick2:
            {
                var (x, y) = GetProcessedStick2Value(binding.Stick2Source!, _vectorControlValues);
                return (x == 0.0f) && (y == 0.0f);
            }

            case BindingKind.GamepadTrigger:
            {
                return GetProcessedTriggerValue(binding.TriggerSource!, _scalarControlValues) == 0.0f;
            }

            case BindingKind.AxisThreshold:
            {
                return GetAxisThresholdMetric(binding, _scalarControlValues) <= (binding.ReleaseThreshold ?? 0.45f);
            }

            default:
            {
                return true;
            }
        }
    }

    private bool IsControlDown(InputControlKey control)
        => _digitalControlStates.TryGetValue(control, out var isDown) && isDown;

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
            while (true)
            {
                var inputReady = _inputInbox.TryPeek(out var envelope) && (envelope.AssignedTick <= assignedTick);
                var controlReady = _inputControlInbox.TryPeek(out var control)
                    && (control.AssignedTick is { } controlTick)
                    && (controlTick <= assignedTick);

                if (!inputReady && !controlReady)
                {
                    break;
                }

                if (controlReady && (!inputReady || !IsInputEnvelopeBeforeControl(envelope!, control!)))
                {
                    _ = _inputControlInbox.Dequeue();
                    _ = ApplyInputControlEnvelope(control!, control!.AssignedTick);
                }
                else
                {
                    _ = _inputInbox.Dequeue();
                    ApplyInputEnvelope(envelope!);
                }
            }
        }

        _nextOpenTick = Math.Max(_nextOpenTick, checked(assignedTick + 1));
        var snapshot = CreateActionSnapshot(assignedTick);
        Volatile.Write(ref _simulationActionSnapshot, snapshot);
        return snapshot;
    }

    private void RebuildOrderedContexts()
    {
        var contexts = new List<InputContextOrder>();

        foreach (var mapState in _maps.Values)
        {
            foreach (var context in mapState.Map.Contexts)
            {
                if ((mapState.ActiveContextMask & (1UL << context.Bit)) != 0)
                {
                    contexts.Add(new InputContextOrder(
                        mapState.Map.MapId,
                        mapState.Map.MapId.ToString("D"),
                        context,
                        mapState.ContextInsertionSequences[context.Bit],
                        context.ContextId.ToString("D")));
                }
            }
        }

        _orderedContexts =
        [
            .. contexts.OrderByDescending(static context => context.Context.Priority)
                .ThenByDescending(static context => context.InsertionSequence)
                .ThenBy(static context => context.CanonicalMapId, StringComparer.Ordinal)
                .ThenBy(static context => context.CanonicalContextId, StringComparer.Ordinal),
        ];

        foreach (var mapState in _maps.Values)
        {
            foreach (var bindingState in mapState.BindingStates.Values)
            {
                var matchingContextIndex = -1;

                for (var index = 0; index < _orderedContexts.Length; index++)
                {
                    var context = _orderedContexts[index];

                    if ((context.MapId == mapState.Map.MapId)
                        && ((bindingState.Definition.ContextMask & (1UL << context.Context.Bit)) != 0))
                    {
                        matchingContextIndex = index;
                        break;
                    }
                }

                if (matchingContextIndex < 0)
                {
                    bindingState.ContextRank = -1;
                    bindingState.IsContextEligible = false;
                    continue;
                }

                bindingState.ContextRank = matchingContextIndex;
                var capturedClasses = InputClass.None;

                for (var index = 0; index < matchingContextIndex; index++)
                {
                    capturedClasses |= _orderedContexts[index].Context.CaptureClasses;
                }

                bindingState.IsContextEligible = !bindingState.ControlKeys.Any(
                    control => (capturedClasses & GetInputClass(control)) != 0);
            }
        }

        _orderedConsumableBindings =
        [
            .. _maps.Values.SelectMany(static state => state.BindingStates.Values)
                .Where(static bindingState =>
                    bindingState.Definition.Enabled
                    && ((bindingState.Definition.Flags & InputBindingBehavior.ConsumePhysicalEvent) != 0)
                    && bindingState.IsContextEligible)
                .OrderBy(static bindingState => bindingState.ContextRank)
                .ThenBy(static bindingState => bindingState.Definition.Priority)
                .ThenBy(static bindingState => bindingState.CanonicalMapId, StringComparer.Ordinal)
                .ThenBy(static bindingState => bindingState.CanonicalBindingId, StringComparer.Ordinal),
        ];
    }

    private void RecomputeAffectedActions(InputControlKey changedControl, long hostSequence, long assignedTick)
    {
        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            if (!mapState.BindingsByControl.TryGetValue(changedControl, out var affectedBindings))
            {
                continue;
            }

            foreach (var action in mapState.OrderedActions)
            {
                var isAffected = false;

                foreach (var bindingState in affectedBindings)
                {
                    if (bindingState.Definition.ActionId == action.ActionId)
                    {
                        isAffected = true;
                        break;
                    }
                }

                if (isAffected)
                {
                    ProcessActionEvent(mapState, action, hostSequence, assignedTick);
                }
            }
        }
    }

    private void RecordHostControlSequence(long hostSequence)
    {
        _lastSeenHostSequence = Math.Max(_lastSeenHostSequence, hostSequence);
    }

    private void RecordUnacceptedSequence(long hostSequence)
    {
        if (_firstUnacceptedHostSequence == 0)
        {
            _firstUnacceptedHostSequence = hostSequence;
        }

        _lastUnacceptedHostSequence = hostSequence;
    }

    private ImmutableArray<InputActionTransition> RefreshContextEligibility(
        Dictionary<InputBindingRuntimeState, bool> priorEligibility,
        long hostSequence,
        long? assignedTick,
        bool deferTransitions)
    {
        var transitions = ImmutableArray.CreateBuilder<InputActionTransition>();

        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            foreach (var bindingState in mapState.BindingStates.Values)
            {
                var wasEligible = priorEligibility.TryGetValue(bindingState, out var previous) && previous;
                var isEligible = bindingState.Definition.Enabled && bindingState.IsContextEligible;

                if ((wasEligible != isEligible) && !IsBindingNeutral(bindingState))
                {
                    bindingState.IsDisarmed = true;
                }
            }

            foreach (var action in mapState.OrderedActions)
            {
                var actionState = mapState.ActionStates[action.ActionId];
                var firstTransitionIndex = actionState.Transitions.Count;
                ProcessActionEvent(
                    mapState,
                    action,
                    hostSequence,
                    assignedTick,
                    InputCancellationReason.ContextMasked);

                for (var index = firstTransitionIndex; index < actionState.Transitions.Count; index++)
                {
                    var transition = actionState.Transitions[index];

                    if (deferTransitions)
                    {
                        actionState.PendingTransitions.Add(transition);
                    }
                    else
                    {
                        transitions.Add(transition);
                    }
                }

                if (deferTransitions && (actionState.Transitions.Count > firstTransitionIndex))
                {
                    actionState.Transitions.RemoveRange(
                        firstTransitionIndex,
                        actionState.Transitions.Count - firstTransitionIndex);
                }
            }
        }

        return transitions.ToImmutable();
    }

    private bool SetDigitalControlState(InputControlKey control, bool isDown, long hostTimestamp)
    {
        if (IsControlDown(control) == isDown)
        {
            return false;
        }

        if (isDown)
        {
            _digitalControlStates[control] = true;
            _digitalControlDownTimestamps[control] = hostTimestamp;
        }
        else
        {
            _ = _digitalControlStates.Remove(control);
            _ = _digitalControlDownTimestamps.Remove(control);
        }

        return true;
    }

    private bool SetScalarControlValue(InputControlKey control, float value)
    {
        value = NormalizeZero(value);
        var currentValue = GetScalarControlValue(control, _scalarControlValues);

        if (currentValue == value)
        {
            return false;
        }

        if (value == 0.0f)
        {
            _ = _scalarControlValues.Remove(control);
        }
        else
        {
            _scalarControlValues[control] = value;
        }

        return true;
    }

    private bool SetVectorControlValue(InputControlKey control, float x, float y)
    {
        x = NormalizeZero(x);
        y = NormalizeZero(y);
        var (currentX, currentY) = GetVectorControlValue(control, _vectorControlValues);

        if ((currentX == x) && (currentY == y))
        {
            return false;
        }

        if ((x == 0.0f) && (y == 0.0f))
        {
            _ = _vectorControlValues.Remove(control);
        }
        else
        {
            _vectorControlValues[control] = (x, y);
        }

        return true;
    }

    private void UpdateAffectedBindingStates(InputControlKey changedControl, InputEnvelope envelope)
    {
        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            if (!mapState.BindingsByControl.TryGetValue(changedControl, out var affectedBindings))
            {
                continue;
            }

            foreach (var bindingState in affectedBindings)
            {
                UpdateBindingState(bindingState, envelope);
            }
        }
    }

    private void UpdateBindingState(InputBindingRuntimeState bindingState, InputEnvelope? envelope)
    {
        var binding = bindingState.Definition;

        switch (binding.Kind)
        {
            case BindingKind.Button:
            {
                bindingState.IsDown = IsControlDown(GetControlKey(binding.Control!));
                break;
            }

            case BindingKind.Chord:
            {
                bindingState.IsDown = UpdateChordState(bindingState, envelope);
                break;
            }

            case BindingKind.Axis1Keys:
            {
                var negativeDown = IsControlDown(GetControlKey(binding.Negative!));
                var positiveDown = IsControlDown(GetControlKey(binding.Positive!));
                bindingState.AxisX = (positiveDown == negativeDown) ? 0.0f : positiveDown ? 1.0f : -1.0f;
                bindingState.AxisY = 0.0f;
                break;
            }

            case BindingKind.Axis2Keys:
            {
                var leftDown = IsControlDown(GetControlKey(binding.Left!));
                var rightDown = IsControlDown(GetControlKey(binding.Right!));
                var downDown = IsControlDown(GetControlKey(binding.Down!));
                var upDown = IsControlDown(GetControlKey(binding.Up!));
                var x = (rightDown ? 1.0f : 0.0f) - (leftDown ? 1.0f : 0.0f);
                var y = (upDown ? 1.0f : 0.0f) - (downDown ? 1.0f : 0.0f);
                var magnitudeSquared = (x * x) + (y * y);

                if (magnitudeSquared > 1.0f)
                {
                    var inverseMagnitude = 1.0f / MathF.Sqrt(magnitudeSquared);
                    x *= inverseMagnitude;
                    y *= inverseMagnitude;
                }

                bindingState.AxisX = NormalizeZero(x);
                bindingState.AxisY = NormalizeZero(y);
                break;
            }

            case BindingKind.GamepadAxis1:
            {
                bindingState.AxisX = GetProcessedAxis1Value(binding.Axis1Source!, _scalarControlValues);
                bindingState.AxisY = 0.0f;
                break;
            }

            case BindingKind.GamepadStick2:
            {
                var (x, y) = GetProcessedStick2Value(binding.Stick2Source!, _vectorControlValues);
                bindingState.AxisX = x;
                bindingState.AxisY = y;
                break;
            }

            case BindingKind.GamepadTrigger:
            {
                bindingState.AxisX = GetProcessedTriggerValue(binding.TriggerSource!, _scalarControlValues);
                bindingState.AxisY = 0.0f;
                break;
            }

            case BindingKind.AxisThreshold:
            {
                var metric = GetAxisThresholdMetric(binding, _scalarControlValues);
                var pressThreshold = binding.PressThreshold ?? 0.55f;
                var releaseThreshold = binding.ReleaseThreshold ?? 0.45f;
                bindingState.IsDown = bindingState.IsDown ? metric > releaseThreshold : metric >= pressThreshold;
                break;
            }
        }

        if (envelope is null)
        {
            bindingState.IsDisarmed = !IsBindingNeutral(bindingState);
        }
        else if (bindingState.IsDisarmed && IsBindingNeutral(bindingState))
        {
            bindingState.IsDisarmed = false;
        }
    }

    private bool UpdateChordState(InputBindingRuntimeState bindingState, InputEnvelope? envelope)
    {
        var binding = bindingState.Definition;

        if (envelope is null)
        {
            bindingState.ChordIsDown = false;
            bindingState.ChordStartedHostTimestamp = null;
            bindingState.ChordIsDisarmed = !IsBindingNeutral(bindingState);
            return false;
        }

        var anyDown = false;
        var allDown = true;
        var allUp = true;
        var releaseIsMember = false;

        foreach (var control in binding.Controls)
        {
            var controlKey = GetControlKey(control);
            var isDown = IsControlDown(controlKey);
            anyDown |= isDown;
            allDown &= isDown;
            allUp &= !isDown;
            releaseIsMember |= IsDigitalRelease(envelope) && (controlKey == envelope.Control);
        }

        if (bindingState.ChordIsDown)
        {
            if (allDown)
            {
                return true;
            }

            bindingState.ChordIsDown = false;
            bindingState.ChordIsDisarmed = !allUp;
            bindingState.ChordStartedHostTimestamp = null;
            return false;
        }

        if (bindingState.ChordIsDisarmed)
        {
            if (allUp)
            {
                bindingState.ChordIsDisarmed = false;
                bindingState.ChordStartedHostTimestamp = null;
            }

            return false;
        }

        if (!anyDown)
        {
            bindingState.ChordStartedHostTimestamp = null;
            return false;
        }

        if (bindingState.ChordStartedHostTimestamp is not { } startTimestamp)
        {
            startTimestamp = long.MaxValue;

            foreach (var control in binding.Controls)
            {
                var controlKey = GetControlKey(control);

                if (_digitalControlDownTimestamps.TryGetValue(controlKey, out var downTimestamp))
                {
                    startTimestamp = Math.Min(startTimestamp, downTimestamp);
                }
            }

            bindingState.ChordStartedHostTimestamp = startTimestamp;
        }

        if (releaseIsMember && !allDown)
        {
            bindingState.ChordIsDisarmed = !allUp;
            bindingState.ChordStartedHostTimestamp = null;
            return false;
        }

        if (binding.CompletionWindowMilliseconds is { } windowMilliseconds)
        {
            var windowTicks = checked((long)windowMilliseconds * TimeSpan.TicksPerMillisecond);

            if ((envelope.HostTimestamp - startTimestamp) > windowTicks)
            {
                bindingState.ChordIsDisarmed = !allUp;
                bindingState.ChordStartedHostTimestamp = null;
                return false;
            }
        }

        bindingState.ChordIsDown = allDown;
        return bindingState.ChordIsDown;
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

        internal List<InputActionTransition> PendingTransitions { get; } = [];

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
            Transitions.AddRange(PendingTransitions);

            foreach (var transition in PendingTransitions)
            {
                PressedThisTick |= transition.Kind == InputTransitionKind.Pressed;
                ReleasedThisTick |= transition.Kind == InputTransitionKind.Released;
                ValueChangedThisTick |= transition.Kind == InputTransitionKind.ValueChanged;
            }

            PendingTransitions.Clear();
        }
    }

    private sealed class InputBindingRuntimeState
    {
        internal InputBindingRuntimeState(Guid mapId, BindingDefinition definition)
        {
            CanonicalBindingId = definition.BindingId.ToString("D");
            ControlKeys = GetBindingControlKeys(definition);
            Definition = definition;
            CanonicalMapId = mapId.ToString("D");
            ContextRank = -1;
            MapId = mapId;
        }

        internal float AxisX { get; set; }

        internal float AxisY { get; set; }

        internal string CanonicalBindingId { get; }

        internal string CanonicalMapId { get; }

        internal bool ChordIsDisarmed { get; set; }

        internal bool ChordIsDown { get; set; }

        internal long? ChordStartedHostTimestamp { get; set; }

        internal ImmutableArray<InputControlKey> ConsumptionControls { get; set; } = [];

        internal int ContextRank { get; set; }

        internal ImmutableArray<InputControlKey> ControlKeys { get; }

        internal BindingDefinition Definition { get; }

        internal bool IsContextEligible { get; set; }

        internal bool IsDisarmed { get; set; }

        internal bool IsDown { get; set; }

        internal Guid MapId { get; }
    }

    private readonly record struct InputContextOrder(
        Guid MapId,
        string CanonicalMapId,
        ContextDefinition Context,
        long InsertionSequence,
        string CanonicalContextId);

    private sealed class InputControlEnvelope(
        Guid mapId,
        long expectedRevision,
        ulong newMask,
        long[] contextInsertionSequences,
        long lastInsertionSequence,
        long hostTimestamp,
        long hostSequence,
        long? assignedTick)
    {
        internal long? AssignedTick { get; } = assignedTick;

        internal long[] ContextInsertionSequences { get; } = contextInsertionSequences;

        internal long ExpectedRevision { get; } = expectedRevision;

        internal long HostSequence { get; } = hostSequence;

        internal long HostTimestamp { get; } = hostTimestamp;

        internal long LastInsertionSequence { get; } = lastInsertionSequence;

        internal Guid MapId { get; } = mapId;

        internal ulong NewMask { get; } = newMask;
    }

    private readonly record struct InputControlKey(
        InputControlKind Kind,
        ushort UsagePage,
        ushort Usage,
        string? LogicalKeyName,
        ushort PlayerSlot,
        int Value);

    private enum InputControlKind
    {
        GamepadAxis,
        GamepadButton,
        GamepadStick,
        GamepadTrigger,
        KeyboardHid,
        LogicalKey,
        MouseButton,
    }

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
