// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using TerraFX;

namespace TerraFX.Input;

internal static partial class InputActionMapCodec
{
    internal static EbinReadStatus TryDecode(
        scoped ReadOnlySpan<byte> source,
        out InputActionMapV1? map,
        out int bytesConsumed,
        out EbinReadDiagnostic? diagnostic,
        out ValidationIssue? issue)
    {
        var action = default(DecodeAction);
        var status = EbinStructuralReader.Read(
            source,
            s_readLimits,
            EbinReadPolicy.Canonical,
            ref action,
            out bytesConsumed,
            out diagnostic);

        map = action.Map;
        issue = action.Issue;

        if (status != EbinReadStatus.Complete)
        {
            map = null;
            return status;
        }

        if (bytesConsumed != source.Length)
        {
            map = null;
            issue = new ValidationIssue("InvalidSchema", "/");
            return status;
        }

        if ((map is not null) && (issue is null))
        {
            issue = InputActionMapValidator.Validate(map);

            if (issue is not null)
            {
                map = null;
            }
        }

        return status;
    }

    private static string ItemPath(string collectionName, int index)
        => $"/{collectionName}/{index.ToString(CultureInfo.InvariantCulture)}";

    private static bool TryReadAction(
        EbinRecordReadView record,
        int index,
        out ActionDefinition? action,
        out ValidationIssue? issue)
    {
        var path = ItemPath("Actions", index);
        action = null;

        if (!TryValidateRecordSchema(record, "input/action", 3, 5, path, out issue)
            || !TryReadGuid(record, 1, $"{path}/ActionId", out var actionId, out issue)
            || !TryReadUtf8(record, 2, 128, $"{path}/Name", out var name, out issue)
            || !TryReadActionKind(record, 3, $"{path}/Kind", out var kind, out issue)
            || !TryReadOptionalSingle(record, 4, $"{path}/DownPressThreshold", out var pressThreshold, out issue)
            || !TryReadOptionalSingle(record, 5, $"{path}/DownReleaseThreshold", out var releaseThreshold, out issue))
        {
            return false;
        }

        action = new ActionDefinition(actionId, name, kind, pressThreshold, releaseThreshold);
        return true;
    }

    private static bool TryReadActionKind(
        EbinRecordReadView record,
        uint tag,
        string path,
        out InputActionKind kind,
        out ValidationIssue? issue)
    {
        kind = default;

        if (!TryReadByte(record, tag, path, out var wireValue, out issue))
        {
            return false;
        }

        switch (wireValue)
        {
            case 1:
            {
                kind = InputActionKind.Digital;
                return true;
            }

            case 2:
            {
                kind = InputActionKind.Axis1;
                return true;
            }

            case 3:
            {
                kind = InputActionKind.Axis2;
                return true;
            }

            default:
            {
                issue = new ValidationIssue("InvalidEnumOrFlags", path);
                return false;
            }
        }
    }

    private static bool TryReadActions(
        EbinRecordReadView record,
        out ImmutableArray<ActionDefinition> actions,
        out ValidationIssue? issue)
    {
        actions = [];

        if (!TryReadRecordArrayCount(record, 5, 256, "/Actions", out var count, out issue))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<ActionDefinition>(count);

        for (var index = 0; index < count; index++)
        {
            if (!TryReadAction(record.GetRecordArrayElement(5, index), index, out var action, out issue))
            {
                return false;
            }

            builder.Add(action!);
        }

        actions = builder.MoveToImmutable();
        return true;
    }

    private static bool TryReadAxis1Keys(
        EbinRecordReadView record,
        string path,
        out BindingSourceData data,
        out ValidationIssue? issue)
    {
        data = new BindingSourceData();

        if (!TryValidateRecordSchema(record, "input/axis1keys", 2, 2, path, out issue)
            || !TryReadRecordField(record, 1, $"{path}/Negative", out var negativeRecord, out issue)
            || !TryReadKeyboardControl(negativeRecord, $"{path}/Negative", out var negative, out issue)
            || !TryReadRecordField(record, 2, $"{path}/Positive", out var positiveRecord, out issue)
            || !TryReadKeyboardControl(positiveRecord, $"{path}/Positive", out var positive, out issue))
        {
            return false;
        }

        data.Negative = negative;
        data.Positive = positive;
        return true;
    }

