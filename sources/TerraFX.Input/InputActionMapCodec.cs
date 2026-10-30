// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using TerraFX;

namespace TerraFX.Input;

internal static partial class InputActionMapCodec
{
    private const ushort Binary32Kind = 10;
    private const ushort BooleanKind = 9;
    private const ushort GuidKind = 11;
    private const ushort Int32Kind = 7;
    private const int MaximumEncodedBytes = 2_097_152;
    private const ushort RecordArrayKind = 24;
    private const ushort RecordKind = 22;
    private const ushort UInt16Kind = 2;
    private const ushort UInt32Kind = 3;
    private const ushort UInt64Kind = 4;
    private const ushort UInt8Kind = 1;

    private static readonly EbinReadLimits s_readLimits = new(
        MaximumEncodedBytes,
        64,
        64,
        1_048_576,
        1_048_576,
        16_384,
        65_536,
        65_536);
    private static readonly ImmutableDictionary<string, Guid> s_schemaIds = CreateSchemaIds();
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    internal static byte[] Encode(InputActionMapV1 map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var issue = InputActionMapValidator.Validate(map);

        if (issue is not null)
        {
            throw new ArgumentException($"The input map is invalid at {issue.SchemaPath}: {issue.Code}.", nameof(map));
        }

        var actions = new byte[map.Actions.Length][];

        for (var index = 0; index < actions.Length; index++)
        {
            actions[index] = EncodeAction(map.Actions[index]);
        }

        var contexts = new byte[map.Contexts.Length][];

        for (var index = 0; index < contexts.Length; index++)
        {
            contexts[index] = EncodeContext(map.Contexts[index]);
        }

        var bindings = new byte[map.Bindings.Length][];

        for (var index = 0; index < bindings.Length; index++)
        {
            bindings[index] = EncodeBinding(map.Bindings[index]);
        }

        return BuildRecord("input/map", [
            RequiredField(1, UInt32Kind, 1, EncodeUInt32(checked((uint)map.SchemaVersion))),
            RequiredField(2, GuidKind, 1, EncodeGuid(map.MapId)),
            RequiredField(3, UInt64Kind, 1, EncodeUInt64(checked((ulong)map.Revision))),
            RequiredField(4, UInt64Kind, 1, EncodeUInt64(map.InitialActiveContextMask)),
            RequiredField(5, RecordArrayKind, checked((ulong)actions.Length), EncodeRecordArray(actions)),
            RequiredField(6, RecordArrayKind, checked((ulong)contexts.Length), EncodeRecordArray(contexts)),
            RequiredField(7, RecordArrayKind, checked((ulong)bindings.Length), EncodeRecordArray(bindings)),
        ]);
    }

    private static byte[] BuildRecord(string schemaKey, EncodedField[] fields)
    {
        Array.Sort(fields, static (left, right) => left.Tag.CompareTo(right.Tag));
        var payloadLength = 0;

        for (var index = 0; index < fields.Length; index++)
        {
            payloadLength = checked(payloadLength + fields[index].Payload.Length);
        }

        var expectedRecordBytes = checked(64 + (fields.Length * 32));

        for (var index = 0; index < fields.Length; index++)
        {
            expectedRecordBytes = checked((expectedRecordBytes + 15) & ~15);
            expectedRecordBytes = checked(expectedRecordBytes + fields[index].Payload.Length);
        }

        expectedRecordBytes = checked((expectedRecordBytes + 15) & ~15);

        if (expectedRecordBytes > MaximumEncodedBytes)
        {
            throw new InvalidOperationException("The encoded input record exceeds its admitted byte budget.");
        }

        if (payloadLength > MaximumEncodedBytes)
        {
            throw new InvalidOperationException("The input record payload exceeds its admitted byte budget.");
        }

        var payloadStorage = new byte[payloadLength];
        var fieldSources = new EbinFieldSource[fields.Length];
        var payloadOffset = 0;

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            field.Payload.CopyTo(payloadStorage.AsSpan(payloadOffset));
            fieldSources[index] = new EbinFieldSource(
                field.Tag,
                field.Kind,
                field.Flags,
                field.Count,
                new EbinPayloadSlice(payloadOffset, field.Payload.Length));
            payloadOffset = checked(payloadOffset + field.Payload.Length);
        }

