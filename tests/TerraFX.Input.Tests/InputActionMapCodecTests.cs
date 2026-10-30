// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class InputActionMapCodecTests
{
    private static InputActionMapV1 CreateAllBindingKindsMap()
    {
        var digitalAction = new ActionDefinition(
            new Guid("20000000-0000-0000-0000-000000000001"),
            "Digital",
            InputActionKind.Digital);
        var axis1Action = new ActionDefinition(
            new Guid("20000000-0000-0000-0000-000000000002"),
            "MoveX",
            InputActionKind.Axis1,
            0.7f,
            0.2f);
        var axis2Action = new ActionDefinition(
            new Guid("20000000-0000-0000-0000-000000000003"),
            "Move",
            InputActionKind.Axis2);
        var context = new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000001"), 0);
        var keyboard = new KeyboardControl(7, 80);

        return new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000001"),
            1,
            1,
            [digitalAction, axis1Action, axis2Action],
            [context],
            [
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000001"), digitalAction.ActionId, 1, BindingKind.Button) {
                    Control = new DigitalControl(keyboard),
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000002"), digitalAction.ActionId, 1, BindingKind.Chord) {
                    Controls = [new DigitalControl(keyboard), new DigitalControl(MouseButtonValue.Primary)],
                    CompletionWindowMilliseconds = 250,
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000003"), axis1Action.ActionId, 1, BindingKind.Axis1Keys) {
                    Negative = new KeyboardControl(7, 80),
                    Positive = new KeyboardControl(7, 79),
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000004"), axis2Action.ActionId, 1, BindingKind.Axis2Keys) {
                    Left = new KeyboardControl(7, 80),
                    Right = new KeyboardControl(7, 79),
                    Down = new KeyboardControl(7, 81),
                    Up = new KeyboardControl(7, 82),
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000005"), axis1Action.ActionId, 1, BindingKind.GamepadAxis1) {
                    Axis1Source = new GamepadAxis1Source(3, GamepadAxis.RightY, invert: true, deadzone: 0.25f),
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000006"), axis2Action.ActionId, 1, BindingKind.GamepadStick2) {
                    Stick2Source = new GamepadStick2Source(4, GamepadStick.Left, invertY: true, radialDeadzone: 0.2f),
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000007"), axis1Action.ActionId, 1, BindingKind.GamepadTrigger) {
                    TriggerSource = new GamepadTriggerSource(2, GamepadTrigger.Right, deadzone: 0.05f),
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000008"), digitalAction.ActionId, 1, BindingKind.AxisThreshold) {
                    ThresholdAxisSource = new GamepadAxis1Source(5, GamepadAxis.LeftX, invert: false, deadzone: 0.1f),
                    ThresholdDirection = AxisThresholdDirection.Positive,
                    PressThreshold = 0.6f,
                    ReleaseThreshold = 0.4f,
                },
                new BindingDefinition(new Guid("40000000-0000-0000-0000-000000000009"), digitalAction.ActionId, 1, BindingKind.AxisThreshold) {
                    ThresholdTriggerSource = new GamepadTriggerSource(6, GamepadTrigger.Left, deadzone: 0.05f),
                    ThresholdDirection = AxisThresholdDirection.Magnitude,
                    PressThreshold = 0.7f,
                    ReleaseThreshold = 0.3f,
                },
            ]);
    }

    private static InputActionMapV1 CreateCanonicalMap()
        => new InputActionMapV1(
            1,
            new Guid("10000000-0000-0000-0000-000000000001"),
            1,
            1,
            [new ActionDefinition(new Guid("20000000-0000-0000-0000-000000000001"), "Jump", InputActionKind.Digital)],
            [new ContextDefinition(new Guid("30000000-0000-0000-0000-000000000001"), 0)],
            [
                new BindingDefinition(
                    new Guid("40000000-0000-0000-0000-000000000001"),
                    new Guid("20000000-0000-0000-0000-000000000001"),
                    1,
                    BindingKind.Button) {
                        Control = new DigitalControl(new KeyboardControl(7, 80)),
                    },
            ]);

    private static InputActionMapV1 CreateMaximumEncodedMap()
    {
        var actions = ImmutableArray.CreateBuilder<ActionDefinition>(256);

        for (var index = 0; index < 256; index++)
        {
            var name = string.Concat(
                "Action",
                index.ToString("D3", CultureInfo.InvariantCulture),
                new string('a', 119));
            actions.Add(new ActionDefinition(Guid.NewGuid(), name, InputActionKind.Digital));
        }

        var contexts = ImmutableArray.CreateBuilder<ContextDefinition>(64);

        for (var index = 0; index < 64; index++)
        {
            contexts.Add(new ContextDefinition(Guid.NewGuid(), checked((byte)index)));
        }

        var bindings = ImmutableArray.CreateBuilder<BindingDefinition>(1_024);

        for (var bindingIndex = 0; bindingIndex < 1_024; bindingIndex++)
        {
            var controls = ImmutableArray.CreateBuilder<DigitalControl>(4);

            for (var controlIndex = 0; controlIndex < 4; controlIndex++)
            {
                var name = string.Concat(
                    "K",
                    bindingIndex.ToString("D4", CultureInfo.InvariantCulture),
                    "-",
                    controlIndex.ToString(CultureInfo.InvariantCulture),
                    new string('k', 57));
                controls.Add(new DigitalControl(new KeyboardControl(name)));
            }

            bindings.Add(new BindingDefinition(
                Guid.NewGuid(),
                actions[bindingIndex % actions.Count].ActionId,
                ulong.MaxValue,
                BindingKind.Chord) {
                Controls = controls.MoveToImmutable(),
                CompletionWindowMilliseconds = 60_000,
            });
        }

        return new InputActionMapV1(
            1,
            Guid.NewGuid(),
            1,
            ulong.MaxValue,
            actions.MoveToImmutable(),
            contexts.MoveToImmutable(),
            bindings.MoveToImmutable());
    }

    [Test]
    public void DecodingRejectsTrailingBytes()
    {
        var record = InputActionMapCodec.Encode(CreateCanonicalMap());
        var source = new byte[record.Length + 1];
        record.CopyTo(source, 0);

        var status = InputActionMapCodec.TryDecode(
            source,
            out var map,
            out var bytesConsumed,
            out var diagnostic,
            out var issue);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(bytesConsumed, Is.EqualTo(record.Length));
        Assert.That(diagnostic, Is.Null);
        Assert.That(map, Is.Null);
        Assert.That(issue?.Code, Is.EqualTo("InvalidSchema"));
        Assert.That(issue?.SchemaPath, Is.EqualTo("/"));
    }

    [Test]
    public void EncodingMatchesTheCanonicalInputMapLiteral()
    {
        const string ExpectedHex = """
            4542494E0100000040000000070000009EEAA7C11DA18A39BF59626F8622E2554006000000000000E00000000000000001000000000000000000000000000000
            0100000003000100200100000000000004000000000000000100000000000000020000000B000100300100000000000010000000000000000100000000000000
            03000000040001004001000000000000080000000000000001000000000000000400000004000100500100000000000008000000000000000100000000000000
            05000000180001006001000000000000F00000000000000001000000000000000600000018000100500200000000000020010000000000000100000000000000
            07000000180001007003000000000000D00200000000000001000000000000000100000000000000000000000000000010000000000000000000000000000001
            010000000000000000000000000000000100000000000000000000000000000001000000000000002000000000000000F0000000000000000000000000000000
            4542494E010000004000000003000000480FA06D77608269BE8E703677339EDFD000000000000000600000000000000001000000000000000000000000000000
            010000000B000100A00000000000000010000000000000000100000000000000020000000C000100B00000000000000004000000000000000400000000000000
            0300000001000100C00000000000000001000000000000000100000000000000200000000000000000000000000000014A756D70000000000000000000000000
            0100000000000000000000000000000001000000000000002000000000000000200100000000000000000000000000004542494E010000004000000004000000
            598BEAD2D7388436ADF33A3DD5E853900001000000000000800000000000000001000000000000000000000000000000010000000B000100C000000000000000
            100000000000000001000000000000000200000001000100D000000000000000010000000000000001000000000000000300000007000100E000000000000000
            040000000000000001000000000000000400000001000100F0000000000000000100000000000000010000000000000030000000000000000000000000000001
            00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000000000002000000000000000
            D00200000000000000000000000000004542494E010000004000000008000000F9F981FF1FF281C497D6252F8CB53208B0020000000000000001000000000000
            01000000000000000000000000000000010000000B000100400100000000000010000000000000000100000000000000020000000B0001005001000000000000
            10000000000000000100000000000000030000000400010060010000000000000800000000000000010000000000000004000000070001007001000000000000
            04000000000000000100000000000000050000000100010080010000000000000100000000000000010000000000000006000000090001009001000000000000
            010000000000000001000000000000000700000001000100A001000000000000010000000000000001000000000000000800000016000100B001000000000000
            00010000000000000100000000000000400000000000000000000000000000012000000000000000000000000000000101000000000000000000000000000000
            00000000000000000000000000000000010000000000000000000000000000000100000000000000000000000000000000000000000000000000000000000000
            4542494E010000004000000001000000880727E7690F868C81D69980733C97E30001000000000000200000000000000001000000000000000000000000000000
            01000000160001006000000000000000A00000000000000001000000000000004542494E010000004000000002000000E9C4321E6EAA8546BFB4CDA1431EFC25
            A0000000000000004000000000000000010000000000000000000000000000000100000002000100800000000000000002000000000000000100000000000000
            02000000020001009000000000000000020000000000000001000000000000000700000000000000000000000000000050000000000000000000000000000000
            """;
        var expected = Convert.FromHexString(string.Concat(ExpectedHex.Where(static value => !char.IsWhiteSpace(value))));
        var actual = InputActionMapCodec.Encode(CreateCanonicalMap());

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void MaximumVersionOneMapFitsItsEncodedBudget()
    {
        var encoded = InputActionMapCodec.Encode(CreateMaximumEncodedMap());

        Assert.That(encoded.Length, Is.LessThanOrEqualTo(2_097_152));
        Assert.That(
            InputActionMapCodec.TryDecode(
                encoded,
                out var map,
                out var bytesConsumed,
                out var diagnostic,
                out var issue),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(bytesConsumed, Is.EqualTo(encoded.Length));
        Assert.That(diagnostic, Is.Null);
        Assert.That(issue, Is.Null);
        Assert.That(map, Is.Not.Null);
    }

    [Test]
    public void RoundTripsEveryBindingSourceKind()
    {
        var original = CreateAllBindingKindsMap();
        var encoded = InputActionMapCodec.Encode(original);
        var status = InputActionMapCodec.TryDecode(
            encoded,
            out var decoded,
            out var bytesConsumed,
            out var diagnostic,
            out var issue);

        Assert.That(status, Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(bytesConsumed, Is.EqualTo(encoded.Length));
        Assert.That(diagnostic, Is.Null);
        Assert.That(issue, Is.Null);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Actions.Select(static action => action.Kind), Is.EqualTo(original.Actions.Select(static action => action.Kind)));
        Assert.That(decoded.Bindings.Select(static binding => binding.Kind), Is.EqualTo(original.Bindings.Select(static binding => binding.Kind)));
        Assert.That(decoded.Bindings[0].Control?.Keyboard?.Usage, Is.EqualTo((ushort)80));
        Assert.That(decoded.Bindings[1].Controls.Length, Is.EqualTo(2));
        Assert.That(decoded.Bindings[4].Axis1Source?.Deadzone, Is.EqualTo(0.25f));
        Assert.That(decoded.Bindings[5].Stick2Source?.InvertY, Is.True);
        Assert.That(decoded.Bindings[6].TriggerSource?.Trigger, Is.EqualTo(GamepadTrigger.Right));
        Assert.That(decoded.Bindings[7].ThresholdAxisSource?.Axis, Is.EqualTo(GamepadAxis.LeftX));
        Assert.That(decoded.Bindings[8].ThresholdTriggerSource?.Trigger, Is.EqualTo(GamepadTrigger.Left));
    }

    [Test]
    public void WireKindIdsAreOneBasedAndExplicit()
    {
        var encoded = InputActionMapCodec.Encode(CreateAllBindingKindsMap());
        var reader = default(WireKindReader);

        Assert.That(
            EbinStructuralReader.Read(
                encoded,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref reader,
                out var bytesConsumed,
                out var diagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(bytesConsumed, Is.EqualTo(encoded.Length));
        Assert.That(diagnostic, Is.Null);
        Assert.That(reader.ActionKinds, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(reader.BindingKinds, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 8 }));
    }

    private struct WireKindReader : IAction<EbinRecordReadView>
    {
        internal ImmutableArray<byte> ActionKinds { get; private set; }

        internal ImmutableArray<byte> BindingKinds { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var actionCount = checked((int)record.GetField(4).Count);
            var actions = ImmutableArray.CreateBuilder<byte>(actionCount);

            for (var index = 0; index < actionCount; index++)
            {
                actions.Add(record.GetRecordArrayElement(5, index).GetField(2).Payload[0]);
            }

            var bindingCount = checked((int)record.GetField(6).Count);
            var bindings = ImmutableArray.CreateBuilder<byte>(bindingCount);

            for (var index = 0; index < bindingCount; index++)
            {
                bindings.Add(record.GetRecordArrayElement(7, index).GetField(4).Payload[0]);
            }

            ActionKinds = actions.MoveToImmutable();
            BindingKinds = bindings.MoveToImmutable();
        }
    }
}