    private static bool TryReadAxis1Source(
        EbinRecordReadView record,
        string path,
        out GamepadAxis1Source? source,
        out ValidationIssue? issue)
    {
        source = null;

        if (!TryValidateRecordSchema(record, "input/gamepadaxis1", 4, 4, path, out issue)
            || !TryReadUInt16(record, 1, $"{path}/PlayerSlot", out var playerSlot, out issue)
            || !TryReadByte(record, 2, $"{path}/Axis", out var axis, out issue)
            || !TryReadBoolean(record, 3, $"{path}/Invert", out var invert, out issue)
            || !TryReadSingle(record, 4, $"{path}/Deadzone", out var deadzone, out issue))
        {
            return false;
        }

        source = new GamepadAxis1Source(playerSlot, (GamepadAxis)axis, invert, deadzone);
        return true;
    }

    private static bool TryReadAxis2Keys(
        EbinRecordReadView record,
        string path,
        out BindingSourceData data,
        out ValidationIssue? issue)
    {
        data = new BindingSourceData();

        if (!TryValidateRecordSchema(record, "input/axis2keys", 4, 4, path, out issue)
            || !TryReadRecordField(record, 1, $"{path}/Left", out var leftRecord, out issue)
            || !TryReadKeyboardControl(leftRecord, $"{path}/Left", out var left, out issue)
            || !TryReadRecordField(record, 2, $"{path}/Right", out var rightRecord, out issue)
            || !TryReadKeyboardControl(rightRecord, $"{path}/Right", out var right, out issue)
            || !TryReadRecordField(record, 3, $"{path}/Down", out var downRecord, out issue)
            || !TryReadKeyboardControl(downRecord, $"{path}/Down", out var down, out issue)
            || !TryReadRecordField(record, 4, $"{path}/Up", out var upRecord, out issue)
            || !TryReadKeyboardControl(upRecord, $"{path}/Up", out var up, out issue))
        {
            return false;
        }

        data.Left = left;
        data.Right = right;
        data.Down = down;
        data.Up = up;
        return true;
    }

    private static bool TryReadAxisThreshold(
        EbinRecordReadView record,
        string path,
        out BindingSourceData data,
        out ValidationIssue? issue)
    {
        data = new BindingSourceData();

        if (!TryValidateRecordSchema(record, "input/axisthreshold", 4, 4, path, out issue)
            || !TryReadRecordField(record, 1, $"{path}/Source", out var sourceRecord, out issue))
        {
            return false;
        }

        var sourceSchemaId = sourceRecord.Header.SchemaId;

        if (sourceSchemaId == s_schemaIds["input/gamepadaxis1"])
        {
            if (!TryReadAxis1Source(sourceRecord, $"{path}/Source", out var axisSource, out issue))
            {
                return false;
            }

            data.ThresholdAxisSource = axisSource;
        }
        else if (sourceSchemaId == s_schemaIds["input/gamepadtrigger"])
        {
            if (!TryReadTriggerSource(sourceRecord, $"{path}/Source", out var triggerSource, out issue))
            {
                return false;
            }

            data.ThresholdTriggerSource = triggerSource;
        }
        else
        {
            issue = new ValidationIssue("InvalidSchema", $"{path}/Source");
            return false;
        }

        if (!TryReadByte(record, 2, $"{path}/Direction", out var direction, out issue)
            || !TryReadSingle(record, 3, $"{path}/PressThreshold", out var pressThreshold, out issue)
            || !TryReadSingle(record, 4, $"{path}/ReleaseThreshold", out var releaseThreshold, out issue))
        {
            return false;
        }

        data.ThresholdDirection = (AxisThresholdDirection)direction;
        data.PressThreshold = pressThreshold;
        data.ReleaseThreshold = releaseThreshold;
        return true;
    }

