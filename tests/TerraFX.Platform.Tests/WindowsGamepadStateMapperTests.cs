// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.Versioning;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
[SupportedOSPlatform("windows")]
internal sealed class WindowsGamepadStateMapperTests
{
    [Test]
    public void MapsStateToPhysicalPositionAndCompleteReconciliation()
    {
        var state = new PlatformGamepadState(
            Buttons: 0x0000_0105,
            LeftTrigger: 0.25f,
            RightTrigger: 0.75f,
            LeftThumbstickX: -0.5f,
            LeftThumbstickY: 0.5f,
            RightThumbstickX: 0.25f,
            RightThumbstickY: -0.25f);
        var values = WindowsGamepadStateMapper.CreateState(state);

        Assert.That(values.Length, Is.EqualTo(20));
        Assert.That(values[0].GamepadButton, Is.EqualTo(TerraFX.Input.GamepadButtonValue.South));
        Assert.That(values[0].IsDown, Is.True);
        Assert.That(values[6].GamepadButton, Is.EqualTo(TerraFX.Input.GamepadButtonValue.DpadLeft));
        Assert.That(values[6].IsDown, Is.True);
        Assert.That(values[8].GamepadButton, Is.EqualTo(TerraFX.Input.GamepadButtonValue.Menu));
        Assert.That(values[8].IsDown, Is.True);
        Assert.That(values[12].Axis, Is.EqualTo(TerraFX.Input.GamepadAxis.LeftX));
        Assert.That(values[12].AxisValue, Is.EqualTo(-0.5f));
        Assert.That(values[16].Stick, Is.EqualTo(TerraFX.Input.GamepadStick.Left));
        Assert.That(values[16].StickValue?.X, Is.EqualTo(-0.5f));
        Assert.That(values[18].Trigger, Is.EqualTo(TerraFX.Input.GamepadTrigger.Left));
        Assert.That(values[18].TriggerValue, Is.EqualTo(0.25f));
    }

    [Test]
    public void RejectsNonNormalizedGamepadValues()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => WindowsGamepadStateMapper.CreateState(new PlatformGamepadState(
                0,
                0.0f,
                0.0f,
                float.NaN,
                0.0f,
                0.0f,
                0.0f)));
        _ = Assert.Throws<InvalidDataException>(
            () => WindowsGamepadStateMapper.CreateState(new PlatformGamepadState(
                0,
                0.0f,
                1.01f,
                0.0f,
                0.0f,
                0.0f,
                0.0f)));
    }

    [Test]
    public void ReportsOnlyChangedControls()
    {
        var previous = new PlatformGamepadState(0, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);
        var current = new PlatformGamepadState(0x0000_0004, 0.25f, 0.0f, -0.5f, 0.0f, 0.0f, 0.0f);
        var changes = WindowsGamepadStateMapper.GetChanges(previous, current);

        Assert.That(changes.Length, Is.EqualTo(4));
        Assert.That(changes[0].GamepadButton, Is.EqualTo(TerraFX.Input.GamepadButtonValue.South));
        Assert.That(changes[0].IsDown, Is.True);
        Assert.That(changes[1].Axis, Is.EqualTo(TerraFX.Input.GamepadAxis.LeftX));
        Assert.That(changes[2].Stick, Is.EqualTo(TerraFX.Input.GamepadStick.Left));
        Assert.That(changes[3].Trigger, Is.EqualTo(TerraFX.Input.GamepadTrigger.Left));
    }
}
