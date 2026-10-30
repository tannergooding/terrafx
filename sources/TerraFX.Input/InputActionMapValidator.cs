// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TerraFX.Input;

internal static class InputActionMapValidator
{
    private const int MaximumActions = 256;
    private const int MaximumBindings = 1_024;
    private const int MaximumContexts = 64;

    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    internal static ValidationIssue? Validate(InputActionMapV1 map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var issues = new List<ValidationIssue>();

        if (map.SchemaVersion != 1)
        {
            Add(issues, "UnknownSchemaVersion", "/SchemaVersion");
        }

        if (map.MapId == Guid.Empty)
        {
            Add(issues, "InvalidSchema", "/MapId");
        }

        if (map.Revision <= 0)
        {
            Add(issues, "InvalidSchema", "/Revision");
        }

        var actionsUsable = CheckArray(map.Actions, MaximumActions, "/Actions", issues);
        var contextsUsable = CheckArray(map.Contexts, MaximumContexts, "/Contexts", issues);
        var bindingsUsable = CheckArray(map.Bindings, MaximumBindings, "/Bindings", issues);

        if (contextsUsable && map.Contexts.IsEmpty)
        {
            Add(issues, "InvalidSchema", "/Contexts");
        }

        var actionKinds = new Dictionary<Guid, InputActionKind>();
        var contextBits = ValidateContexts(map.Contexts, contextsUsable, issues);
        ValidateActions(map.Actions, actionsUsable, actionKinds, issues);
        ValidateInitialMask(map.InitialActiveContextMask, contextBits, issues);
        ValidateBindings(map.Bindings, bindingsUsable, actionKinds, contextBits, issues);

        return issues
            .OrderBy(static issue => issue.SchemaPath, StringComparer.Ordinal)
            .ThenBy(static issue => issue.Code, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static void Add(List<ValidationIssue> issues, string code, string schemaPath)
        => issues.Add(new ValidationIssue(code, schemaPath));

    private static void AddKeyboardKey(
        KeyboardControl? control,
        string path,
        HashSet<KeyboardControlKey> keys,
        List<ValidationIssue> issues)
    {
        if (ValidateKeyboardControl(control, path, issues) is { } key && !keys.Add(key))
        {
            Add(issues, "DuplicateControl", path);
        }
    }

    private static bool CheckArray<T>(
        ImmutableArray<T> values,
        int maximumLength,
        string path,
        List<ValidationIssue> issues)
    {
        if (values.IsDefault)
        {
            Add(issues, "InvalidSchema", path);
            return false;
        }

        if (values.Length > maximumLength)
        {
            Add(issues, "CapacityExceeded", path);
            return false;
        }

        return true;
    }

    private static string Escape(string value)
        => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static bool IsCompatible(InputActionKind actionKind, BindingKind bindingKind)
        => actionKind switch {
            InputActionKind.Digital => bindingKind is BindingKind.Button or BindingKind.Chord or BindingKind.AxisThreshold,
            InputActionKind.Axis1 => bindingKind is BindingKind.Axis1Keys or BindingKind.GamepadAxis1 or BindingKind.GamepadTrigger,
            InputActionKind.Axis2 => bindingKind is BindingKind.Axis2Keys or BindingKind.GamepadStick2,
            _ => false,
        };

    private static bool IsDeadzone(float deadzone)
        => float.IsFinite(deadzone) && (deadzone >= 0) && (deadzone < 1);

    private static bool IsThresholdPair(float press, float release)
        => float.IsFinite(press)
        && float.IsFinite(release)
        && (release >= 0)
        && (release < press)
        && (press <= 1);

    private static bool IsValidUtf8(string? value, int minimumBytes, int maximumBytes)
    {
        if (value is null)
        {
            return false;
        }

        try
        {
            var byteCount = s_strictUtf8.GetByteCount(value);
            return (byteCount >= minimumBytes) && (byteCount <= maximumBytes);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static string ItemPath(string collectionName, int index)
        => $"/{Escape(collectionName)}/{index.ToString(CultureInfo.InvariantCulture)}";

    private static void ValidateActionThresholds(ActionDefinition action, string path, List<ValidationIssue> issues)
    {
        if (action.Kind == InputActionKind.Digital)
        {
            if (action.DownPressThreshold is not null)
            {
                Add(issues, "InvalidThreshold", $"{path}/DownPressThreshold");
            }

            if (action.DownReleaseThreshold is not null)
            {
                Add(issues, "InvalidThreshold", $"{path}/DownReleaseThreshold");
            }

            return;
        }

        if (action.DownPressThreshold.HasValue != action.DownReleaseThreshold.HasValue)
        {
            Add(issues, "InvalidThreshold", $"{path}/DownPressThreshold");
            return;
        }

        if (action.DownPressThreshold is { } press
            && action.DownReleaseThreshold is { } release
            && !IsThresholdPair(press, release))
        {
            Add(issues, "InvalidThreshold", $"{path}/DownPressThreshold");
        }
    }

    private static void ValidateActions(
        ImmutableArray<ActionDefinition> actions,
        bool usable,
        Dictionary<Guid, InputActionKind> actionKinds,
        List<ValidationIssue> issues)
    {
        if (!usable)
        {
            return;
        }

        var actionIds = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            var path = ItemPath("Actions", index);

            if (action is null)
            {
                Add(issues, "InvalidSchema", path);
                continue;
            }

            if (action.ActionId == Guid.Empty)
            {
                Add(issues, "InvalidSchema", $"{path}/ActionId");
            }
            else if (!actionIds.Add(action.ActionId))
            {
                Add(issues, "DuplicateId", $"{path}/ActionId");
            }

            if (!IsValidUtf8(action.Name, 1, 128))
            {
                Add(issues, "InvalidStringOrEncoding", $"{path}/Name");
            }
            else if (!names.Add(action.Name))
            {
                Add(issues, "DuplicateName", $"{path}/Name");
            }

            if (!Enum.IsDefined(action.Kind))
            {
                Add(issues, "InvalidEnumOrFlags", $"{path}/Kind");
            }
            else
            {
                _ = actionKinds.TryAdd(action.ActionId, action.Kind);
            }

            ValidateActionThresholds(action, path, issues);
        }
    }

    private static void ValidateAxis1Keys(BindingDefinition binding, string path, List<ValidationIssue> issues)
    {
        var negative = ValidateKeyboardControl(binding.Negative, $"{path}/Negative", issues);
        var positive = ValidateKeyboardControl(binding.Positive, $"{path}/Positive", issues);

        if ((negative is { } negativeKey) && (positive is { } positiveKey) && negativeKey == positiveKey)
        {
            Add(issues, "DuplicateControl", $"{path}/Positive");
        }
    }

    private static void ValidateAxis2Keys(BindingDefinition binding, string path, List<ValidationIssue> issues)
    {
        var keys = new HashSet<KeyboardControlKey>();
        AddKeyboardKey(binding.Left, $"{path}/Left", keys, issues);
        AddKeyboardKey(binding.Right, $"{path}/Right", keys, issues);
        AddKeyboardKey(binding.Down, $"{path}/Down", keys, issues);
        AddKeyboardKey(binding.Up, $"{path}/Up", keys, issues);
    }

    private static void ValidateAxisSource(GamepadAxis1Source? source, string path, List<ValidationIssue> issues)
    {
        if (source is null)
        {
            Add(issues, "InvalidSchema", path);
            return;
        }

        if (!Enum.IsDefined(source.Axis))
        {
            Add(issues, "InvalidEnumOrFlags", $"{path}/Axis");
        }

        if (!IsDeadzone(source.Deadzone))
        {
            Add(issues, "InvalidProcessor", $"{path}/Deadzone");
        }
    }

    private static void ValidateAxisThreshold(BindingDefinition binding, string path, List<ValidationIssue> issues)
    {
        var axisSource = binding.ThresholdAxisSource;
        var triggerSource = binding.ThresholdTriggerSource;

        if ((axisSource is null) == (triggerSource is null))
        {
            Add(issues, "InvalidSchema", $"{path}/ThresholdAxisSource");
        }
        else if (axisSource is not null)
        {
            ValidateAxisSource(axisSource, $"{path}/ThresholdAxisSource", issues);
        }
        else
        {
            ValidateTriggerSource(triggerSource, $"{path}/ThresholdTriggerSource", issues);
        }

        if (binding.ThresholdDirection is not { } direction || !Enum.IsDefined(direction))
        {
            Add(issues, "InvalidEnumOrFlags", $"{path}/ThresholdDirection");
        }
        else if ((triggerSource is not null) && (direction == AxisThresholdDirection.Negative))
        {
            Add(issues, "InvalidProcessor", $"{path}/ThresholdDirection");
        }

        var press = binding.PressThreshold ?? 0.55f;
        var release = binding.ReleaseThreshold ?? 0.45f;

        if (binding.PressThreshold.HasValue != binding.ReleaseThreshold.HasValue)
        {
            Add(issues, "InvalidThreshold", $"{path}/PressThreshold");
        }
        else if (!IsThresholdPair(press, release))
        {
            Add(issues, "InvalidThreshold", $"{path}/PressThreshold");
        }
    }

    private static void ValidateBindingSource(BindingDefinition binding, string path, List<ValidationIssue> issues)
    {
        ValidateUnusedFields(binding, path, issues);

        switch (binding.Kind)
        {
            case BindingKind.Button:
            {
                if (binding.Control is null)
                {
                    Add(issues, "InvalidSchema", $"{path}/Control");
                }
                else
                {
                    _ = ValidateDigitalControl(binding.Control, $"{path}/Control", issues);
                }

                break;
            }

            case BindingKind.Chord:
            {
                ValidateChord(binding, path, issues);
                break;
            }

            case BindingKind.Axis1Keys:
            {
                ValidateAxis1Keys(binding, path, issues);
                break;
            }

            case BindingKind.Axis2Keys:
            {
                ValidateAxis2Keys(binding, path, issues);
                break;
            }

            case BindingKind.GamepadAxis1:
            {
                ValidateAxisSource(binding.Axis1Source, $"{path}/Axis1Source", issues);
                break;
            }

            case BindingKind.GamepadStick2:
            {
                ValidateStickSource(binding.Stick2Source, $"{path}/Stick2Source", issues);
                break;
            }

            case BindingKind.GamepadTrigger:
            {
                ValidateTriggerSource(binding.TriggerSource, $"{path}/TriggerSource", issues);
                break;
            }

            case BindingKind.AxisThreshold:
            {
                ValidateAxisThreshold(binding, path, issues);
                break;
            }
        }
    }

    private static void ValidateBindings(
        ImmutableArray<BindingDefinition> bindings,
        bool usable,
        Dictionary<Guid, InputActionKind> actionKinds,
        ulong declaredContextMask,
        List<ValidationIssue> issues)
    {
        if (!usable)
        {
            return;
        }

        var bindingIds = new HashSet<Guid>();

        for (var index = 0; index < bindings.Length; index++)
        {
            var binding = bindings[index];
            var path = ItemPath("Bindings", index);

            if (binding is null)
            {
                Add(issues, "InvalidSchema", path);
                continue;
            }

            if (binding.BindingId == Guid.Empty)
            {
                Add(issues, "InvalidSchema", $"{path}/BindingId");
            }
            else if (!bindingIds.Add(binding.BindingId))
            {
                Add(issues, "DuplicateId", $"{path}/BindingId");
            }

            if (binding.ActionId == Guid.Empty)
            {
                Add(issues, "InvalidSchema", $"{path}/ActionId");
            }
            else if (!actionKinds.TryGetValue(binding.ActionId, out var actionKind))
            {
                Add(issues, "UnknownReference", $"{path}/ActionId");
            }
            else if (!IsCompatible(actionKind, binding.Kind))
            {
                Add(issues, "IncompatibleKind", $"{path}/Kind");
            }

            if ((binding.ContextMask == 0) || ((binding.ContextMask & ~declaredContextMask) != 0))
            {
                Add(issues, "InvalidContextMask", $"{path}/ContextMask");
            }

            if (((int)binding.Flags & ~(int)InputBindingBehavior.ConsumePhysicalEvent) != 0)
            {
                Add(issues, "InvalidEnumOrFlags", $"{path}/Flags");
            }

            if (binding.Controls.IsDefault)
            {
                Add(issues, "InvalidSchema", $"{path}/Controls");
            }

            if (!Enum.IsDefined(binding.Kind))
            {
                Add(issues, "InvalidEnumOrFlags", $"{path}/Kind");
                continue;
            }

            ValidateBindingSource(binding, path, issues);
        }
    }

    private static void ValidateChord(BindingDefinition binding, string path, List<ValidationIssue> issues)
    {
        var controls = binding.Controls;

        if (binding.CompletionWindowMilliseconds is { } window && window is < 1 or > 60_000)
        {
            Add(issues, "InvalidProcessor", $"{path}/CompletionWindowMilliseconds");
        }

        if (controls.IsDefault || controls.Length is < 2 or > 4)
        {
            Add(issues, "InvalidSchema", $"{path}/Controls");
            return;
        }

        var keys = new HashSet<DigitalControlKey>();

        for (var index = 0; index < controls.Length; index++)
        {
            var control = controls[index];
            var controlPath = $"{path}/Controls/{index.ToString(CultureInfo.InvariantCulture)}";

            if (control is null)
            {
                Add(issues, "InvalidSchema", controlPath);
                continue;
            }

            if (ValidateDigitalControl(control, controlPath, issues) is { } key && !keys.Add(key))
            {
                Add(issues, "DuplicateControl", controlPath);
            }
        }
    }

    private static ulong ValidateContexts(
        ImmutableArray<ContextDefinition> contexts,
        bool usable,
        List<ValidationIssue> issues)
    {
        if (!usable)
        {
            return 0;
        }

        var contextIds = new HashSet<Guid>();
        var bits = new HashSet<byte>();
        var declaredMask = 0UL;

        for (var index = 0; index < contexts.Length; index++)
        {
            var context = contexts[index];
            var path = ItemPath("Contexts", index);

            if (context is null)
            {
                Add(issues, "InvalidSchema", path);
                continue;
            }

            if (context.ContextId == Guid.Empty)
            {
                Add(issues, "InvalidSchema", $"{path}/ContextId");
            }
            else if (!contextIds.Add(context.ContextId))
            {
                Add(issues, "DuplicateId", $"{path}/ContextId");
            }

            if (context.Bit > 63)
            {
                Add(issues, "InvalidSchema", $"{path}/Bit");
            }
            else
            {
                if (!bits.Add(context.Bit))
                {
                    Add(issues, "InvalidSchema", $"{path}/Bit");
                }

                declaredMask |= 1UL << context.Bit;
            }

            if (((int)context.CaptureClasses & ~(int)(InputClass.Keyboard | InputClass.Pointing | InputClass.Gamepad)) != 0)
            {
                Add(issues, "InvalidEnumOrFlags", $"{path}/CaptureClasses");
            }
        }

        return declaredMask;
    }

    private static DigitalControlKey? ValidateDigitalControl(DigitalControl control, string path, List<ValidationIssue> issues)
    {
        switch (control.Kind)
        {
            case DigitalControlKind.KeyboardHid:
            case DigitalControlKind.LogicalKey:
            {
                if ((control.Keyboard is null)
                    || (control.MouseButton is not null)
                    || (control.PlayerSlot is not null)
                    || (control.GamepadButton is not null))
                {
                    Add(issues, "InvalidSchema", path);
                    return null;
                }

                var requiredKeyboardKind = control.Kind == DigitalControlKind.KeyboardHid
                    ? KeyboardControlKind.KeyboardHid
                    : KeyboardControlKind.LogicalKey;

                if (control.Keyboard.Kind != requiredKeyboardKind)
                {
                    Add(issues, "InvalidSchema", path);
                    return null;
                }

                return ValidateKeyboardControl(control.Keyboard, path, issues) is { } keyboardKey
                    ? new DigitalControlKey(control.Kind, keyboardKey, null, null, null)
                    : null;
            }

            case DigitalControlKind.MouseButton:
            {
                if ((control.Keyboard is not null)
                    || (control.MouseButton is not { } mouseButton)
                    || (control.PlayerSlot is not null)
                    || (control.GamepadButton is not null))
                {
                    Add(issues, "InvalidSchema", path);
                    return null;
                }

                if (!Enum.IsDefined(mouseButton))
                {
                    Add(issues, "InvalidEnumOrFlags", path);
                    return null;
                }

                return new DigitalControlKey(control.Kind, null, mouseButton, null, null);
            }

            case DigitalControlKind.GamepadButton:
            {
                if ((control.Keyboard is not null)
                    || (control.MouseButton is not null)
                    || (control.PlayerSlot is not { } playerSlot)
                    || (control.GamepadButton is not { } gamepadButton))
                {
                    Add(issues, "InvalidSchema", path);
                    return null;
                }

                if (!Enum.IsDefined(gamepadButton))
                {
                    Add(issues, "InvalidEnumOrFlags", path);
                    return null;
                }

                return new DigitalControlKey(control.Kind, null, null, playerSlot, gamepadButton);
            }

            default:
            {
                Add(issues, "InvalidEnumOrFlags", path);
                return null;
            }
        }
    }

    private static void ValidateInitialMask(ulong mask, ulong declaredMask, List<ValidationIssue> issues)
    {
        if ((mask & ~declaredMask) != 0)
        {
            Add(issues, "InvalidContextMask", "/InitialActiveContextMask");
        }
    }

    private static KeyboardControlKey? ValidateKeyboardControl(KeyboardControl? control, string path, List<ValidationIssue> issues)
    {
        if (control is null)
        {
            Add(issues, "InvalidSchema", path);
            return null;
        }

        switch (control.Kind)
        {
            case KeyboardControlKind.KeyboardHid:
            {
                if (control.LogicalKeyName is not null)
                {
                    Add(issues, "InvalidSchema", path);
                    return null;
                }

                return new KeyboardControlKey(control.Kind, control.UsagePage, control.Usage, null);
            }

            case KeyboardControlKind.LogicalKey:
            {
                if ((control.UsagePage != 0)
                    || (control.Usage != 0)
                    || !IsValidUtf8(control.LogicalKeyName, 1, 64))
                {
                    Add(issues, "InvalidStringOrEncoding", path);
                    return null;
                }

                return new KeyboardControlKey(control.Kind, 0, 0, control.LogicalKeyName);
            }

            default:
            {
                Add(issues, "InvalidEnumOrFlags", path);
                return null;
            }
        }
    }

    private static void ValidateStickSource(GamepadStick2Source? source, string path, List<ValidationIssue> issues)
    {
        if (source is null)
        {
            Add(issues, "InvalidSchema", path);
            return;
        }

        if (!Enum.IsDefined(source.Stick))
        {
            Add(issues, "InvalidEnumOrFlags", $"{path}/Stick");
        }

        if (!IsDeadzone(source.RadialDeadzone))
        {
            Add(issues, "InvalidProcessor", $"{path}/RadialDeadzone");
        }
    }

    private static void ValidateTriggerSource(GamepadTriggerSource? source, string path, List<ValidationIssue> issues)
    {
        if (source is null)
        {
            Add(issues, "InvalidSchema", path);
            return;
        }

        if (!Enum.IsDefined(source.Trigger))
        {
            Add(issues, "InvalidEnumOrFlags", $"{path}/Trigger");
        }

        if (!IsDeadzone(source.Deadzone))
        {
            Add(issues, "InvalidProcessor", $"{path}/Deadzone");
        }
    }

    private static void ValidateUnusedFields(BindingDefinition binding, string path, List<ValidationIssue> issues)
    {
        var kind = binding.Kind;

        Unexpected(kind != BindingKind.Button && binding.Control is not null, "Control");
        Unexpected(kind != BindingKind.Chord && (!binding.Controls.IsDefaultOrEmpty), "Controls");
        Unexpected(kind != BindingKind.Chord && binding.CompletionWindowMilliseconds is not null, "CompletionWindowMilliseconds");
        Unexpected(kind != BindingKind.Axis1Keys && binding.Negative is not null, "Negative");
        Unexpected(kind != BindingKind.Axis1Keys && binding.Positive is not null, "Positive");
        Unexpected(kind != BindingKind.Axis2Keys && binding.Left is not null, "Left");
        Unexpected(kind != BindingKind.Axis2Keys && binding.Right is not null, "Right");
        Unexpected(kind != BindingKind.Axis2Keys && binding.Down is not null, "Down");
        Unexpected(kind != BindingKind.Axis2Keys && binding.Up is not null, "Up");
        Unexpected(kind != BindingKind.GamepadAxis1 && binding.Axis1Source is not null, "Axis1Source");
        Unexpected(kind != BindingKind.GamepadStick2 && binding.Stick2Source is not null, "Stick2Source");
        Unexpected(kind != BindingKind.GamepadTrigger && binding.TriggerSource is not null, "TriggerSource");
        Unexpected(kind != BindingKind.AxisThreshold && binding.ThresholdAxisSource is not null, "ThresholdAxisSource");
        Unexpected(kind != BindingKind.AxisThreshold && binding.ThresholdTriggerSource is not null, "ThresholdTriggerSource");
        Unexpected(kind != BindingKind.AxisThreshold && binding.ThresholdDirection is not null, "ThresholdDirection");
        Unexpected(kind != BindingKind.AxisThreshold && binding.PressThreshold is not null, "PressThreshold");
        Unexpected(kind != BindingKind.AxisThreshold && binding.ReleaseThreshold is not null, "ReleaseThreshold");

        void Unexpected(bool condition, string propertyName)
        {
            if (condition)
            {
                Add(issues, "InvalidSchema", $"{path}/{propertyName}");
            }
        }
    }

    private readonly record struct DigitalControlKey(
        DigitalControlKind Kind,
        KeyboardControlKey? Keyboard,
        MouseButtonValue? MouseButton,
        ushort? PlayerSlot,
        GamepadButtonValue? GamepadButton);

    private readonly record struct KeyboardControlKey(
        KeyboardControlKind Kind,
        ushort UsagePage,
        ushort Usage,
        string? LogicalKeyName);
}