    private static bool TryReadBinding(
        EbinRecordReadView record,
        int index,
        out BindingDefinition? binding,
        out ValidationIssue? issue)
    {
        var path = ItemPath("Bindings", index);
        binding = null;

        if (!TryValidateRecordSchema(record, "input/binding", 8, 8, path, out issue)
            || !TryReadGuid(record, 1, $"{path}/BindingId", out var bindingId, out issue)
            || !TryReadGuid(record, 2, $"{path}/ActionId", out var actionId, out issue)
            || !TryReadUInt64(record, 3, $"{path}/ContextMask", out var contextMask, out issue)
            || !TryReadInt32(record, 4, $"{path}/Priority", out var priority, out issue)
            || !TryReadBindingKind(record, 5, $"{path}/Kind", out var kind, out issue)
            || !TryReadBoolean(record, 6, $"{path}/Enabled", out var enabled, out issue)
            || !TryReadByte(record, 7, $"{path}/Flags", out var flags, out issue)
            || !TryReadRecordField(record, 8, $"{path}/Source", out var sourceRecord, out issue)
            || !TryReadBindingSource(sourceRecord, kind, $"{path}/Source", out var source, out issue))
        {
            return false;
        }

        binding = new BindingDefinition(bindingId, actionId, contextMask, kind) {
            Priority = priority,
            Enabled = enabled,
            Flags = (InputBindingBehavior)flags,
            Control = source.Control,
            Controls = source.Controls,
            CompletionWindowMilliseconds = source.CompletionWindowMilliseconds,
            Negative = source.Negative,
            Positive = source.Positive,
            Left = source.Left,
            Right = source.Right,
            Down = source.Down,
            Up = source.Up,
            Axis1Source = source.Axis1Source,
            Stick2Source = source.Stick2Source,
            TriggerSource = source.TriggerSource,
            ThresholdAxisSource = source.ThresholdAxisSource,
            ThresholdTriggerSource = source.ThresholdTriggerSource,
            ThresholdDirection = source.ThresholdDirection,
            PressThreshold = source.PressThreshold,
            ReleaseThreshold = source.ReleaseThreshold,
        };
        return true;
    }

    private static bool TryReadBindingKind(
        EbinRecordReadView record,
        uint tag,
        string path,
        out BindingKind kind,
        out ValidationIssue? issue)
    {
        kind = default;

        if (!TryReadByte(record, tag, path, out var wireValue, out issue))
        {
            return false;
        }

        if (wireValue is < 1 or > 8)
        {
            issue = new ValidationIssue("InvalidEnumOrFlags", path);
            return false;
        }

        kind = (BindingKind)(wireValue - 1);
        return true;
    }

    private static bool TryReadBindingSource(
        EbinRecordReadView record,
        BindingKind kind,
        string path,
        out BindingSourceData data,
        out ValidationIssue? issue)
    {
        switch (kind)
        {
            case BindingKind.Button:
            {
                data = new BindingSourceData();

                if (!TryValidateRecordSchema(record, "input/button", 1, 1, path, out issue)
                    || !TryReadRecordField(record, 1, $"{path}/Control", out var controlRecord, out issue)
                    || !TryReadDigitalControl(controlRecord, $"{path}/Control", out var control, out issue))
                {
                    return false;
                }

                data.Control = control;
                return true;
            }

            case BindingKind.Chord:
            {
                return TryReadChord(record, path, out data, out issue);
            }

            case BindingKind.Axis1Keys:
            {
                return TryReadAxis1Keys(record, path, out data, out issue);
            }

            case BindingKind.Axis2Keys:
            {
                return TryReadAxis2Keys(record, path, out data, out issue);
            }

            case BindingKind.GamepadAxis1:
            {
                data = new BindingSourceData();

                if (!TryReadAxis1Source(record, path, out var axisSource, out issue))
                {
                    return false;
                }

                data.Axis1Source = axisSource;
                return true;
            }

            case BindingKind.GamepadStick2:
            {
                data = new BindingSourceData();

                if (!TryReadStickSource(record, path, out var stickSource, out issue))
                {
                    return false;
                }

                data.Stick2Source = stickSource;
                return true;
            }

            case BindingKind.GamepadTrigger:
            {
                data = new BindingSourceData();

                if (!TryReadTriggerSource(record, path, out var triggerSource, out issue))
                {
                    return false;
                }

                data.TriggerSource = triggerSource;
                return true;
            }

            case BindingKind.AxisThreshold:
            {
                return TryReadAxisThreshold(record, path, out data, out issue);
            }

            default:
            {
                data = new BindingSourceData();
                issue = new ValidationIssue("InvalidEnumOrFlags", $"{path}/Kind");
                return false;
            }
        }
    }

