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
    private const int MaximumPendingReconciliationInputs = 4096;

    private readonly Dictionary<InputControlKey, List<InputConsumptionLease>> _consumptionLeases = [];
    private readonly Dictionary<InputControlKey, long> _digitalControlDownTimestamps = [];
    private readonly Dictionary<InputControlKey, bool> _digitalControlStates = [];
    private readonly Dictionary<InputControlKey, HashSet<InputDeviceHandle>> _physicalDigitalSources = [];
    private long _firstUnacceptedHostSequence;
    private readonly Queue<InputControlEnvelope> _inputControlInbox = [];
    private readonly Queue<InputEnvelope> _inputInbox = [];
    private long _hostControlTimestamp;
    private InputEnvelope? _inputInboxTail;
    private bool _inputOverflowed;
    private long _lastAssignedInputTick = -1;
    private long _lastSeenHostSequence;
    private long _lastSeenHostTimestamp;
    private long _lastUnacceptedHostSequence;
    private bool _overflowServiced;
    private int _pendingReconciliationInputCount;
    private ImmutableArray<InputContextOrder> _orderedContexts = [];
    private ImmutableArray<InputBindingRuntimeState> _orderedConsumableBindings = [];
    private readonly List<PendingReboundAction> _pendingReboundActions = [];
    private readonly HashSet<InputControlKey> _unreconciledControls = [];
    private readonly List<InputActionState> _terminalActionStates = [];
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

    private static float GetAxisThresholdMetric(BindingDefinition binding, InputInjection injection)
    {
        var value = (binding.ThresholdAxisSource is { } axisSource)
            ? GetProcessedAxis1Value(axisSource, injection.AxisValue.GetValueOrDefault())
            : GetProcessedTriggerValue(binding.ThresholdTriggerSource!, injection.TriggerValue.GetValueOrDefault());

        return binding.ThresholdDirection switch {
            AxisThresholdDirection.Negative => MathF.Max(0.0f, -value),
            AxisThresholdDirection.Positive => MathF.Max(0.0f, value),
            AxisThresholdDirection.Magnitude => MathF.Abs(value),
            _ => 0.0f,
        };
    }

    private static float GetBindingAxisMagnitude(BindingDefinition binding, InputInjection injection)
    {
        return binding.Kind switch {
            BindingKind.GamepadAxis1 => MathF.Abs(
                GetProcessedAxis1Value(binding.Axis1Source!, injection.AxisValue.GetValueOrDefault())),
            BindingKind.GamepadStick2 => GetProcessedStick2Magnitude(
                binding.Stick2Source!,
                injection.StickValue.GetValueOrDefault()),
            BindingKind.GamepadTrigger => GetProcessedTriggerValue(
                binding.TriggerSource!,
                injection.TriggerValue.GetValueOrDefault()),
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
        return GetProcessedAxis1Value(source, GetScalarControlValue(GetControlKey(source), scalarControlValues));
    }

    private static float GetProcessedAxis1Value(GamepadAxis1Source source, float rawValue)
    {
        var value = Math.Clamp(rawValue, -1.0f, 1.0f);
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

    private static float GetProcessedStick2Magnitude(GamepadStick2Source source, Vector2 rawValue)
    {
        var (x, y) = GetProcessedStick2Value(source, (rawValue.X, rawValue.Y));
        return MathF.Sqrt((x * x) + (y * y));
    }

    private static (float X, float Y) GetProcessedStick2Value(
        GamepadStick2Source source,
        Dictionary<InputControlKey, (float X, float Y)> vectorControlValues)
    {
        return GetProcessedStick2Value(source, GetVectorControlValue(GetControlKey(source), vectorControlValues));
    }

    private static (float X, float Y) GetProcessedStick2Value(GamepadStick2Source source, (float X, float Y) rawValue)
    {
        var (rawX, rawY) = rawValue;
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
        return GetProcessedTriggerValue(source, GetScalarControlValue(GetControlKey(source), scalarControlValues));
    }

    private static float GetProcessedTriggerValue(GamepadTriggerSource source, float rawValue)
    {
        var value = Math.Clamp(rawValue, 0.0f, 1.0f);
        return NormalizeZero(MathF.Max(0.0f, (value - source.Deadzone) / (1.0f - source.Deadzone)));
    }

    private static float GetScalarControlValue(
        InputControlKey control,
        Dictionary<InputControlKey, float> scalarControlValues)
        => scalarControlValues.TryGetValue(control, out var value) ? value : 0.0f;

    private static bool GetThresholdBindingDown(InputBindingRuntimeState bindingState, InputInjection injection)
    {
        var binding = bindingState.Definition;
        var metric = GetAxisThresholdMetric(binding, injection);
        var pressThreshold = binding.PressThreshold ?? 0.55f;
        var releaseThreshold = binding.ReleaseThreshold ?? 0.45f;
        return bindingState.IsDown ? metric > releaseThreshold : metric >= pressThreshold;
    }

    private static (float X, float Y) GetVectorControlValue(
        InputControlKey control,
        Dictionary<InputControlKey, (float X, float Y)> vectorControlValues)
        => vectorControlValues.TryGetValue(control, out var value) ? value : (0.0f, 0.0f);

    private static bool IsBindingSuppressedByConsumption(
        InputBindingRuntimeState bindingState,
        Dictionary<InputControlKey, List<InputConsumptionLease>> consumptionLeases)
    {
        foreach (var control in bindingState.ControlKeys)
        {
            if (!consumptionLeases.TryGetValue(control, out var leases))
            {
                continue;
            }

            foreach (var lease in leases)
            {
                if (lease.ContextRank < bindingState.ContextRank)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsDigitalRelease(InputEnvelope envelope)
    {
        if (envelope.Injection is { } injection)
        {
            return injection.Kind switch {
                InputInjectionKind.KeyboardKey => injection.KeyEventKind == InputKeyEventKind.Up,
                InputInjectionKind.GamepadButton => !injection.IsDown.GetValueOrDefault(),
                _ => false,
            };
        }

        return envelope.PhysicalEvent?.Kind is PhysicalInputEventKind.KeyUp
            or PhysicalInputEventKind.PointerButtonUp
            or PhysicalInputEventKind.GamepadButtonUp
            or PhysicalInputEventKind.DeviceRemoved;
    }

    private static bool IsInputEnvelopeBeforeControl(InputEnvelope input, InputControlEnvelope control)
        => (input.HostTimestamp < control.HostTimestamp)
            || ((input.HostTimestamp == control.HostTimestamp) && (input.HostSequence < control.HostSequence));

    private static bool IsNeutralInput(InputInjection injection)
    {
        return injection.Kind switch {
            InputInjectionKind.KeyboardKey => injection.KeyEventKind == InputKeyEventKind.Up,
            InputInjectionKind.GamepadButton => !injection.IsDown.GetValueOrDefault(),
            InputInjectionKind.GamepadAxis => injection.AxisValue.GetValueOrDefault() == 0.0f,
            InputInjectionKind.GamepadStick => (injection.StickValue.GetValueOrDefault().X == 0.0f)
                && (injection.StickValue.GetValueOrDefault().Y == 0.0f),
            InputInjectionKind.GamepadTrigger => injection.TriggerValue.GetValueOrDefault() == 0.0f,
            _ => false,
        };
    }

    private static bool IsNeutralInput(PhysicalInputEvent inputEvent)
    {
        if (inputEvent.Kind is PhysicalInputEventKind.KeyUp
            or PhysicalInputEventKind.PointerButtonUp
            or PhysicalInputEventKind.GamepadButtonUp)
        {
            return true;
        }

        if (inputEvent.Value is not { } value)
        {
            return false;
        }

        return inputEvent.Kind switch {
            PhysicalInputEventKind.GamepadAxis => value.AxisValue == 0.0f,
            PhysicalInputEventKind.GamepadStick => value.StickValue is { X: 0.0f, Y: 0.0f },
            PhysicalInputEventKind.GamepadTrigger => value.TriggerValue == 0.0f,
            _ => false,
        };
    }

    private static bool IsNonNeutralInput(InputInjection injection)
        => !IsNeutralInput(injection)
            && (injection.Kind != InputInjectionKind.KeyboardKey
                || injection.KeyEventKind == InputKeyEventKind.Down);

    private static bool IsNonNeutralInput(PhysicalInputValue value)
    {
        return value.Kind switch {
            PhysicalInputValueKind.Digital or PhysicalInputValueKind.GamepadButton => value.IsDown == true,
            PhysicalInputValueKind.GamepadAxis => value.AxisValue is { } axisValue && axisValue != 0.0f,
            PhysicalInputValueKind.GamepadStick => value.StickValue is { } stickValue
                && ((stickValue.X != 0.0f) || (stickValue.Y != 0.0f)),
            PhysicalInputValueKind.GamepadTrigger => value.TriggerValue is { } triggerValue && triggerValue != 0.0f,
            _ => false,
        };
    }

    private static bool IsValidCancellationRequest(
        InputCancellationReason reason,
        InputClass inputClasses,
        ushort? playerSlot)
    {
        const InputClass allClasses = InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad;
        const InputClass focusedWindowClasses = InputClass.Keyboard | InputClass.Pointing;

        if ((inputClasses == InputClass.None) || ((inputClasses & ~allClasses) != 0))
        {
            return false;
        }

        return reason switch {
            InputCancellationReason.DeviceRemoved => (inputClasses == InputClass.Gamepad) && (playerSlot is not null),
            InputCancellationReason.FocusLost => (inputClasses == focusedWindowClasses) && (playerSlot is null),
            InputCancellationReason.Paused => (inputClasses == allClasses) && (playerSlot is null),
            _ => false,
        };
    }

    private static bool IsValidReconciliationScope(
        InputClass inputClasses,
        ushort? playerSlot,
        bool isPaused)
    {
        const InputClass allClasses = InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad;
        const InputClass focusedWindowClasses = InputClass.Keyboard | InputClass.Pointing;

        if ((inputClasses == InputClass.None) || ((inputClasses & ~allClasses) != 0))
        {
            return false;
        }

        if (isPaused)
        {
            return (inputClasses == allClasses) && (playerSlot is null);
        }

        return ((inputClasses == focusedWindowClasses) && (playerSlot is null))
            || ((inputClasses == InputClass.Gamepad) && (playerSlot is not null))
            || ((inputClasses == allClasses) && (playerSlot is null));
    }

    private static bool MatchesInputScope(
        InputControlKey control,
        InputClass inputClasses,
        ushort? playerSlot)
    {
        return ((GetInputClass(control) & inputClasses) != 0)
            && ((playerSlot is not { } slot)
                || ((GetInputClass(control) == InputClass.Gamepad) && (control.PlayerSlot == slot)));
    }

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
        InputCancellationReason? cancellationReason,
        Dictionary<InputControlKey, List<InputConsumptionLease>> consumptionLeases)
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
                    && !IsBindingSuppressedByConsumption(bindingState, consumptionLeases)
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
                || IsBindingSuppressedByConsumption(bindingState, consumptionLeases)
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

    private static bool TryGetControlKey(
        PhysicalInputEvent inputEvent,
        ushort? playerSlot,
        out InputControlKey control)
    {
        control = default;

        switch (inputEvent.Kind)
        {
            case PhysicalInputEventKind.KeyDown:
            case PhysicalInputEventKind.KeyUp:
            case PhysicalInputEventKind.KeyRepeat:
            case PhysicalInputEventKind.PointerButtonDown:
            case PhysicalInputEventKind.PointerButtonUp:
            {
                if ((playerSlot is not null)
                    || (inputEvent.Value is not { Kind: PhysicalInputValueKind.Digital, DigitalControl: { } digitalControl }))
                {
                    return false;
                }

                control = GetControlKey(digitalControl);
                return GetInputClass(control) is InputClass.Keyboard or InputClass.Pointing;
            }

            case PhysicalInputEventKind.GamepadButtonDown:
            case PhysicalInputEventKind.GamepadButtonUp:
            {
                if ((playerSlot is not { } slot)
                    || (inputEvent.Value is not { Kind: PhysicalInputValueKind.GamepadButton, GamepadButton: { } button }))
                {
                    return false;
                }

                control = new InputControlKey(InputControlKind.GamepadButton, 0, 0, null, slot, (int)button);
                return true;
            }

            case PhysicalInputEventKind.GamepadAxis:
            {
                if ((playerSlot is not { } slot)
                    || (inputEvent.Value is not { Kind: PhysicalInputValueKind.GamepadAxis, Axis: { } axis }))
                {
                    return false;
                }

                control = new InputControlKey(InputControlKind.GamepadAxis, 0, 0, null, slot, (int)axis);
                return true;
            }

            case PhysicalInputEventKind.GamepadStick:
            {
                if ((playerSlot is not { } slot)
                    || (inputEvent.Value is not { Kind: PhysicalInputValueKind.GamepadStick, Stick: { } stick }))
                {
                    return false;
                }

                control = new InputControlKey(InputControlKind.GamepadStick, 0, 0, null, slot, (int)stick);
                return true;
            }

            case PhysicalInputEventKind.GamepadTrigger:
            {
                if ((playerSlot is not { } slot)
                    || (inputEvent.Value is not { Kind: PhysicalInputValueKind.GamepadTrigger, Trigger: { } trigger }))
                {
                    return false;
                }

                control = new InputControlKey(InputControlKind.GamepadTrigger, 0, 0, null, slot, (int)trigger);
                return true;
            }
        }

        return false;
    }

    private static bool TryGetControlKey(
        PhysicalInputValue inputValue,
        ushort? playerSlot,
        out InputControlKey control)
    {
        control = default;

        switch (inputValue.Kind)
        {
            case PhysicalInputValueKind.Digital:
            {
                if ((playerSlot is not null) || (inputValue.DigitalControl is not { } digitalControl))
                {
                    return false;
                }

                control = GetControlKey(digitalControl);
                return GetInputClass(control) is InputClass.Keyboard or InputClass.Pointing;
            }

            case PhysicalInputValueKind.GamepadButton:
            {
                if ((playerSlot is not { } slot) || (inputValue.GamepadButton is not { } button))
                {
                    return false;
                }

                control = new InputControlKey(InputControlKind.GamepadButton, 0, 0, null, slot, (int)button);
                return true;
            }

            case PhysicalInputValueKind.GamepadAxis:
            {
                if ((playerSlot is not { } slot) || (inputValue.Axis is not { } axis))
                {
                    return false;
                }

                control = new InputControlKey(InputControlKind.GamepadAxis, 0, 0, null, slot, (int)axis);
                return true;
            }

            case PhysicalInputValueKind.GamepadStick:
            {
                if ((playerSlot is not { } slot) || (inputValue.Stick is not { } stick))
                {
                    return false;
                }

                control = new InputControlKey(InputControlKind.GamepadStick, 0, 0, null, slot, (int)stick);
                return true;
            }

            case PhysicalInputValueKind.GamepadTrigger:
            {
                if ((playerSlot is not { } slot) || (inputValue.Trigger is not { } trigger))
                {
                    return false;
                }

                control = new InputControlKey(InputControlKind.GamepadTrigger, 0, 0, null, slot, (int)trigger);
                return true;
            }
        }

        return false;
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

    internal InputRoutingReceipt CancelInputState(
        InputCancellationReason reason,
        InputClass inputClasses,
        ushort? playerSlot,
        long hostTimestamp,
        long hostSequence,
        out long discardedInputCount)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequence, 1);
        discardedInputCount = 0;

        if (!IsValidCancellationRequest(reason, inputClasses, playerSlot))
        {
            return CreateRoutingRejection("InvalidCancellation", "/Cancellation");
        }

        if ((reason == InputCancellationReason.Paused)
            ? ((_recipientKind != InputRecipientKind.SimulationTimeline) || _isPaused || _pauseControlPending)
            : _isPaused)
        {
            return CreateRoutingRejection("InvalidCancellation", "/Recipient");
        }

        if ((hostSequence <= _lastSeenHostSequence)
            || (hostTimestamp < _hostControlTimestamp)
            || (hostTimestamp < _lastSeenHostTimestamp))
        {
            return CreateRoutingRejection("InvalidEventOrder", "/HostSequence");
        }

        if (!CanAdmitControlCommand() || (_inputControlInbox.Count >= MaximumControlCommandsPerBoundary))
        {
            return CreateRoutingCapacityFailure("/InputControl");
        }

        var hostTick = (_recipientKind == InputRecipientKind.SimulationTimeline) ? _nextOpenTick : (long?)null;
        var envelope = new InputControlEnvelope(
            InputControlCommandKind.CancelInput,
            Guid.Empty,
            0,
            0,
            [],
            _lastInsertionSequence,
            inputClasses,
            playerSlot,
            reason,
            [],
            hostTimestamp,
            hostSequence,
            hostTick);

        discardedInputCount = DiscardPendingInputCopies(inputClasses, playerSlot);
        _inputControlInbox.Enqueue(envelope);
        RecordHostControlSequence(hostSequence);
        _hostControlTimestamp = hostTimestamp;
        _lastSeenHostTimestamp = hostTimestamp;
        _controlCommandsInBoundary++;

        if (reason == InputCancellationReason.Paused)
        {
            _isPaused = true;
            _pauseControlPending = true;
        }

        return new InputRoutingReceipt(
            InputRoutingStatus.Applied,
            _recipientId,
            hostSequence,
            hostTick,
            null);
    }

    internal InputRoutingReceipt EnqueueHeadlessInjection(
        InputInjection injection,
        long hostTimestamp,
        long hostSequence)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(injection);
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequence, 1);

        if (_recipientKind != InputRecipientKind.SimulationTimeline)
        {
            return CreateInputRejection("InvalidRecipient", "/RecipientKind");
        }

        var segment = _simulationClockSegment;

        if (!segment.IsValid)
        {
            return CreateInputRejection("ClockSegmentUnavailable", "/ClockSegment");
        }

        if (hostTimestamp < segment.HostTimestampStartTicks)
        {
            return CreateInputRejection("InvalidEventOrder", "/HostTimestamp");
        }

        if ((hostTimestamp == segment.HostTimestampStartTicks)
            && (hostSequence < segment.HostSequenceStart))
        {
            return CreateInputRejection("InvalidEventOrder", "/HostSequence");
        }

        if (!segment.TryGetTickIndexForHostEvent(hostTimestamp, hostSequence, out var assignedTick, out var late))
        {
            return CreateInputRejection("NoEligibleTick", "/ClockSegment");
        }

        return EnqueueHeadlessInjection(injection, hostTimestamp, hostSequence, assignedTick, late);
    }

    internal InputRoutingReceipt EnqueueHeadlessInjection(
        InputInjection injection,
        long hostTimestamp,
        long hostSequence,
        long assignedTick,
        bool late = false)
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

        var effectiveLate = late || (assignedTick < _nextOpenTick);
        var effectiveTick = Math.Max(assignedTick, _nextOpenTick);

        var control = GetControlKey(injection);
        var envelope = new InputEnvelope(injection, control, hostTimestamp, hostSequence, effectiveTick, effectiveLate);

        return EnqueueInputEnvelope(envelope, hostTimestamp, hostSequence, assignedTick, late);
    }

    internal InputRoutingReceipt EnqueuePhysicalEvent(
        PhysicalInputEvent inputEvent,
        ushort? playerSlot,
        out long discardedInputCount)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(inputEvent);
        discardedInputCount = 0;

        if (_recipientKind != InputRecipientKind.SimulationTimeline)
        {
            return CreateInputRejection("InvalidRecipient", "/RecipientKind");
        }

        if (inputEvent.Kind == PhysicalInputEventKind.FocusLost)
        {
            if (playerSlot is not null)
            {
                return CreateInputRejection("InvalidPlayerSlot", "/PlayerSlot");
            }

            return CancelInputState(
                InputCancellationReason.FocusLost,
                InputClass.Keyboard | InputClass.Pointing,
                playerSlot: null,
                inputEvent.HostTimestamp,
                inputEvent.HostSequence,
                out discardedInputCount);
        }

        if (inputEvent.Kind == PhysicalInputEventKind.DeviceRemoved)
        {
            if (playerSlot is { } slot)
            {
                return CancelInputState(
                    InputCancellationReason.DeviceRemoved,
                    InputClass.Gamepad,
                    slot,
                    inputEvent.HostTimestamp,
                    inputEvent.HostSequence,
                    out discardedInputCount);
            }

            var removedDevice = inputEvent.Device
                ?? throw new InvalidOperationException("A physical device-removal event has no device identity.");
            return QueuePhysicalDeviceRemoval(removedDevice, inputEvent.HostTimestamp, inputEvent.HostSequence, out discardedInputCount);
        }

        if (inputEvent.Kind == PhysicalInputEventKind.StateReconciliation)
        {
            var isDeviceSource = inputEvent.Device is not null;

            if (isDeviceSource && (playerSlot is null))
            {
                return CreateInputRejection("UnassignedDevice", "/PlayerSlot");
            }

            if (!isDeviceSource && (playerSlot is not null))
            {
                return CreateInputRejection("InvalidPlayerSlot", "/PlayerSlot");
            }

            var inputClasses = isDeviceSource
                ? InputClass.Gamepad
                : InputClass.Keyboard | InputClass.Pointing;

            return ReconcileInputState(
                [],
                inputClasses,
                playerSlot,
                inputEvent.HostTimestamp,
                inputEvent.HostSequence,
                out discardedInputCount,
                physicalInputs: inputEvent.ReconciliationState);
        }

        var isGamepadEvent = inputEvent.Kind is PhysicalInputEventKind.GamepadButtonDown
            or PhysicalInputEventKind.GamepadButtonUp
            or PhysicalInputEventKind.GamepadAxis
            or PhysicalInputEventKind.GamepadStick
            or PhysicalInputEventKind.GamepadTrigger;

        if (isGamepadEvent && (playerSlot is null))
        {
            return CreateInputRejection("UnassignedDevice", "/PlayerSlot");
        }

        if (!isGamepadEvent && (playerSlot is not null))
        {
            return CreateInputRejection("InvalidPlayerSlot", "/PlayerSlot");
        }

        if (!TryGetControlKey(inputEvent, playerSlot, out var control))
        {
            return CreateInputRejection("UnsupportedPhysicalEvent", "/Kind");
        }

        var segment = _simulationClockSegment;

        if (!segment.IsValid)
        {
            return CreateInputRejection("ClockSegmentUnavailable", "/ClockSegment");
        }

        if (inputEvent.HostTimestamp < segment.HostTimestampStartTicks)
        {
            return CreateInputRejection("InvalidEventOrder", "/HostTimestamp");
        }

        if ((inputEvent.HostTimestamp == segment.HostTimestampStartTicks)
            && (inputEvent.HostSequence < segment.HostSequenceStart))
        {
            return CreateInputRejection("InvalidEventOrder", "/HostSequence");
        }

        if (!segment.TryGetTickIndexForHostEvent(
            inputEvent.HostTimestamp,
            inputEvent.HostSequence,
            out var assignedTick,
            out var late))
        {
            return CreateInputRejection("NoEligibleTick", "/ClockSegment");
        }

        var envelope = new InputEnvelope(
            inputEvent,
            control,
            inputEvent.HostTimestamp,
            inputEvent.HostSequence,
            Math.Max(assignedTick, _nextOpenTick),
            late || (assignedTick < _nextOpenTick));
        return EnqueueInputEnvelope(envelope, inputEvent.HostTimestamp, inputEvent.HostSequence, assignedTick, late);
    }

    internal InputRoutingReceipt ReconcileHeadlessInputState(
        ImmutableArray<InputInjection> heldInputs,
        long hostTimestamp,
        long hostSequence)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequence, 1);

        var isOverflowRecovery = _inputOverflowed && _overflowServiced;
        var isPauseRecovery = _isPaused && !_pauseControlPending;

        if ((!isOverflowRecovery && !isPauseRecovery) || (_inputControlInbox.Count != 0))
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
        _physicalDigitalSources.Clear();
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
        _inputInboxTail = null;
        _inputOverflowed = false;
        _overflowServiced = false;
        _isPaused = false;
        _pauseControlPending = false;
        _pendingReconciliationInputCount = 0;
        _unreconciledControls.Clear();
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

        RefreshAllConsumptionLeases();
        return new InputRoutingReceipt(InputRoutingStatus.Applied, _recipientId, hostSequence, null, null);
    }

    internal InputRoutingReceipt ReconcileInputState(
        ImmutableArray<InputInjection> heldInputs,
        InputClass inputClasses,
        ushort? playerSlot,
        long hostTimestamp,
        long hostSequence,
        out long discardedInputCount,
        ImmutableArray<PhysicalInputValue> physicalInputs = default)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequence, 1);
        discardedInputCount = 0;
        var physicalInputCount = physicalInputs.IsDefault ? 0 : physicalInputs.Length;
        var reconciliationInputCount = (long)heldInputs.Length + physicalInputCount;
        var requiresWholePausedState = _isPaused && physicalInputs.IsDefault;

        if (heldInputs.IsDefault
            || (reconciliationInputCount > MaximumPendingReconciliationInputs)
            || (_inputOverflowed && !_overflowServiced)
            || !IsValidReconciliationScope(inputClasses, playerSlot, requiresWholePausedState))
        {
            return CreateRoutingRejection("InvalidReconciliation", "/State");
        }

        if (_isPaused && _pauseControlPending)
        {
            return CreateRoutingRejection("InvalidReconciliation", "/PauseBoundary");
        }

        if (reconciliationInputCount > (MaximumPendingReconciliationInputs - _pendingReconciliationInputCount))
        {
            return CreateRoutingCapacityFailure("/Reconciliation");
        }

        var reconciledControls = new HashSet<InputControlKey>();

        foreach (var input in heldInputs)
        {
            ArgumentNullException.ThrowIfNull(input);

            if (!IsNonNeutralInput(input)
                || !MatchesInputScope(GetControlKey(input), inputClasses, playerSlot)
                || !reconciledControls.Add(GetControlKey(input)))
            {
                return CreateRoutingRejection("InvalidReconciliation", "/State");
            }
        }

        if (!physicalInputs.IsDefault)
        {
            foreach (var input in physicalInputs)
            {
                if (!IsNonNeutralInput(input)
                    || !TryGetControlKey(input, playerSlot, out var inputControl)
                    || !MatchesInputScope(inputControl, inputClasses, playerSlot)
                    || !reconciledControls.Add(inputControl))
                {
                    return CreateRoutingRejection("InvalidReconciliation", "/State");
                }
            }
        }

        if ((hostSequence <= _lastSeenHostSequence)
            || (hostTimestamp < _hostControlTimestamp)
            || (hostTimestamp < _lastSeenHostTimestamp))
        {
            return CreateRoutingRejection("InvalidEventOrder", "/HostSequence");
        }

        if (!CanAdmitControlCommand() || (_inputControlInbox.Count >= MaximumControlCommandsPerBoundary))
        {
            return CreateRoutingCapacityFailure("/InputControl");
        }

        var hostTick = (_recipientKind == InputRecipientKind.SimulationTimeline) ? _nextOpenTick : (long?)null;
        var envelope = new InputControlEnvelope(
            InputControlCommandKind.ReconcileInput,
            Guid.Empty,
            0,
            0,
            [],
            _lastInsertionSequence,
            inputClasses,
            playerSlot,
            null,
            heldInputs,
            hostTimestamp,
            hostSequence,
            hostTick,
            physicalInputs);

        discardedInputCount = DiscardPendingInputCopies(inputClasses, playerSlot);
        _inputControlInbox.Enqueue(envelope);
        _pendingReconciliationInputCount += checked((int)reconciliationInputCount);
        RecordHostControlSequence(hostSequence);
        _hostControlTimestamp = hostTimestamp;
        _lastSeenHostTimestamp = hostTimestamp;
        _controlCommandsInBoundary++;

        return new InputRoutingReceipt(
            InputRoutingStatus.Applied,
            _recipientId,
            hostSequence,
            hostTick,
            null);
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

    internal void SetSimulationClockSegment(in SimulationClockSegment segment)
    {
        EnsureOwnerThread();

        if (_recipientKind != InputRecipientKind.SimulationTimeline)
        {
            throw new InvalidOperationException("Only a simulation timeline owns a simulation clock segment.");
        }

        if (!segment.IsValid)
        {
            throw new ArgumentException("The selected simulation clock segment is not initialized.", nameof(segment));
        }

        if ((segment.HostTimestampStartTicks < _hostControlTimestamp)
            || (segment.HostTimestampStartTicks < _lastSeenHostTimestamp)
            || (segment.HostSequenceStart <= _lastSeenHostSequence))
        {
            throw new ArgumentException("The simulation clock segment starts before the recipient's next host event.", nameof(segment));
        }

        if (_simulationClockSegment.IsValid)
        {
            if ((segment.Revision <= _simulationClockSegment.Revision)
                || (segment.TickRate != _simulationClockSegment.TickRate)
                || (segment.HostTimestampStartTicks < _simulationClockSegment.HostTimestampStartTicks)
                || (segment.HostSequenceStart < _simulationClockSegment.HostSequenceStart)
                || (segment.NextOpenTick != _nextOpenTick))
            {
                throw new ArgumentException("The simulation clock segment regresses or disagrees with the recipient timeline.", nameof(segment));
            }
        }
        else
        {
            if ((_inputInbox.Count != 0)
                || (_inputControlInbox.Count != 0)
                || (_lastAssignedInputTick >= 0)
                || (segment.NextOpenTick < _nextOpenTick))
            {
                throw new ArgumentException("The first simulation clock segment cannot skip pending recipient state.", nameof(segment));
            }

            _nextOpenTick = segment.NextOpenTick;
        }

        _simulationClockSegment = segment;
    }

    private void AddConsumptionChangedControl(
        InputControlKey control,
        HashSet<InputControlKey> changedControls,
        HashSet<InputBindingRuntimeState> affectedBindingStates)
    {
        _ = changedControls.Add(control);

        foreach (var mapState in _maps.Values)
        {
            if (mapState.BindingsByControl.TryGetValue(control, out var bindings))
            {
                foreach (var bindingState in bindings)
                {
                    _ = affectedBindingStates.Add(bindingState);
                }
            }
        }
    }

    private ImmutableArray<InputActionTransition> ApplyContextMaskEnvelope(
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

    private ImmutableArray<InputActionTransition> ApplyInputCancellationEnvelope(
        InputControlEnvelope control,
        long? assignedTick)
    {
        if (control.CancellationReason is not { } reason)
        {
            throw new InvalidOperationException("An admitted input-cancellation command has no reason.");
        }

        ClearInputValuesForScope(control.InputClasses, control.PlayerSlot);
        MarkUnreconciledControls(control.InputClasses, control.PlayerSlot);
        UpdateBindingsForScope(control.InputClasses, control.PlayerSlot);
        RefreshAllConsumptionLeases();
        var transitions = RecomputeAllActions(
            control.HostSequence,
            assignedTick,
            reason);

        if (reason == InputCancellationReason.Paused)
        {
            _pauseControlPending = false;
        }

        return transitions;
    }

    private ImmutableArray<InputActionTransition> ApplyInputControlEnvelope(
        InputControlEnvelope control,
        long? assignedTick)
    {
        switch (control.Kind)
        {
            case InputControlCommandKind.CancelInput:
            {
                return ApplyInputCancellationEnvelope(control, assignedTick);
            }

            case InputControlCommandKind.ReconcileInput:
            {
                return ApplyInputReconciliationEnvelope(control, assignedTick);
            }

            case InputControlCommandKind.RemovePhysicalDevice:
            {
                return ApplyPhysicalDeviceRemovalEnvelope(control, assignedTick);
            }

            case InputControlCommandKind.SetActiveContextMask:
            {
                return ApplyContextMaskEnvelope(control, assignedTick);
            }
        }

        throw new InvalidOperationException("An admitted input-control command has an unknown kind.");
    }

    private void ApplyInputEnvelope(InputEnvelope envelope)
    {
        var changed = false;
        var wasUnreconciled = _unreconciledControls.Contains(envelope.Control);

        if (envelope.Injection is { } injection)
        {
            switch (injection.Kind)
            {
                case InputInjectionKind.KeyboardKey:
                {
                    if (injection.KeyEventKind == InputKeyEventKind.Repeat)
                    {
                        return;
                    }

                    var isDown = injection.KeyEventKind == InputKeyEventKind.Down;
                    changed = SetDigitalControlState(envelope.Control, isDown, envelope.HostTimestamp);
                    break;
                }

                case InputInjectionKind.GamepadButton:
                {
                    changed = SetDigitalControlState(
                        envelope.Control,
                        injection.IsDown.GetValueOrDefault(),
                        envelope.HostTimestamp);
                    break;
                }

                case InputInjectionKind.GamepadAxis:
                {
                    changed = SetScalarControlValue(envelope.Control, injection.AxisValue.GetValueOrDefault());
                    break;
                }

                case InputInjectionKind.GamepadStick:
                {
                    var value = NormalizeStick(injection.StickValue.GetValueOrDefault());
                    changed = SetVectorControlValue(envelope.Control, value.X, value.Y);
                    break;
                }

                case InputInjectionKind.GamepadTrigger:
                {
                    changed = SetScalarControlValue(envelope.Control, injection.TriggerValue.GetValueOrDefault());
                    break;
                }
            }
        }
        else if (envelope.PhysicalEvent is { } physicalEvent)
        {
            var value = physicalEvent.Value
                ?? throw new InvalidOperationException("An admitted action-state physical event has no control value.");

            switch (physicalEvent.Kind)
            {
                case PhysicalInputEventKind.KeyDown:
                case PhysicalInputEventKind.PointerButtonDown:
                case PhysicalInputEventKind.GamepadButtonDown:
                {
                    var device = physicalEvent.Device
                        ?? throw new InvalidOperationException("An admitted physical digital event has no device identity.");
                    changed = SetPhysicalDigitalControlState(envelope.Control, isDown: true, device, physicalEvent.HostTimestamp);
                    break;
                }

                case PhysicalInputEventKind.KeyUp:
                case PhysicalInputEventKind.PointerButtonUp:
                case PhysicalInputEventKind.GamepadButtonUp:
                {
                    var device = physicalEvent.Device
                        ?? throw new InvalidOperationException("An admitted physical digital event has no device identity.");
                    changed = SetPhysicalDigitalControlState(envelope.Control, isDown: false, device, physicalEvent.HostTimestamp);
                    break;
                }

                case PhysicalInputEventKind.KeyRepeat:
                {
                    var device = physicalEvent.Device
                        ?? throw new InvalidOperationException("An admitted physical key repeat has no device identity.");
                    changed = SetPhysicalDigitalControlState(envelope.Control, isDown: true, device, physicalEvent.HostTimestamp);

                    if (changed)
                    {
                        _ = _unreconciledControls.Add(envelope.Control);
                        DisarmBindingsForControl(envelope.Control);
                    }

                    break;
                }

                case PhysicalInputEventKind.GamepadAxis:
                {
                    changed = SetScalarControlValue(envelope.Control, value.AxisValue.GetValueOrDefault());
                    break;
                }

                case PhysicalInputEventKind.GamepadStick:
                {
                    var stick = NormalizeStick(value.StickValue.GetValueOrDefault());
                    changed = SetVectorControlValue(envelope.Control, stick.X, stick.Y);
                    break;
                }

                case PhysicalInputEventKind.GamepadTrigger:
                {
                    changed = SetScalarControlValue(envelope.Control, value.TriggerValue.GetValueOrDefault());
                    break;
                }

                default:
                {
                    throw new InvalidOperationException("An admitted physical input event has no action-state mapping.");
                }
            }
        }
        else
        {
            throw new InvalidOperationException("An admitted input envelope has no value.");
        }

        var isNeutral = (envelope.Injection is { } injectionValue)
            ? IsNeutralInput(injectionValue)
            : IsNeutralInput(envelope.PhysicalEvent
                ?? throw new InvalidOperationException("An admitted input envelope has no physical event."));

        var hasPhysicalSources = _physicalDigitalSources.TryGetValue(envelope.Control, out var physicalSources)
            && (physicalSources.Count != 0);

        if (wasUnreconciled && isNeutral && !IsControlDown(envelope.Control) && !hasPhysicalSources)
        {
            _ = _unreconciledControls.Remove(envelope.Control);
        }

        if (wasUnreconciled && !isNeutral)
        {
            DisarmBindingsForControl(envelope.Control);
        }

        if (changed || wasUnreconciled)
        {
            var affectedControls = UpdateAffectedBindingStates(envelope.Control, envelope);
            RecomputeAffectedActions(affectedControls, envelope.HostSequence, envelope.AssignedTick);
        }
    }

    private ImmutableArray<InputActionTransition> ApplyInputReconciliationEnvelope(
        InputControlEnvelope control,
        long? assignedTick)
    {
        ClearInputValuesForScope(control.InputClasses, control.PlayerSlot);
        _ = _unreconciledControls.RemoveWhere(
            key => MatchesInputScope(key, control.InputClasses, control.PlayerSlot));

        foreach (var input in control.ReconciliationInputs)
        {
            var inputControl = GetControlKey(input);

            switch (input.Kind)
            {
                case InputInjectionKind.KeyboardKey:
                case InputInjectionKind.GamepadButton:
                {
                    _digitalControlStates.Add(inputControl, true);
                    _digitalControlDownTimestamps.Add(inputControl, control.HostTimestamp);
                    break;
                }

                case InputInjectionKind.GamepadAxis:
                {
                    _scalarControlValues.Add(inputControl, NormalizeZero(input.AxisValue.GetValueOrDefault()));
                    break;
                }

                case InputInjectionKind.GamepadStick:
                {
                    var stick = NormalizeStick(input.StickValue.GetValueOrDefault());
                    _vectorControlValues.Add(inputControl, (stick.X, stick.Y));
                    break;
                }

                case InputInjectionKind.GamepadTrigger:
                {
                    _scalarControlValues.Add(inputControl, NormalizeZero(input.TriggerValue.GetValueOrDefault()));
                    break;
                }
            }
        }

        if (!control.PhysicalReconciliationInputs.IsDefault)
        {
            foreach (var input in control.PhysicalReconciliationInputs)
            {
                if (!TryGetControlKey(input, control.PlayerSlot, out var inputControl))
                {
                    throw new InvalidOperationException("An admitted physical reconciliation value has no control key.");
                }

                switch (input.Kind)
                {
                    case PhysicalInputValueKind.Digital:
                    case PhysicalInputValueKind.GamepadButton:
                    {
                        _digitalControlStates.Add(inputControl, true);
                        _digitalControlDownTimestamps.Add(inputControl, control.HostTimestamp);
                        break;
                    }

                    case PhysicalInputValueKind.GamepadAxis:
                    {
                        _scalarControlValues.Add(inputControl, NormalizeZero(input.AxisValue.GetValueOrDefault()));
                        break;
                    }

                    case PhysicalInputValueKind.GamepadStick:
                    {
                        var stick = NormalizeStick(input.StickValue.GetValueOrDefault());
                        _vectorControlValues.Add(inputControl, (stick.X, stick.Y));
                        break;
                    }

                    case PhysicalInputValueKind.GamepadTrigger:
                    {
                        _scalarControlValues.Add(inputControl, NormalizeZero(input.TriggerValue.GetValueOrDefault()));
                        break;
                    }

                    default:
                    {
                        throw new InvalidOperationException("An admitted physical reconciliation value has an unknown kind.");
                    }
                }
            }
        }

        if (_inputOverflowed && (_lastUnacceptedHostSequence > control.HostSequence))
        {
            MarkUnreconciledControls(control.InputClasses, control.PlayerSlot);
        }

        UpdateBindingsForScope(control.InputClasses, control.PlayerSlot);
        RefreshAllConsumptionLeases();

        var physicalReconciliationInputCount = control.PhysicalReconciliationInputs.IsDefault
            ? 0
            : control.PhysicalReconciliationInputs.Length;
        var reconciliationInputCount = control.ReconciliationInputs.Length + physicalReconciliationInputCount;

        if (reconciliationInputCount > _pendingReconciliationInputCount)
        {
            throw new InvalidOperationException("The pending reconciliation input count is inconsistent.");
        }

        _pendingReconciliationInputCount -= reconciliationInputCount;

        if (_isPaused && !_pauseControlPending && _unreconciledControls.Count == 0)
        {
            _isPaused = false;
        }

        if (_inputOverflowed
            && _overflowServiced
            && (_lastUnacceptedHostSequence <= control.HostSequence)
            && (_unreconciledControls.Count == 0))
        {
            _inputOverflowed = false;
            _overflowServiced = false;
            _firstUnacceptedHostSequence = 0;
            _lastUnacceptedHostSequence = 0;
            _lastAssignedInputTick = _nextOpenTick;
        }

        return RecomputeAllActions(control.HostSequence, assignedTick, cancellationReason: null);
    }

    private void ApplyPendingReboundActions(long? assignedTick)
    {
        for (var index = 0; index < _pendingReboundActions.Count;)
        {
            var rebound = _pendingReboundActions[index];

            if (rebound.AssignedTick is { } reboundTick)
            {
                if ((assignedTick is not { } currentTick) || (reboundTick > currentTick))
                {
                    index++;
                    continue;
                }
            }
            else if (assignedTick is not null)
            {
                index++;
                continue;
            }

            var transitions = ImmutableArray.CreateBuilder<InputActionTransition>(
                rebound.PendingTransitions.Length + (rebound.WasDown ? 1 : 0));
            transitions.AddRange(rebound.PendingTransitions);

            if (rebound.WasDown)
            {
                transitions.Add(new InputActionTransition(
                    rebound.MapId,
                    rebound.ActionId,
                    InputTransitionKind.Canceled,
                    InputCancellationReason.Rebound,
                    rebound.HostSequence,
                    rebound.AssignedTick,
                    rebound.AxisX,
                    rebound.AxisY));
            }

            var transitionArray = transitions.MoveToImmutable();

            if (_maps.TryGetValue(rebound.MapId, out var mapState)
                && mapState.ActionStates.TryGetValue(rebound.ActionId, out var actionState))
            {
                actionState.Transitions.AddRange(transitionArray);

                foreach (var transition in transitionArray)
                {
                    actionState.PressedThisTick |= transition.Kind == InputTransitionKind.Pressed;
                    actionState.ReleasedThisTick |= transition.Kind == InputTransitionKind.Released;
                    actionState.ValueChangedThisTick |= transition.Kind == InputTransitionKind.ValueChanged;
                }
            }
            else
            {
                var pressedThisTick = transitionArray.Any(static transition => transition.Kind == InputTransitionKind.Pressed);
                var releasedThisTick = transitionArray.Any(static transition => transition.Kind == InputTransitionKind.Released);
                var valueChangedThisTick = transitionArray.Any(static transition => transition.Kind == InputTransitionKind.ValueChanged);
                _terminalActionStates.Add(new InputActionState(
                    rebound.MapId,
                    rebound.ActionId,
                    rebound.MapRevision,
                    rebound.Kind,
                    isDownAtEnd: false,
                    pressedThisTick,
                    releasedThisTick,
                    valueChangedThisTick,
                    rebound.AxisX,
                    rebound.AxisY,
                    transitionArray));
            }

            _pendingReboundActions.RemoveAt(index);
        }
    }

    private ImmutableArray<InputActionTransition> ApplyPhysicalDeviceRemovalEnvelope(
        InputControlEnvelope control,
        long? assignedTick)
    {
        if ((control.PhysicalSourceDevice is not { IsValid: true } device)
            || (control.CancellationReason != InputCancellationReason.DeviceRemoved)
            || (control.InputClasses != (InputClass.Keyboard | InputClass.Pointing))
            || (control.PlayerSlot is not null))
        {
            throw new InvalidOperationException("An admitted physical-device removal command has an invalid scope.");
        }

        var effectiveTick = assignedTick
            ?? control.AssignedTick
            ?? throw new InvalidOperationException("An admitted physical-device removal command has no assigned tick.");
        var physicalEvent = new PhysicalInputEvent(
            PhysicalInputEventKind.DeviceRemoved,
            control.HostTimestamp,
            control.HostSequence,
            device: device);
        var changed = false;

        foreach (var entry in _physicalDigitalSources.ToArray())
        {
            if (!MatchesInputScope(entry.Key, control.InputClasses, control.PlayerSlot)
                || !entry.Value.Remove(device))
            {
                continue;
            }

            if (entry.Value.Count != 0)
            {
                continue;
            }

            _ = _physicalDigitalSources.Remove(entry.Key);

            if (!SetDigitalControlState(entry.Key, isDown: false, control.HostTimestamp))
            {
                continue;
            }

            var envelope = new InputEnvelope(
                physicalEvent,
                entry.Key,
                control.HostTimestamp,
                control.HostSequence,
                effectiveTick,
                late: false);
            _ = UpdateAffectedBindingStates(entry.Key, envelope);
            changed = true;
        }

        return changed
            ? RecomputeAllActions(
                control.HostSequence,
                effectiveTick,
                InputCancellationReason.DeviceRemoved)
            : [];
    }

    private bool CanCoalesceInputEnvelope(InputEnvelope previous, InputEnvelope next)
    {
        if ((previous.Injection is not { } previousInjection)
            || (next.Injection is not { } nextInjection)
            || (_inputInbox.Count != 1)
            || (_inputControlInbox.Count != 0)
            || !previous.CanCoalesceWith(next))
        {
            return false;
        }

        foreach (var mapState in _maps.Values)
        {
            if (!mapState.BindingsByControl.TryGetValue(previous.Control, out var bindings))
            {
                continue;
            }

            foreach (var bindingState in bindings)
            {
                var binding = bindingState.Definition;

                if (!binding.Enabled || !bindingState.IsContextEligible)
                {
                    continue;
                }

                if (bindingState.IsDisarmed || _unreconciledControls.Contains(previous.Control))
                {
                    return false;
                }

                if (IsBindingSuppressedByConsumption(bindingState, _consumptionLeases))
                {
                    continue;
                }

                if ((binding.Flags & InputBindingBehavior.ConsumePhysicalEvent) != 0)
                {
                    return false;
                }

                if (binding.Kind == BindingKind.AxisThreshold)
                {
                    if (GetThresholdBindingDown(bindingState, previousInjection) != bindingState.IsDown
                        || GetThresholdBindingDown(bindingState, nextInjection) != bindingState.IsDown)
                    {
                        return false;
                    }

                    continue;
                }

                if (binding.Kind is not (BindingKind.GamepadAxis1 or BindingKind.GamepadStick2 or BindingKind.GamepadTrigger))
                {
                    continue;
                }

                var actionState = mapState.ActionStates[binding.ActionId];
                var previousMagnitude = GetBindingAxisMagnitude(binding, previousInjection);
                var nextMagnitude = GetBindingAxisMagnitude(binding, nextInjection);
                var pressThreshold = actionState.Definition.DownPressThreshold ?? 0.55f;
                var releaseThreshold = actionState.Definition.DownReleaseThreshold ?? 0.45f;

                if (actionState.IsDownAtEnd
                    ? ((previousMagnitude <= releaseThreshold) || (nextMagnitude <= releaseThreshold))
                    : ((previousMagnitude >= pressThreshold) || (nextMagnitude >= pressThreshold)))
                {
                    return false;
                }
            }
        }

        return true;
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
        _consumptionLeases.Clear();
        _digitalControlDownTimestamps.Clear();
        _digitalControlStates.Clear();
        _physicalDigitalSources.Clear();
        _inputControlInbox.Clear();
        _inputInbox.Clear();
        _inputInboxTail = null;
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
        _isPaused = false;
        _pauseControlPending = false;
        _pendingReconciliationInputCount = 0;
        _pendingReboundActions.Clear();
        _orderedConsumableBindings = [];
        _orderedContexts = [];
        _terminalActionStates.Clear();
        _unreconciledControls.Clear();
    }

    private void ClearInputValuesForScope(InputClass inputClasses, ushort? playerSlot)
    {
        foreach (var control in _digitalControlStates.Keys.Where(key => MatchesInputScope(key, inputClasses, playerSlot)).ToArray())
        {
            _ = _digitalControlStates.Remove(control);
        }

        foreach (var control in _physicalDigitalSources.Keys.Where(key => MatchesInputScope(key, inputClasses, playerSlot)).ToArray())
        {
            _ = _physicalDigitalSources.Remove(control);
        }

        foreach (var control in _digitalControlDownTimestamps.Keys.Where(key => MatchesInputScope(key, inputClasses, playerSlot)).ToArray())
        {
            _ = _digitalControlDownTimestamps.Remove(control);
        }

        foreach (var control in _scalarControlValues.Keys.Where(key => MatchesInputScope(key, inputClasses, playerSlot)).ToArray())
        {
            _ = _scalarControlValues.Remove(control);
        }

        foreach (var control in _vectorControlValues.Keys.Where(key => MatchesInputScope(key, inputClasses, playerSlot)).ToArray())
        {
            _ = _vectorControlValues.Remove(control);
        }
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
                if ((control.Kind == InputControlCommandKind.SetActiveContextMask)
                    && (control.MapId == mapState.Map.MapId))
                {
                    activeMask = control.NewMask;
                }
            }

            count += CountSetBits(activeMask);
        }

        return count;
    }

    private InputRoutingReceipt CreateInputCapacityFailure(long hostSequence, long assignedTick, bool late = false)
        => new InputRoutingReceipt(
            InputRoutingStatus.CapacityExceeded,
            _recipientId,
            hostSequence,
            assignedTick,
            new ValidationIssue("CapacityExceeded", "/InputInbox"),
            late);

    private InputRoutingReceipt CreateInputRejection(string code, string path)
        => new InputRoutingReceipt(
            InputRoutingStatus.Rejected,
            _recipientId,
            null,
            null,
            new ValidationIssue(code, path));

    private void DisarmBindingsForConsumptionChanges(HashSet<InputControlKey> changedControls)
    {
        foreach (var control in changedControls)
        {
            foreach (var mapState in _maps.Values)
            {
                if (!mapState.BindingsByControl.TryGetValue(control, out var bindings))
                {
                    continue;
                }

                foreach (var bindingState in bindings)
                {
                    if (bindingState.Definition.Enabled
                        && bindingState.IsContextEligible
                        && IsBindingSuppressedByConsumption(bindingState, _consumptionLeases)
                        && !IsBindingNeutral(bindingState))
                    {
                        bindingState.IsDisarmed = true;
                    }
                }
            }
        }
    }

    private void DisarmBindingsForControl(InputControlKey control)
    {
        foreach (var mapState in _maps.Values)
        {
            if (!mapState.BindingsByControl.TryGetValue(control, out var bindings))
            {
                continue;
            }

            foreach (var bindingState in bindings)
            {
                if (!IsBindingNeutral(bindingState))
                {
                    bindingState.IsDisarmed = true;
                }
            }
        }
    }

    private long DiscardPendingInputCopies(
        InputClass inputClasses,
        ushort? playerSlot,
        InputDeviceHandle? physicalDevice = null)
    {
        var retainedInputs = new Queue<InputEnvelope>(_inputInbox.Count);
        long discardedInputCount = 0;

        while (_inputInbox.TryDequeue(out var envelope))
        {
            var matchesDevice = physicalDevice is not { } targetDevice
                || (envelope.PhysicalEvent?.Device is { } sourceDevice && (sourceDevice == targetDevice));

            if (matchesDevice && MatchesInputScope(envelope.Control, inputClasses, playerSlot))
            {
                discardedInputCount = checked(discardedInputCount + envelope.ReplacementCount + 1);
            }
            else
            {
                retainedInputs.Enqueue(envelope);
            }
        }

        _inputInboxTail = null;

        while (retainedInputs.TryDequeue(out var envelope))
        {
            _inputInbox.Enqueue(envelope);
            _inputInboxTail = envelope;
        }

        return discardedInputCount;
    }

    private InputRoutingReceipt EnqueueInputEnvelope(
        InputEnvelope envelope,
        long hostTimestamp,
        long hostSequence,
        long assignedTick,
        bool late)
    {
        EnsureOwnerThread();

        if (_isPaused)
        {
            return CreateInputRejection("Paused", "/Recipient");
        }

        if ((hostSequence <= _lastSeenHostSequence) || (hostTimestamp < _lastSeenHostTimestamp))
        {
            _lastSeenHostSequence = Math.Max(hostSequence, _lastSeenHostSequence);
            return CreateInputRejection("InvalidEventOrder", "/HostSequence");
        }

        _lastSeenHostSequence = hostSequence;
        _lastSeenHostTimestamp = hostTimestamp;

        var effectiveLate = late || (assignedTick < _nextOpenTick);
        var effectiveTick = Math.Max(assignedTick, _nextOpenTick);

        if (_inputOverflowed)
        {
            RecordUnacceptedSequence(hostSequence);

            var inputClass = GetInputClass(envelope.Control);

            if (inputClass != InputClass.None)
            {
                var playerSlot = inputClass == InputClass.Gamepad
                    ? envelope.Control.PlayerSlot
                    : (ushort?)null;
                MarkUnreconciledControls(inputClass, playerSlot);
            }

            return CreateInputCapacityFailure(hostSequence, effectiveTick, effectiveLate);
        }

        if (effectiveTick < _lastAssignedInputTick)
        {
            return CreateInputRejection("InvalidTickOrder", "/AssignedTick");
        }

        if ((_inputInboxTail is { } tail)
            && CanCoalesceInputEnvelope(tail, envelope)
            && tail.TryCoalesceWith(envelope))
        {
            _lastAssignedInputTick = effectiveTick;
            return new InputRoutingReceipt(InputRoutingStatus.Applied, _recipientId, hostSequence, effectiveTick, null, effectiveLate);
        }

        if (_inputInbox.Count >= MaximumPendingInputEvents)
        {
            _inputOverflowed = true;
            RecordUnacceptedSequence(hostSequence);
            return CreateInputCapacityFailure(hostSequence, effectiveTick, effectiveLate);
        }

        _inputInbox.Enqueue(envelope);
        _inputInboxTail = envelope;
        _lastAssignedInputTick = effectiveTick;

        return new InputRoutingReceipt(InputRoutingStatus.Applied, _recipientId, hostSequence, effectiveTick, null, effectiveLate);
    }

    private ImmutableArray<InputControlKey> GetConsumptionControls(InputBindingRuntimeState bindingState)
    {
        var binding = bindingState.Definition;

        if (!binding.Enabled
            || ((binding.Flags & InputBindingBehavior.ConsumePhysicalEvent) == 0)
            || !bindingState.IsContextEligible
            || bindingState.IsDisarmed
            || IsBindingSuppressedByConsumption(bindingState, _consumptionLeases))
        {
            return [];
        }

        var controls = ImmutableArray.CreateBuilder<InputControlKey>();

        switch (binding.Kind)
        {
            case BindingKind.Button:
            {
                if (bindingState.IsDown)
                {
                    controls.Add(bindingState.ControlKeys[0]);
                }

                break;
            }

            case BindingKind.Chord:
            {
                if (!IsBindingNeutral(bindingState))
                {
                    controls.AddRange(bindingState.ControlKeys);
                }

                break;
            }

            case BindingKind.Axis1Keys:
            case BindingKind.Axis2Keys:
            {
                foreach (var control in bindingState.ControlKeys)
                {
                    if (IsControlDown(control))
                    {
                        controls.Add(control);
                    }
                }

                break;
            }

            case BindingKind.GamepadAxis1:
            case BindingKind.GamepadStick2:
            case BindingKind.GamepadTrigger:
            {
                if ((bindingState.AxisX != 0.0f) || (bindingState.AxisY != 0.0f))
                {
                    controls.Add(bindingState.ControlKeys[0]);
                }

                break;
            }

            case BindingKind.AxisThreshold:
            {
                if (bindingState.IsDown)
                {
                    controls.Add(bindingState.ControlKeys[0]);
                }

                break;
            }
        }

        return controls.ToImmutable();
    }

    private int GetEffectiveConsumptionRank(InputControlKey control)
    {
        if (!_consumptionLeases.TryGetValue(control, out var leases))
        {
            return int.MaxValue;
        }

        var rank = int.MaxValue;

        foreach (var lease in leases)
        {
            rank = Math.Min(rank, lease.ContextRank);
        }

        return rank;
    }

    private (ulong ActiveMask, long[] InsertionSequences, long LastInsertionSequence) GetPlannedContextState(InputActionMapState mapState)
    {
        var activeMask = mapState.ActiveContextMask;
        var insertionSequences = mapState.ContextInsertionSequences;
        var lastInsertionSequence = _lastInsertionSequence;

        foreach (var control in _inputControlInbox)
        {
            if (control.Kind != InputControlCommandKind.SetActiveContextMask)
            {
                continue;
            }

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
        _inputInboxTail = null;
        _digitalControlStates.Clear();
        _physicalDigitalSources.Clear();
        _scalarControlValues.Clear();
        _vectorControlValues.Clear();
        MarkUnreconciledControls(InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad, playerSlot: null);
        var transitions = ImmutableArray.CreateBuilder<InputActionTransition>();

        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            InitializeMapState(mapState);

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

        RefreshAllConsumptionLeases();
        _overflowServiced = true;
        return new InputOverflowResult(_firstUnacceptedHostSequence, _lastUnacceptedHostSequence, transitions.ToImmutable());
    }

    private bool HasActiveConsumedBinding(InputActionMapState mapState, ActionDefinition action)
    {
        foreach (var binding in mapState.Map.Bindings)
        {
            if (binding.ActionId != action.ActionId)
            {
                continue;
            }

            var bindingState = mapState.BindingStates[binding.BindingId];

            if (binding.Enabled
                && bindingState.IsContextEligible
                && !IsBindingNeutral(bindingState)
                && IsBindingSuppressedByConsumption(bindingState, _consumptionLeases))
            {
                return true;
            }
        }

        return false;
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
        foreach (var control in bindingState.ControlKeys)
        {
            if (_unreconciledControls.Contains(control))
            {
                return false;
            }
        }

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

    private void MarkUnreconciledControls(InputClass inputClasses, ushort? playerSlot)
    {
        foreach (var mapState in _maps.Values)
        {
            foreach (var bindingState in mapState.BindingStates.Values)
            {
                foreach (var control in bindingState.ControlKeys)
                {
                    if (MatchesInputScope(control, inputClasses, playerSlot))
                    {
                        _ = _unreconciledControls.Add(control);
                    }
                }
            }
        }
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

        ApplyPendingReboundActions(assignedTick);

        if (_inputOverflowed && !_overflowServiced)
        {
            _ = HandleInputOverflow(assignedTick);
        }
        else
        {
            while (true)
            {
                InputEnvelope? envelope = null;
                var inputReady = !_inputOverflowed
                    && _inputInbox.TryPeek(out envelope)
                    && (envelope.AssignedTick <= assignedTick);
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
                    var dequeuedInput = _inputInbox.Dequeue();

                    if (ReferenceEquals(_inputInboxTail, dequeuedInput))
                    {
                        _inputInboxTail = null;
                    }

                    ApplyInputEnvelope(dequeuedInput);
                }
            }
        }

        _nextOpenTick = Math.Max(_nextOpenTick, checked(assignedTick + 1));
        var snapshot = CreateActionSnapshot(assignedTick);
        Volatile.Write(ref _simulationActionSnapshot, snapshot);
        return snapshot;
    }

    private InputRoutingReceipt QueuePhysicalDeviceRemoval(
        InputDeviceHandle device,
        long hostTimestamp,
        long hostSequence,
        out long discardedInputCount)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostSequence, 1);
        discardedInputCount = 0;

        if (!device.IsValid)
        {
            return CreateRoutingRejection("InvalidDevice", "/Device");
        }

        if (_recipientKind != InputRecipientKind.SimulationTimeline)
        {
            return CreateRoutingRejection("InvalidRecipient", "/RecipientKind");
        }

        if (_isPaused)
        {
            return CreateRoutingRejection("Paused", "/Recipient");
        }

        if ((hostSequence <= _lastSeenHostSequence)
            || (hostTimestamp < _hostControlTimestamp)
            || (hostTimestamp < _lastSeenHostTimestamp))
        {
            return CreateRoutingRejection("InvalidEventOrder", "/HostSequence");
        }

        if (!CanAdmitControlCommand() || (_inputControlInbox.Count >= MaximumControlCommandsPerBoundary))
        {
            return CreateRoutingCapacityFailure("/InputControl");
        }

        var hostTick = _nextOpenTick;
        var envelope = new InputControlEnvelope(
            InputControlCommandKind.RemovePhysicalDevice,
            Guid.Empty,
            0,
            0,
            [],
            _lastInsertionSequence,
            InputClass.Keyboard | InputClass.Pointing,
            null,
            InputCancellationReason.DeviceRemoved,
            [],
            hostTimestamp,
            hostSequence,
            hostTick,
            physicalSourceDevice: device);

        discardedInputCount = DiscardPendingInputCopies(
            InputClass.Keyboard | InputClass.Pointing,
            playerSlot: null,
            physicalDevice: device);
        _inputControlInbox.Enqueue(envelope);
        RecordHostControlSequence(hostSequence);
        _hostControlTimestamp = hostTimestamp;
        _lastSeenHostTimestamp = hostTimestamp;
        _controlCommandsInBoundary++;

        return new InputRoutingReceipt(
            InputRoutingStatus.Applied,
            _recipientId,
            hostSequence,
            hostTick,
            null);
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

    private void RecomputeAffectedActions(
        HashSet<InputControlKey> changedControls,
        long hostSequence,
        long assignedTick,
        InputCancellationReason? cancellationReason = null)
    {
        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            foreach (var action in mapState.OrderedActions)
            {
                var isAffected = false;

                foreach (var changedControl in changedControls)
                {
                    if (!mapState.BindingsByControl.TryGetValue(changedControl, out var affectedBindings))
                    {
                        continue;
                    }

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
                        break;
                    }
                }

                if (isAffected)
                {
                    var actionCancellationReason = cancellationReason
                        ?? (mapState.ActionStates[action.ActionId].IsDownAtEnd
                            && HasActiveConsumedBinding(mapState, action)
                            ? InputCancellationReason.InputConsumed
                            : null);
                    ProcessActionEvent(
                        mapState,
                        action,
                        hostSequence,
                        assignedTick,
                        actionCancellationReason,
                        _consumptionLeases);
                }
            }
        }
    }

    private ImmutableArray<InputActionTransition> RecomputeAllActions(
        long hostSequence,
        long? assignedTick,
        InputCancellationReason? cancellationReason)
    {
        var transitions = ImmutableArray.CreateBuilder<InputActionTransition>();

        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            foreach (var action in mapState.OrderedActions)
            {
                var actionState = mapState.ActionStates[action.ActionId];
                var firstTransitionIndex = actionState.Transitions.Count;
                ProcessActionEvent(
                    mapState,
                    action,
                    hostSequence,
                    assignedTick,
                    cancellationReason,
                    _consumptionLeases);
                transitions.AddRange(actionState.Transitions.GetRange(
                    firstTransitionIndex,
                    actionState.Transitions.Count - firstTransitionIndex));
            }
        }

        return transitions.ToImmutable();
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

    private void RefreshAllConsumptionLeases()
    {
        var changedControls = new HashSet<InputControlKey>(_consumptionLeases.Keys);
        _consumptionLeases.Clear();

        foreach (var mapState in _maps.Values)
        {
            foreach (var bindingState in mapState.BindingStates.Values)
            {
                bindingState.ConsumptionControls = [];
            }
        }

        var affectedBindingStates = new HashSet<InputBindingRuntimeState>();

        foreach (var bindingState in _orderedConsumableBindings)
        {
            RefreshConsumptionForBinding(bindingState, changedControls, affectedBindingStates);
        }

        DisarmBindingsForConsumptionChanges(changedControls);
    }

    private void RefreshConsumptionForBinding(
        InputBindingRuntimeState bindingState,
        HashSet<InputControlKey> changedControls,
        HashSet<InputBindingRuntimeState> affectedBindingStates)
    {
        foreach (var control in bindingState.ConsumptionControls)
        {
            var previousRank = GetEffectiveConsumptionRank(control);

            if (_consumptionLeases.TryGetValue(control, out var leases))
            {
                for (var index = leases.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(leases[index].BindingState, bindingState))
                    {
                        leases.RemoveAt(index);
                    }
                }

                if (leases.Count == 0)
                {
                    _ = _consumptionLeases.Remove(control);
                }
            }

            if (previousRank != GetEffectiveConsumptionRank(control))
            {
                AddConsumptionChangedControl(control, changedControls, affectedBindingStates);
            }
        }

        bindingState.ConsumptionControls = GetConsumptionControls(bindingState);

        foreach (var control in bindingState.ConsumptionControls)
        {
            var previousRank = GetEffectiveConsumptionRank(control);

            if (!_consumptionLeases.TryGetValue(control, out var leases))
            {
                leases = [];
                _consumptionLeases.Add(control, leases);
            }

            leases.Add(new InputConsumptionLease(bindingState, bindingState.ContextRank));

            if (previousRank != GetEffectiveConsumptionRank(control))
            {
                AddConsumptionChangedControl(control, changedControls, affectedBindingStates);
            }
        }
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
        }

        RefreshAllConsumptionLeases();

        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            foreach (var action in mapState.OrderedActions)
            {
                var actionState = mapState.ActionStates[action.ActionId];
                var firstTransitionIndex = actionState.Transitions.Count;
                var cancellationReason = actionState.IsDownAtEnd && HasActiveConsumedBinding(mapState, action)
                    ? InputCancellationReason.InputConsumed
                    : InputCancellationReason.ContextMasked;
                ProcessActionEvent(
                    mapState,
                    action,
                    hostSequence,
                    assignedTick,
                    cancellationReason,
                    _consumptionLeases);

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

    private bool SetPhysicalDigitalControlState(
        InputControlKey control,
        bool isDown,
        InputDeviceHandle device,
        long hostTimestamp)
    {
        var wasDown = IsControlDown(control);

        if (isDown)
        {
            if (!_physicalDigitalSources.TryGetValue(control, out var sources))
            {
                sources = [];
                _physicalDigitalSources.Add(control, sources);
            }

            if (!sources.Add(device))
            {
                return false;
            }
        }
        else if (_physicalDigitalSources.TryGetValue(control, out var existingSources))
        {
            if (!existingSources.Remove(device))
            {
                return false;
            }

            if (existingSources.Count == 0)
            {
                _ = _physicalDigitalSources.Remove(control);
            }
        }

        var isDownAfter = _physicalDigitalSources.TryGetValue(control, out var activeSources)
            && (activeSources.Count != 0);

        return (wasDown != isDownAfter)
            && SetDigitalControlState(control, isDownAfter, hostTimestamp);
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

    private HashSet<InputControlKey> UpdateAffectedBindingStates(InputControlKey changedControl, InputEnvelope envelope)
    {
        var changedControls = new HashSet<InputControlKey> { changedControl };
        var affectedBindingStates = new HashSet<InputBindingRuntimeState>();

        foreach (var mapState in _maps.Values.OrderBy(static state => state.Map.MapId.ToString("D"), StringComparer.Ordinal))
        {
            if (!mapState.BindingsByControl.TryGetValue(changedControl, out var affectedBindings))
            {
                continue;
            }

            foreach (var bindingState in affectedBindings)
            {
                UpdateBindingState(bindingState, envelope);
                _ = affectedBindingStates.Add(bindingState);
            }
        }

        foreach (var bindingState in _orderedConsumableBindings)
        {
            if (affectedBindingStates.Contains(bindingState))
            {
                RefreshConsumptionForBinding(bindingState, changedControls, affectedBindingStates);
            }
        }

        DisarmBindingsForConsumptionChanges(changedControls);
        return changedControls;
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

    private void UpdateBindingsForScope(InputClass inputClasses, ushort? playerSlot)
    {
        foreach (var mapState in _maps.Values)
        {
            foreach (var bindingState in mapState.BindingStates.Values)
            {
                if (bindingState.ControlKeys.Any(control => MatchesInputScope(control, inputClasses, playerSlot)))
                {
                    UpdateBindingState(bindingState, envelope: null);
                }
            }
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

    private readonly record struct InputConsumptionLease(InputBindingRuntimeState BindingState, int ContextRank);

    private readonly record struct InputContextOrder(
        Guid MapId,
        string CanonicalMapId,
        ContextDefinition Context,
        long InsertionSequence,
        string CanonicalContextId);

    private enum InputControlCommandKind
    {
        CancelInput,
        ReconcileInput,
        RemovePhysicalDevice,
        SetActiveContextMask,
    }

    private sealed class InputControlEnvelope(
        InputControlCommandKind kind,
        Guid mapId,
        long expectedRevision,
        ulong newMask,
        long[] contextInsertionSequences,
        long lastInsertionSequence,
        InputClass inputClasses,
        ushort? playerSlot,
        InputCancellationReason? cancellationReason,
        ImmutableArray<InputInjection> reconciliationInputs,
        long hostTimestamp,
        long hostSequence,
        long? assignedTick,
        ImmutableArray<PhysicalInputValue> physicalReconciliationInputs = default,
        InputDeviceHandle? physicalSourceDevice = null)
    {
        internal long? AssignedTick { get; } = assignedTick;

        internal InputCancellationReason? CancellationReason { get; } = cancellationReason;

        internal long[] ContextInsertionSequences { get; } = contextInsertionSequences;

        internal long ExpectedRevision { get; } = expectedRevision;

        internal long HostSequence { get; } = hostSequence;

        internal long HostTimestamp { get; } = hostTimestamp;

        internal InputClass InputClasses { get; } = inputClasses;

        internal InputControlCommandKind Kind { get; } = kind;

        internal long LastInsertionSequence { get; } = lastInsertionSequence;

        internal Guid MapId { get; } = mapId;

        internal ulong NewMask { get; } = newMask;

        internal ImmutableArray<PhysicalInputValue> PhysicalReconciliationInputs { get; } = physicalReconciliationInputs;

        internal InputDeviceHandle? PhysicalSourceDevice { get; } = physicalSourceDevice;

        internal ushort? PlayerSlot { get; } = playerSlot;

        internal ImmutableArray<InputInjection> ReconciliationInputs { get; } = reconciliationInputs;
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

    private sealed class InputEnvelope
    {
        private InputInjection? _injection;
        private long _lastHostSequence;
        private long _lastHostTimestamp;
        private long _replacementCount;

        internal InputEnvelope(
            InputInjection injection,
            InputControlKey control,
            long hostTimestamp,
            long hostSequence,
            long assignedTick,
            bool late)
        {
            _injection = injection;
            Control = control;
            FirstHostSequence = hostSequence;
            FirstHostTimestamp = hostTimestamp;
            _lastHostSequence = hostSequence;
            _lastHostTimestamp = hostTimestamp;
            AssignedTick = assignedTick;
            Late = late;
        }

        internal InputEnvelope(
            PhysicalInputEvent physicalEvent,
            InputControlKey control,
            long hostTimestamp,
            long hostSequence,
            long assignedTick,
            bool late)
        {
            PhysicalEvent = physicalEvent;
            Control = control;
            FirstHostSequence = hostSequence;
            FirstHostTimestamp = hostTimestamp;
            _lastHostSequence = hostSequence;
            _lastHostTimestamp = hostTimestamp;
            AssignedTick = assignedTick;
            Late = late;
        }

        internal long AssignedTick { get; }

        internal InputControlKey Control { get; }

        internal long FirstHostSequence { get; }

        internal long FirstHostTimestamp { get; }

        internal long HostSequence => _lastHostSequence;

        internal long HostTimestamp => _lastHostTimestamp;

        internal InputInjection? Injection => _injection;

        internal long LastHostSequence => _lastHostSequence;

        internal long LastHostTimestamp => _lastHostTimestamp;

        internal bool Late { get; }

        internal PhysicalInputEvent? PhysicalEvent { get; }

        internal long ReplacementCount => _replacementCount;

        internal bool CanCoalesceWith(InputEnvelope next)
        {
            return (_injection is { } injection)
                && (next._injection is { } nextInjection)
                && (Control == next.Control)
                && (AssignedTick == next.AssignedTick)
                && (Late == next.Late)
                && (injection.Kind == nextInjection.Kind)
                && (injection.Kind is InputInjectionKind.GamepadAxis
                    or InputInjectionKind.GamepadStick
                    or InputInjectionKind.GamepadTrigger);
        }

        internal bool TryCoalesceWith(InputEnvelope next)
        {
            if (!CanCoalesceWith(next))
            {
                return false;
            }

            var replacementCount = checked(_replacementCount + next.ReplacementCount + 1);
            _injection = next._injection;
            _lastHostSequence = next.LastHostSequence;
            _lastHostTimestamp = next.LastHostTimestamp;
            _replacementCount = replacementCount;
            return true;
        }
    }

    private readonly record struct PendingReboundAction(
        Guid MapId,
        Guid ActionId,
        long MapRevision,
        InputActionKind Kind,
        float AxisX,
        float AxisY,
        ImmutableArray<InputActionTransition> PendingTransitions,
        bool WasDown,
        long HostSequence,
        long? AssignedTick);
}
