// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.IO;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class LinuxEvdevGamepadButtonStateTests
{
    [Test]
    public void DeduplicatesHatAndButtonRepresentationsOfDpadControls()
    {
        var state = new LinuxEvdevGamepadButtonState();

        Assert.That(state.TryUpdateButton(0x0222, isDown: true, out var leftDown), Is.True);
        Assert.That(
            leftDown,
            Is.EqualTo(new LinuxEvdevGamepadButtonState.ButtonTransition(GamepadButtonValue.DpadLeft, true)));
        Assert.That(state.TryUpdateHat(0x0010, -1, out var duplicateLeft), Is.False);
        Assert.That(duplicateLeft, Is.Empty);
        Assert.That(state.TryUpdateButton(0x0222, isDown: false, out _), Is.False);
        Assert.That(state.GetPressedButtons(), Does.Contain(GamepadButtonValue.DpadLeft));
        Assert.That(state.TryUpdateHat(0x0010, 0, out var leftUp), Is.True);
        Assert.That(leftUp, Has.Length.EqualTo(1));
        Assert.That(
            leftUp[0],
            Is.EqualTo(new LinuxEvdevGamepadButtonState.ButtonTransition(GamepadButtonValue.DpadLeft, false)));
    }

    [Test]
    public void EmitsBothDirectionsWhenHatMovesDiagonally()
    {
        var state = new LinuxEvdevGamepadButtonState();

        Assert.That(state.TryUpdateHat(0x0010, -1, out var left), Is.True);
        Assert.That(left, Has.Length.EqualTo(1));
        Assert.That(
            left[0],
            Is.EqualTo(new LinuxEvdevGamepadButtonState.ButtonTransition(GamepadButtonValue.DpadLeft, true)));
        Assert.That(state.TryUpdateHat(0x0011, -1, out var up), Is.True);
        Assert.That(up, Has.Length.EqualTo(1));
        Assert.That(
            up[0],
            Is.EqualTo(new LinuxEvdevGamepadButtonState.ButtonTransition(GamepadButtonValue.DpadUp, true)));
        var pressedButtons = state.GetPressedButtons();
        Assert.That(pressedButtons, Has.Length.EqualTo(2));
        Assert.That(pressedButtons[0], Is.EqualTo(GamepadButtonValue.DpadUp));
        Assert.That(pressedButtons[1], Is.EqualTo(GamepadButtonValue.DpadLeft));
    }

    [TestCase((ushort)0x0130, GamepadButtonValue.South)]
    [TestCase((ushort)0x0131, GamepadButtonValue.East)]
    [TestCase((ushort)0x0133, GamepadButtonValue.North)]
    [TestCase((ushort)0x0134, GamepadButtonValue.West)]
    [TestCase((ushort)0x0136, GamepadButtonValue.LeftBumper)]
    [TestCase((ushort)0x0137, GamepadButtonValue.RightBumper)]
    [TestCase((ushort)0x013A, GamepadButtonValue.Menu)]
    [TestCase((ushort)0x013B, GamepadButtonValue.Start)]
    [TestCase((ushort)0x013C, GamepadButtonValue.System)]
    public void MapsStandardButtonCodes(ushort code, GamepadButtonValue expectedButton)
    {
        var state = new LinuxEvdevGamepadButtonState();

        Assert.That(state.TryUpdateButton(code, isDown: true, out var transition), Is.True);
        Assert.That(transition.Button, Is.EqualTo(expectedButton));
        Assert.That(transition.IsDown, Is.True);
        Assert.That(state.GetPressedButtons(), Does.Contain(expectedButton));
    }

    [Test]
    public void RejectsHatValuesOutsideEvdevRange()
    {
        var state = new LinuxEvdevGamepadButtonState();

        _ = Assert.Throws<InvalidDataException>(() => state.TryUpdateHat(0x0010, 2, out _));
        _ = Assert.Throws<InvalidDataException>(() => state.TryUpdateHat(0x0011, -2, out _));
    }

    [Test]
    public void ResetsPressedControlsForDeviceRetirement()
    {
        var state = new LinuxEvdevGamepadButtonState();
        _ = state.TryUpdateButton(0x0130, isDown: true, out _);
        _ = state.TryUpdateHat(0x0010, 1, out _);

        var releases = state.Reset();

        Assert.That(releases, Has.Length.EqualTo(2));
        Assert.That(
            releases[0],
            Is.EqualTo(new LinuxEvdevGamepadButtonState.ButtonTransition(GamepadButtonValue.South, false)));
        Assert.That(
            releases[1],
            Is.EqualTo(new LinuxEvdevGamepadButtonState.ButtonTransition(GamepadButtonValue.DpadRight, false)));
        Assert.That(state.GetPressedButtons(), Is.Empty);
        Assert.That(state.Reset(), Is.Empty);
    }
}