    private static bool TryReadBindings(
        EbinRecordReadView record,
        out ImmutableArray<BindingDefinition> bindings,
        out ValidationIssue? issue)
    {
        bindings = [];

        if (!TryReadRecordArrayCount(record, 7, 1_024, "/Bindings", out var count, out issue))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<BindingDefinition>(count);

        for (var index = 0; index < count; index++)
        {
            if (!TryReadBinding(record.GetRecordArrayElement(7, index), index, out var binding, out issue))
            {
                return false;
            }

            builder.Add(binding!);
        }

        bindings = builder.MoveToImmutable();
        return true;
    }

    private static bool TryReadBoolean(
        EbinRecordReadView record,
        uint tag,
        string path,
        out bool value,
        out ValidationIssue? issue)
    {
        value = false;

        if (!TryReadField(record, tag, BooleanKind, 1, 1, path, out var field, out issue))
        {
            return false;
        }

        value = field.Payload[0] != 0;
        return true;
    }

    private static bool TryReadByte(
        EbinRecordReadView record,
        uint tag,
        string path,
        out byte value,
        out ValidationIssue? issue)
    {
        value = 0;

        if (!TryReadField(record, tag, UInt8Kind, 1, 1, path, out var field, out issue))
        {
            return false;
        }

        value = field.Payload[0];
        return true;
    }

    private static bool TryReadChord(
        EbinRecordReadView record,
        string path,
        out BindingSourceData data,
        out ValidationIssue? issue)
    {
        data = new BindingSourceData();

        if (!TryValidateRecordSchema(record, "input/chord", 1, 2, path, out issue)
            || !TryReadRecordArrayCount(record, 1, 4, $"{path}/Controls", out var count, out issue))
        {
            return false;
        }

        var controls = ImmutableArray.CreateBuilder<DigitalControl>(count);

        for (var index = 0; index < count; index++)
        {
            var controlPath = $"{path}/Controls/{index.ToString(CultureInfo.InvariantCulture)}";

            if (!TryReadDigitalControl(record.GetRecordArrayElement(1, index), controlPath, out var control, out issue))
            {
                return false;
            }

            controls.Add(control!);
        }

        int? completionWindow = null;

        if (record.TryGetField(2, out _))
        {
            if (!TryReadUInt32(record, 2, $"{path}/CompletionWindowMilliseconds", out var window, out issue)
                || (window > int.MaxValue))
            {
                issue ??= new ValidationIssue("InvalidProcessor", $"{path}/CompletionWindowMilliseconds");
                return false;
            }

            completionWindow = checked((int)window);
        }

        data.Controls = controls.MoveToImmutable();
        data.CompletionWindowMilliseconds = completionWindow;
        return true;
    }