        var source = new EbinRecordWriteView(
            s_schemaIds[schemaKey],
            1,
            0,
            fieldSources,
            payloadStorage);
        var status = EbinStructuralWriter.Measure(source, s_readLimits, out var requiredBytes, out var diagnostic);

        if (status != EbinWriteStatus.Complete)
        {
            ThrowWriteFailure(status, diagnostic);
        }

        if (requiredBytes != expectedRecordBytes)
        {
            throw new InvalidOperationException("The EBIN writer returned an inconsistent measured extent.");
        }

        var candidate = new byte[requiredBytes];
        status = EbinStructuralWriter.Prepare(source, s_readLimits, candidate, out var candidateBytes, out diagnostic);

        if (status != EbinWriteStatus.Complete)
        {
            ThrowWriteFailure(status, diagnostic);
        }

        if (candidateBytes != candidate.Length)
        {
            throw new InvalidOperationException("The EBIN writer returned an inconsistent measured extent.");
        }

        return candidate;
    }

    private static ImmutableDictionary<string, Guid> CreateSchemaIds()
    {
        var names = new[] {
            "input/action",
            "input/axis1keys",
            "input/axis2keys",
            "input/axisthreshold",
            "input/binding",
            "input/button",
            "input/chord",
            "input/context",
            "input/gamepadaxis1",
            "input/gamepadbutton",
            "input/gamepadstick2",
            "input/gamepadtrigger",
            "input/keyboardhid",
            "input/logicalkey",
            "input/map",
            "input/mousebutton",
        };
        var schemaIds = ImmutableDictionary.CreateBuilder<string, Guid>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            schemaIds.Add(name, new ContentSchemaIdentityAssignment(name).SchemaId);
        }

        return schemaIds.ToImmutable();
    }

    private static byte[] EncodeAction(ActionDefinition action)
    {
        var fields = new List<EncodedField>(5) {
            RequiredField(1, GuidKind, 1, EncodeGuid(action.ActionId)),
            RequiredField(2, 12, checked((ulong)s_strictUtf8.GetByteCount(action.Name)), EncodeUtf8(action.Name)),
            RequiredField(3, UInt8Kind, 1, EncodeByte(ToWireActionKind(action.Kind))),
        };

        if (action.DownPressThreshold is { } pressThreshold)
        {
            fields.Add(OptionalField(4, Binary32Kind, 1, EncodeSingle(pressThreshold)));
        }

        if (action.DownReleaseThreshold is { } releaseThreshold)
        {
            fields.Add(OptionalField(5, Binary32Kind, 1, EncodeSingle(releaseThreshold)));
        }

        return BuildRecord("input/action", [.. fields]);
    }

    private static byte[] EncodeAxis1Keys(BindingDefinition binding)
        => BuildRecord("input/axis1keys", [
            RequiredField(1, RecordKind, 1, EncodeKeyboardControl(binding.Negative!)),
            RequiredField(2, RecordKind, 1, EncodeKeyboardControl(binding.Positive!)),
        ]);

    private static byte[] EncodeAxis1Source(GamepadAxis1Source source)
        => BuildRecord("input/gamepadaxis1", [
            RequiredField(1, UInt16Kind, 1, EncodeUInt16(source.PlayerSlot)),
            RequiredField(2, UInt8Kind, 1, EncodeByte(checked((byte)source.Axis))),
            RequiredField(3, BooleanKind, 1, EncodeBoolean(source.Invert)),
            RequiredField(4, Binary32Kind, 1, EncodeSingle(source.Deadzone)),
        ]);

    private static byte[] EncodeAxis2Keys(BindingDefinition binding)
        => BuildRecord("input/axis2keys", [
            RequiredField(1, RecordKind, 1, EncodeKeyboardControl(binding.Left!)),
            RequiredField(2, RecordKind, 1, EncodeKeyboardControl(binding.Right!)),
            RequiredField(3, RecordKind, 1, EncodeKeyboardControl(binding.Down!)),
            RequiredField(4, RecordKind, 1, EncodeKeyboardControl(binding.Up!)),
        ]);

    private static byte[] EncodeAxisThreshold(BindingDefinition binding)
    {
        var source = binding.ThresholdAxisSource is { } axisSource
            ? EncodeAxis1Source(axisSource)
            : EncodeTriggerSource(binding.ThresholdTriggerSource!);

        return BuildRecord("input/axisthreshold", [
            RequiredField(1, RecordKind, 1, source),
            RequiredField(2, UInt8Kind, 1, EncodeByte(checked((byte)binding.ThresholdDirection!.Value))),
            RequiredField(3, Binary32Kind, 1, EncodeSingle(binding.PressThreshold ?? 0.55f)),
            RequiredField(4, Binary32Kind, 1, EncodeSingle(binding.ReleaseThreshold ?? 0.45f)),
        ]);
    }

    private static byte[] EncodeBinding(BindingDefinition binding)
        => BuildRecord("input/binding", [
            RequiredField(1, GuidKind, 1, EncodeGuid(binding.BindingId)),
            RequiredField(2, GuidKind, 1, EncodeGuid(binding.ActionId)),
            RequiredField(3, UInt64Kind, 1, EncodeUInt64(binding.ContextMask)),
            RequiredField(4, Int32Kind, 1, EncodeInt32(binding.Priority)),
            RequiredField(5, UInt8Kind, 1, EncodeByte(ToWireBindingKind(binding.Kind))),
            RequiredField(6, BooleanKind, 1, EncodeBoolean(binding.Enabled)),
            RequiredField(7, UInt8Kind, 1, EncodeByte(checked((byte)binding.Flags))),
            RequiredField(8, RecordKind, 1, EncodeBindingSource(binding)),
        ]);

    private static byte[] EncodeBindingSource(BindingDefinition binding)
        => binding.Kind switch {
            BindingKind.Button => BuildRecord("input/button", [
                RequiredField(1, RecordKind, 1, EncodeDigitalControl(binding.Control!)),
            ]),
            BindingKind.Chord => EncodeChord(binding),
            BindingKind.Axis1Keys => EncodeAxis1Keys(binding),
            BindingKind.Axis2Keys => EncodeAxis2Keys(binding),
            BindingKind.GamepadAxis1 => EncodeAxis1Source(binding.Axis1Source!),
            BindingKind.GamepadStick2 => EncodeStickSource(binding.Stick2Source!),
            BindingKind.GamepadTrigger => EncodeTriggerSource(binding.TriggerSource!),
            BindingKind.AxisThreshold => EncodeAxisThreshold(binding),
            _ => throw new InvalidOperationException("The validated input map contains an unsupported binding kind."),
        };

    private static byte[] EncodeBoolean(bool value) => [value ? (byte)1 : (byte)0];

    private static byte[] EncodeByte(byte value) => [value];

    private static byte[] EncodeChord(BindingDefinition binding)
    {
        var controls = new byte[binding.Controls.Length][];

        for (var index = 0; index < controls.Length; index++)
        {
            controls[index] = EncodeDigitalControl(binding.Controls[index]);
        }

        var fields = new List<EncodedField>(2) {
            RequiredField(1, RecordArrayKind, checked((ulong)controls.Length), EncodeRecordArray(controls)),
        };

        if (binding.CompletionWindowMilliseconds is { } completionWindow)
        {
            fields.Add(OptionalField(2, UInt32Kind, 1, EncodeUInt32(checked((uint)completionWindow))));
        }

        return BuildRecord("input/chord", [.. fields]);
    }

    private static byte[] EncodeContext(ContextDefinition context)
        => BuildRecord("input/context", [
            RequiredField(1, GuidKind, 1, EncodeGuid(context.ContextId)),
            RequiredField(2, UInt8Kind, 1, EncodeByte(context.Bit)),
            RequiredField(3, Int32Kind, 1, EncodeInt32(context.Priority)),
            RequiredField(4, UInt8Kind, 1, EncodeByte(checked((byte)context.CaptureClasses))),
        ]);

    private static byte[] EncodeDigitalControl(DigitalControl control)
        => control.Kind switch {
            DigitalControlKind.KeyboardHid or DigitalControlKind.LogicalKey => EncodeKeyboardControl(control.Keyboard!),
            DigitalControlKind.MouseButton => EncodeMouseButton(control.MouseButton!.Value),
            DigitalControlKind.GamepadButton => EncodeGamepadButton(control.PlayerSlot!.Value, control.GamepadButton!.Value),
            _ => throw new InvalidOperationException("The validated input map contains an unsupported digital-control kind."),
        };

    private static byte[] EncodeGamepadButton(ushort playerSlot, GamepadButtonValue button)
        => BuildRecord("input/gamepadbutton", [
            RequiredField(1, UInt16Kind, 1, EncodeUInt16(playerSlot)),
            RequiredField(2, UInt8Kind, 1, EncodeByte(checked((byte)button))),
        ]);

    private static byte[] EncodeGuid(Guid value)
    {
        var payload = new byte[16];
        _ = value.TryWriteBytes(payload, bigEndian: true, out _);
        return payload;
    }

    private static byte[] EncodeInt32(int value)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, value);
        return payload;
    }

    private static byte[] EncodeKeyboardControl(KeyboardControl control)
        => control.Kind switch {
            KeyboardControlKind.KeyboardHid => BuildRecord("input/keyboardhid", [
                RequiredField(1, UInt16Kind, 1, EncodeUInt16(control.UsagePage)),
                RequiredField(2, UInt16Kind, 1, EncodeUInt16(control.Usage)),
            ]),
            KeyboardControlKind.LogicalKey => BuildRecord("input/logicalkey", [
                RequiredField(1, 12, checked((ulong)s_strictUtf8.GetByteCount(control.LogicalKeyName!)), EncodeUtf8(control.LogicalKeyName!)),
            ]),
            _ => throw new InvalidOperationException("The validated input map contains an unsupported keyboard-control kind."),
        };

    private static byte[] EncodeMouseButton(MouseButtonValue button)
        => BuildRecord("input/mousebutton", [
            RequiredField(1, UInt8Kind, 1, EncodeByte(checked((byte)button))),
        ]);

    private static byte[] EncodeRecordArray(byte[][] records)
    {
        var storageLength = 0;

        for (var index = 0; index < records.Length; index++)
        {
            storageLength = checked(storageLength + records[index].Length);
        }

        var offsetCount = checked(records.Length + 1);
        var firstChildOffset = checked((8 + (offsetCount * 8) + 15) & ~15);
        var expectedPayloadBytes = checked((firstChildOffset + storageLength + 15) & ~15);

        if (expectedPayloadBytes > MaximumEncodedBytes)
        {
            throw new InvalidOperationException("The encoded input record array exceeds its admitted byte budget.");
        }

        var childStorage = new byte[storageLength];
        var childRanges = new EbinPayloadSlice[records.Length];
        var offset = 0;

        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            record.CopyTo(childStorage, offset);
            childRanges[index] = new EbinPayloadSlice(offset, record.Length);
            offset = checked(offset + record.Length);
        }

        var status = EbinPayloadWriter.MeasureRecordArray(childRanges, childStorage, s_readLimits, out var requiredBytes, out var diagnostic);

        if (status != EbinWriteStatus.Complete)
        {
            ThrowWriteFailure(status, diagnostic);
        }

        if (requiredBytes != expectedPayloadBytes)
        {
            throw new InvalidOperationException("The EBIN writer returned an inconsistent record-array extent.");
        }

        var payload = new byte[requiredBytes];
        status = EbinPayloadWriter.WriteRecordArray(childRanges, childStorage, s_readLimits, payload, out var payloadBytes, out diagnostic);

        if (status != EbinWriteStatus.Complete)
        {
            ThrowWriteFailure(status, diagnostic);
        }

        if (payloadBytes != payload.Length)
        {
            throw new InvalidOperationException("The EBIN writer returned an inconsistent record-array extent.");
        }

        return payload;
    }

    private static byte[] EncodeSingle(float value)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, BitConverter.SingleToInt32Bits(value));
        return payload;
    }

    private static byte[] EncodeStickSource(GamepadStick2Source source)
        => BuildRecord("input/gamepadstick2", [
            RequiredField(1, UInt16Kind, 1, EncodeUInt16(source.PlayerSlot)),
            RequiredField(2, UInt8Kind, 1, EncodeByte(checked((byte)source.Stick))),
            RequiredField(3, BooleanKind, 1, EncodeBoolean(source.InvertY)),
            RequiredField(4, Binary32Kind, 1, EncodeSingle(source.RadialDeadzone)),
        ]);

    private static byte[] EncodeTriggerSource(GamepadTriggerSource source)
        => BuildRecord("input/gamepadtrigger", [
            RequiredField(1, UInt16Kind, 1, EncodeUInt16(source.PlayerSlot)),
            RequiredField(2, UInt8Kind, 1, EncodeByte(checked((byte)source.Trigger))),
            RequiredField(3, Binary32Kind, 1, EncodeSingle(source.Deadzone)),
        ]);

    private static byte[] EncodeUInt16(ushort value)
    {
        var payload = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, value);
        return payload;
    }

    private static byte[] EncodeUInt32(uint value)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, value);
        return payload;
    }

    private static byte[] EncodeUInt64(ulong value)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, value);
        return payload;
    }

    private static byte[] EncodeUtf8(string value) => s_strictUtf8.GetBytes(value);

    private static EncodedField OptionalField(uint tag, ushort kind, ulong count, byte[] payload)
        => new EncodedField(tag, kind, 0, count, payload);

    private static EncodedField RequiredField(uint tag, ushort kind, ulong count, byte[] payload)
        => new EncodedField(tag, kind, 1, count, payload);

    private static void ThrowWriteFailure(EbinWriteStatus status, EbinStructuralDiagnostic? diagnostic)
        => throw new InvalidOperationException(
            $"The valid input map could not be encoded: {status} ({diagnostic?.Category ?? "no diagnostic"}).");

    private static byte ToWireActionKind(InputActionKind kind)
        => kind switch {
            InputActionKind.Digital => 1,
            InputActionKind.Axis1 => 2,
            InputActionKind.Axis2 => 3,
            _ => throw new InvalidOperationException("The validated input map contains an unsupported action kind."),
        };

    private static byte ToWireBindingKind(BindingKind kind)
        => kind switch {
            BindingKind.Button => 1,
            BindingKind.Chord => 2,
            BindingKind.Axis1Keys => 3,
            BindingKind.Axis2Keys => 4,
            BindingKind.GamepadAxis1 => 5,
            BindingKind.GamepadStick2 => 6,
            BindingKind.GamepadTrigger => 7,
            BindingKind.AxisThreshold => 8,
            _ => throw new InvalidOperationException("The validated input map contains an unsupported binding kind."),
        };

    private readonly record struct EncodedField(uint Tag, ushort Kind, ushort Flags, ulong Count, byte[] Payload);
}
