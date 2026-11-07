// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class LinuxEvdevGamepadStateTests
{
    [Test]
    public void AggregatesButtonsHatsAndAxesIntoReconciliationState()
    {
        var state = new LinuxEvdevGamepadState([
            (0x0000, new LinuxEvdevAxisInfo(-100, 100, Fuzz: 0, Flat: 0), 0),
            (0x0001, new LinuxEvdevAxisInfo(-100, 100, Fuzz: 0, Flat: 0), 0),
            (0x0002, new LinuxEvdevAxisInfo(0, 100, Fuzz: 0, Flat: 0), 0),
        ]);
        var initialState = state.GetCurrentState();
        var buttonCount = Enum.GetValues<GamepadButtonValue>().Length;

        Assert.That(initialState, Has.Length.EqualTo(buttonCount + 8));

        for (var index = 0; index < buttonCount; index++)
        {
            Assert.That(initialState[index].Kind, Is.EqualTo(PlatformInputStateValueKind.GamepadButton));
            Assert.That(initialState[index].IsDown, Is.False);
        }

        Assert.That(state.TryProcessEvent(0x0001, 0x0130, 1, out var southDown), Is.True);
        Assert.That(southDown, Has.Length.EqualTo(1));
        Assert.That(southDown[0].GamepadButton, Is.EqualTo(GamepadButtonValue.South));
        Assert.That(southDown[0].IsDown, Is.True);

        Assert.That(state.TryProcessEvent(0x0003, 0x0001, -100, out var leftYChange), Is.True);
        Assert.That(leftYChange, Has.Length.EqualTo(2));
        Assert.That(leftYChange[0].Axis, Is.EqualTo(GamepadAxis.LeftY));
        Assert.That(leftYChange[0].AxisValue, Is.EqualTo(1.0f));
        var leftStick = leftYChange[1].StickValue
            ?? throw new InvalidOperationException("The evdev left-stick update has no value.");
        Assert.That(leftStick.X, Is.Zero);
        Assert.That(leftStick.Y, Is.EqualTo(1.0f));

        Assert.That(state.TryProcessEvent(0x0003, 0x0002, 100, out var triggerChange), Is.True);
        Assert.That(triggerChange, Has.Length.EqualTo(1));
        Assert.That(triggerChange[0].Trigger, Is.EqualTo(GamepadTrigger.Left));
        Assert.That(triggerChange[0].TriggerValue, Is.EqualTo(1.0f));

        var reconciledState = state.GetCurrentState();
        Assert.That(reconciledState[0].GamepadButton, Is.EqualTo(GamepadButtonValue.South));
        Assert.That(reconciledState[0].IsDown, Is.True);
        Assert.That(reconciledState[buttonCount + 1].AxisValue, Is.EqualTo(1.0f));
        Assert.That(reconciledState[buttonCount + 6].TriggerValue, Is.EqualTo(1.0f));
    }

    [Test]
    public void DeduplicatesHatAndButtonEventsAcrossRepresentations()
    {
        var state = new LinuxEvdevGamepadState([]);

        Assert.That(state.TryProcessEvent(0x0001, 0x0222, 1, out var leftDown), Is.True);
        Assert.That(leftDown[0].GamepadButton, Is.EqualTo(GamepadButtonValue.DpadLeft));
        Assert.That(state.TryProcessEvent(0x0003, 0x0010, -1, out var duplicateHat), Is.False);
        Assert.That(duplicateHat, Is.Empty);
        Assert.That(state.TryProcessEvent(0x0001, 0x0222, 0, out var heldByHat), Is.False);
        Assert.That(heldByHat, Is.Empty);
        Assert.That(state.TryProcessEvent(0x0003, 0x0010, 0, out var leftUp), Is.True);
        Assert.That(leftUp, Has.Length.EqualTo(1));
        Assert.That(leftUp[0].GamepadButton, Is.EqualTo(GamepadButtonValue.DpadLeft));
        Assert.That(leftUp[0].IsDown, Is.False);
    }

    [Test]
    public void IgnoresUnrecognizedAndSynchronizationEvents()
    {
        var state = new LinuxEvdevGamepadState([]);

        Assert.That(state.TryProcessEvent(0x0000, 0, 0, out var sync), Is.False);
        Assert.That(sync, Is.Empty);
        Assert.That(state.TryProcessEvent(0x0003, 0x0020, 1, out var unknownAxis), Is.False);
        Assert.That(unknownAxis, Is.Empty);
        Assert.That(state.TryProcessEvent(0x0001, 0x0200, 1, out var unknownButton), Is.False);
        Assert.That(unknownButton, Is.Empty);
    }

    [Test]
    public void RejectsInvalidDigitalButtonValuesAndIgnoresKeyRepeat()
    {
        var state = new LinuxEvdevGamepadState([]);

        _ = Assert.Throws<InvalidDataException>(
            () => state.TryProcessEvent(0x0001, 0x0130, 3, out _));
        Assert.That(state.TryProcessEvent(0x0001, 0x0130, 2, out var repeat), Is.False);
        Assert.That(repeat, Is.Empty);
    }
}