    private static bool TryReadContext(
        EbinRecordReadView record,
        int index,
        out ContextDefinition? context,
        out ValidationIssue? issue)
    {
        var path = ItemPath("Contexts", index);
        context = null;

        if (!TryValidateRecordSchema(record, "input/context", 4, 4, path, out issue)
            || !TryReadGuid(record, 1, $"{path}/ContextId", out var contextId, out issue)
            || !TryReadByte(record, 2, $"{path}/Bit", out var bit, out issue)
            || !TryReadInt32(record, 3, $"{path}/Priority", out var priority, out issue)
            || !TryReadByte(record, 4, $"{path}/CaptureClasses", out var captureClasses, out issue))
        {
            return false;
        }

        context = new ContextDefinition(contextId, bit, priority, (InputClass)captureClasses);
        return true;
    }

    private static bool TryReadContexts(
        EbinRecordReadView record,
        out ImmutableArray<ContextDefinition> contexts,
        out ValidationIssue? issue)
    {
        contexts = [];

        if (!TryReadRecordArrayCount(record, 6, 64, "/Contexts", out var count, out issue))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<ContextDefinition>(count);

        for (var index = 0; index < count; index++)
        {
            if (!TryReadContext(record.GetRecordArrayElement(6, index), index, out var context, out issue))
            {
                return false;
            }

            builder.Add(context!);
        }

        contexts = builder.MoveToImmutable();
        return true;
    }

    private static bool TryReadDigitalControl(
        EbinRecordReadView record,
        string path,
        out DigitalControl? control,
        out ValidationIssue? issue)
    {
        var schemaId = record.Header.SchemaId;

        if (schemaId == s_schemaIds["input/keyboardhid"])
        {
            if (!TryReadKeyboardControl(record, path, out var keyboard, out issue))
            {
                control = null;
                return false;
            }

            control = new DigitalControl(keyboard!);
            return true;
        }

        if (schemaId == s_schemaIds["input/logicalkey"])
        {
            if (!TryReadKeyboardControl(record, path, out var keyboard, out issue))
            {
                control = null;
                return false;
            }

            control = new DigitalControl(keyboard!);
            return true;
        }

        if (schemaId == s_schemaIds["input/mousebutton"])
        {
            return TryReadMouseButton(record, path, out control, out issue);
        }

        if (schemaId == s_schemaIds["input/gamepadbutton"])
        {
            return TryReadGamepadButton(record, path, out control, out issue);
        }

        control = null;
        issue = new ValidationIssue("InvalidSchema", path);
        return false;
    }

    private static bool TryReadField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong count,
        int byteLength,
        string path,
        out EbinFieldReadView field,
        out ValidationIssue? issue)
    {
        if (!record.TryGetField(tag, out field)
            || (field.Kind != kind)
            || (field.Count != count)
            || (field.Payload.Length != byteLength))
        {
            issue = new ValidationIssue("InvalidSchema", path);
            field = default;
            return false;
        }

        issue = null;
        return true;
    }

    private static bool TryReadGamepadButton(
        EbinRecordReadView record,
        string path,
        out DigitalControl? control,
        out ValidationIssue? issue)
    {
        if (!TryValidateRecordSchema(record, "input/gamepadbutton", 2, 2, path, out issue)
            || !TryReadUInt16(record, 1, $"{path}/PlayerSlot", out var playerSlot, out issue)
            || !TryReadByte(record, 2, $"{path}/Button", out var button, out issue))
        {
            control = null;
            return false;
        }

        control = new DigitalControl(playerSlot, (GamepadButtonValue)button);
        return true;
    }

    private static bool TryReadGuid(
        EbinRecordReadView record,
        uint tag,
        string path,
        out Guid value,
        out ValidationIssue? issue)
    {
        value = default;

        if (!TryReadField(record, tag, GuidKind, 1, 16, path, out var field, out issue))
        {
            return false;
        }

        value = new Guid(field.Payload, bigEndian: true);
        return true;
    }

    private static bool TryReadInt32(
        EbinRecordReadView record,
        uint tag,
        string path,
        out int value,
        out ValidationIssue? issue)
    {
        value = 0;

        if (!TryReadField(record, tag, Int32Kind, 1, 4, path, out var field, out issue))
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(field.Payload);
        return true;
    }

    private static bool TryReadKeyboardControl(
        EbinRecordReadView record,
        string path,
        out KeyboardControl? control,
        out ValidationIssue? issue)
    {
        var schemaId = record.Header.SchemaId;

        if (schemaId == s_schemaIds["input/keyboardhid"])
        {
            if (!TryValidateRecordSchema(record, "input/keyboardhid", 2, 2, path, out issue)
                || !TryReadUInt16(record, 1, $"{path}/UsagePage", out var usagePage, out issue)
                || !TryReadUInt16(record, 2, $"{path}/Usage", out var usage, out issue))
            {
                control = null;
                return false;
            }

            control = new KeyboardControl(usagePage, usage);
            return true;
        }

        if (schemaId == s_schemaIds["input/logicalkey"])
        {
            if (!TryValidateRecordSchema(record, "input/logicalkey", 1, 1, path, out issue)
                || !TryReadUtf8(record, 1, 64, $"{path}/Name", out var name, out issue))
            {
                control = null;
                return false;
            }

            control = new KeyboardControl(name);
            return true;
        }

        control = null;
        issue = new ValidationIssue("InvalidSchema", path);
        return false;
    }

    private static bool TryReadMap(
        EbinRecordReadView record,
        out InputActionMapV1? map,
        out ValidationIssue? issue)
    {
        map = null;

        if (!TryValidateRecordSchema(record, "input/map", 7, 7, "/", out issue)
            || !TryReadUInt32(record, 1, "/SchemaVersion", out var schemaVersion, out issue))
        {
            return false;
        }

        if (schemaVersion != 1)
        {
            issue = new ValidationIssue("UnknownSchemaVersion", "/SchemaVersion");
            return false;
        }

        if (!TryReadGuid(record, 2, "/MapId", out var mapId, out issue)
            || !TryReadUInt64(record, 3, "/Revision", out var wireRevision, out issue)
            || (wireRevision > long.MaxValue)
            || !TryReadUInt64(record, 4, "/InitialActiveContextMask", out var initialMask, out issue))
        {
            issue ??= new ValidationIssue("InvalidSchema", "/Revision");
            return false;
        }

        if (!TryReadActions(record, out var actions, out issue)
            || !TryReadBindings(record, out var bindings, out issue)
            || !TryReadContexts(record, out var contexts, out issue))
        {
            return false;
        }

        map = new InputActionMapV1(
            checked((int)schemaVersion),
            mapId,
            checked((long)wireRevision),
            initialMask,
            actions,
            contexts,
            bindings);
        return true;
    }

    private static bool TryReadMouseButton(
        EbinRecordReadView record,
        string path,
        out DigitalControl? control,
        out ValidationIssue? issue)
    {
        if (!TryValidateRecordSchema(record, "input/mousebutton", 1, 1, path, out issue)
            || !TryReadByte(record, 1, $"{path}/Button", out var button, out issue))
        {
            control = null;
            return false;
        }

        control = new DigitalControl((MouseButtonValue)button);
        return true;
    }

    private static bool TryReadOptionalSingle(
        EbinRecordReadView record,
        uint tag,
        string path,
        out float? value,
        out ValidationIssue? issue)
    {
        value = null;

        if (!record.TryGetField(tag, out _))
        {
            issue = null;
            return true;
        }

        if (!TryReadSingle(record, tag, path, out var single, out issue))
        {
            return false;
        }

        value = single;
        return true;
    }

    private static bool TryReadRecordArrayCount(
        EbinRecordReadView record,
        uint tag,
        int maximumCount,
        string path,
        out int count,
        out ValidationIssue? issue)
    {
        count = 0;

        if (!record.TryGetField(tag, out var field)
            || (field.Kind != RecordArrayKind))
        {
            issue = new ValidationIssue("InvalidSchema", path);
            return false;
        }

        if (field.Count > checked((ulong)maximumCount))
        {
            issue = new ValidationIssue("CapacityExceeded", path);
            return false;
        }

        count = checked((int)field.Count);
        issue = null;
        return true;
    }

    private static bool TryReadRecordField(
        EbinRecordReadView record,
        uint tag,
        string path,
        out EbinRecordReadView nestedRecord,
        out ValidationIssue? issue)
    {
        if (!record.TryGetField(tag, out var field) || (field.Kind != RecordKind) || (field.Count != 1))
        {
            nestedRecord = default;
            issue = new ValidationIssue("InvalidSchema", path);
            return false;
        }

        nestedRecord = record.GetRecord(tag);
        issue = null;
        return true;
    }

    private static bool TryReadRecordSchema(
        EbinRecordReadView record,
        string schemaKey,
        int requiredFields,
        int maximumFields,
        string path,
        out ValidationIssue? issue)
    {
        var header = record.Header;

        if (header.SchemaId != s_schemaIds[schemaKey])
        {
            issue = new ValidationIssue("InvalidSchema", path);
            return false;
        }

        if ((header.Major != 1) || (header.Minor != 0))
        {
            issue = new ValidationIssue("UnknownSchemaVersion", path);
            return false;
        }

        if ((record.FieldCount < requiredFields) || (record.FieldCount > maximumFields))
        {
            issue = new ValidationIssue("InvalidSchema", path);
            return false;
        }

        for (var index = 0; index < record.FieldCount; index++)
        {
            var field = record.GetField(index);
            var expectedTag = checked((uint)(index + 1));
            var expectedRequired = expectedTag <= checked((uint)requiredFields);

            if ((field.Tag != expectedTag) || (field.IsRequired != expectedRequired))
            {
                issue = new ValidationIssue("InvalidSchema", path);
                return false;
            }
        }

        issue = null;
        return true;
    }

    private static bool TryReadSingle(
        EbinRecordReadView record,
        uint tag,
        string path,
        out float value,
        out ValidationIssue? issue)
    {
        value = 0;

        if (!TryReadField(record, tag, Binary32Kind, 1, 4, path, out var field, out issue))
        {
            return false;
        }

        value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload));
        return true;
    }

    private static bool TryReadStickSource(
        EbinRecordReadView record,
        string path,
        out GamepadStick2Source? source,
        out ValidationIssue? issue)
    {
        source = null;

        if (!TryValidateRecordSchema(record, "input/gamepadstick2", 4, 4, path, out issue)
            || !TryReadUInt16(record, 1, $"{path}/PlayerSlot", out var playerSlot, out issue)
            || !TryReadByte(record, 2, $"{path}/Stick", out var stick, out issue)
            || !TryReadBoolean(record, 3, $"{path}/InvertY", out var invertY, out issue)
            || !TryReadSingle(record, 4, $"{path}/RadialDeadzone", out var deadzone, out issue))
        {
            return false;
        }

        source = new GamepadStick2Source(playerSlot, (GamepadStick)stick, invertY, deadzone);
        return true;
    }

    private static bool TryReadTriggerSource(
        EbinRecordReadView record,
        string path,
        out GamepadTriggerSource? source,
        out ValidationIssue? issue)
    {
        source = null;

        if (!TryValidateRecordSchema(record, "input/gamepadtrigger", 3, 3, path, out issue)
            || !TryReadUInt16(record, 1, $"{path}/PlayerSlot", out var playerSlot, out issue)
            || !TryReadByte(record, 2, $"{path}/Trigger", out var trigger, out issue)
            || !TryReadSingle(record, 3, $"{path}/Deadzone", out var deadzone, out issue))
        {
            return false;
        }

        source = new GamepadTriggerSource(playerSlot, (GamepadTrigger)trigger, deadzone);
        return true;
    }

    private static bool TryReadUInt16(
        EbinRecordReadView record,
        uint tag,
        string path,
        out ushort value,
        out ValidationIssue? issue)
    {
        value = 0;

        if (!TryReadField(record, tag, UInt16Kind, 1, 2, path, out var field, out issue))
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt16LittleEndian(field.Payload);
        return true;
    }

    private static bool TryReadUInt32(
        EbinRecordReadView record,
        uint tag,
        string path,
        out uint value,
        out ValidationIssue? issue)
    {
        value = 0;

        if (!TryReadField(record, tag, UInt32Kind, 1, 4, path, out var field, out issue))
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
        return true;
    }

    private static bool TryReadUInt64(
        EbinRecordReadView record,
        uint tag,
        string path,
        out ulong value,
        out ValidationIssue? issue)
    {
        value = 0;

        if (!TryReadField(record, tag, UInt64Kind, 1, 8, path, out var field, out issue))
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt64LittleEndian(field.Payload);
        return true;
    }

    private static bool TryReadUtf8(
        EbinRecordReadView record,
        uint tag,
        int maximumBytes,
        string path,
        out string value,
        out ValidationIssue? issue)
    {
        value = string.Empty;

        if (!record.TryGetField(tag, out var field)
            || (field.Kind != 12)
            || (field.Count != checked((ulong)field.Payload.Length))
            || (field.Payload.Length > maximumBytes))
        {
            issue = new ValidationIssue("InvalidStringOrEncoding", path);
            return false;
        }

        value = s_strictUtf8.GetString(field.Payload);
        issue = null;
        return true;
    }

    private static bool TryValidateRecordSchema(
        EbinRecordReadView record,
        string schemaKey,
        int requiredFields,
        int maximumFields,
        string path,
        out ValidationIssue? issue)
    {
        var header = record.Header;

        if (header.SchemaId != s_schemaIds[schemaKey])
        {
            issue = new ValidationIssue("InvalidSchema", path);
            return false;
        }

        if ((header.Major != 1) || (header.Minor != 0))
        {
            issue = new ValidationIssue("UnknownSchemaVersion", path);
            return false;
        }

        if ((record.FieldCount < requiredFields) || (record.FieldCount > maximumFields))
        {
            issue = new ValidationIssue("InvalidSchema", path);
            return false;
        }

        for (var index = 0; index < record.FieldCount; index++)
        {
            var field = record.GetField(index);
            var expectedTag = checked((uint)(index + 1));
            var expectedRequired = expectedTag <= checked((uint)requiredFields);

            if ((field.Tag != expectedTag) || (field.IsRequired != expectedRequired))
            {
                issue = new ValidationIssue("InvalidSchema", path);
                return false;
            }
        }

        issue = null;
        return true;
    }

    private struct BindingSourceData
    {
        public BindingSourceData()
        {
            Controls = [];
        }

        internal GamepadAxis1Source? Axis1Source { get; set; }

        internal int? CompletionWindowMilliseconds { get; set; }

        internal DigitalControl? Control { get; set; }

        internal ImmutableArray<DigitalControl> Controls { get; set; }

        internal KeyboardControl? Down { get; set; }

        internal KeyboardControl? Left { get; set; }

        internal KeyboardControl? Negative { get; set; }

        internal KeyboardControl? Positive { get; set; }

        internal float? PressThreshold { get; set; }

        internal float? ReleaseThreshold { get; set; }

        internal KeyboardControl? Right { get; set; }

        internal GamepadStick2Source? Stick2Source { get; set; }

        internal GamepadAxis1Source? ThresholdAxisSource { get; set; }

        internal AxisThresholdDirection? ThresholdDirection { get; set; }

        internal GamepadTriggerSource? ThresholdTriggerSource { get; set; }

        internal GamepadTriggerSource? TriggerSource { get; set; }

        internal KeyboardControl? Up { get; set; }
    }

    private struct DecodeAction : IAction<EbinRecordReadView>
    {
        private ValidationIssue? _issue;
        private InputActionMapV1? _map;

        internal readonly ValidationIssue? Issue => _issue;

        internal readonly InputActionMapV1? Map => _map;

        public void Invoke(scoped EbinRecordReadView argument)
        {
            _ = TryReadMap(argument, out _map, out _issue);
        }
    }
}
