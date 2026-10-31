// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class InputInjectionTests
{
    [Test]
    public void ConstructorsPreserveEachNormalizedInjectionShape()
    {
        var key = new KeyboardControl(7, 80);
        var keyInjection = new InputInjection(key, InputKeyEventKind.Down);
        Assert.That(keyInjection.Kind, Is.EqualTo(InputInjectionKind.KeyboardKey));
        Assert.That(keyInjection.Key, Is.SameAs(key));
        Assert.That(keyInjection.KeyEventKind, Is.EqualTo(InputKeyEventKind.Down));
        Assert.That(keyInjection.PlayerSlot, Is.Null);

        var buttonInjection = new InputInjection(2, GamepadButtonValue.South, isDown: true);
        Assert.That(buttonInjection.Kind, Is.EqualTo(InputInjectionKind.GamepadButton));
        Assert.That(buttonInjection.PlayerSlot, Is.EqualTo(2));
        Assert.That(buttonInjection.Button, Is.EqualTo(GamepadButtonValue.South));
        Assert.That(buttonInjection.IsDown, Is.True);

        var axisInjection = new InputInjection(3, GamepadAxis.LeftX, -1.0f);
        Assert.That(axisInjection.Kind, Is.EqualTo(InputInjectionKind.GamepadAxis));
        Assert.That(axisInjection.PlayerSlot, Is.EqualTo(3));
        Assert.That(axisInjection.Axis, Is.EqualTo(GamepadAxis.LeftX));
        Assert.That(axisInjection.AxisValue, Is.EqualTo(-1.0f));

        var stickInjection = new InputInjection(4, GamepadStick.Right, new Vector2(0.6f, 0.7f));
        Assert.That(stickInjection.Kind, Is.EqualTo(InputInjectionKind.GamepadStick));
        Assert.That(stickInjection.PlayerSlot, Is.EqualTo(4));
        Assert.That(stickInjection.Stick, Is.EqualTo(GamepadStick.Right));
        Assert.That(stickInjection.StickValue, Is.EqualTo(new Vector2(0.6f, 0.7f)));

        var triggerInjection = new InputInjection(5, GamepadTrigger.Left, 1.0f);
        Assert.That(triggerInjection.Kind, Is.EqualTo(InputInjectionKind.GamepadTrigger));
        Assert.That(triggerInjection.PlayerSlot, Is.EqualTo(5));
        Assert.That(triggerInjection.Trigger, Is.EqualTo(GamepadTrigger.Left));
        Assert.That(triggerInjection.TriggerValue, Is.EqualTo(1.0f));
    }

    [Test]
    public void RejectsUnknownEnumsAndOutOfRangeNormalizedValues()
    {
        _ = Assert.Throws<ArgumentException>(() => new InputInjection(new KeyboardControl(string.Empty), InputKeyEventKind.Down));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(new KeyboardControl(7, 80), (InputKeyEventKind)int.MaxValue));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, (GamepadButtonValue)int.MaxValue, true));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, (GamepadAxis)int.MaxValue, 0.0f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, (GamepadStick)int.MaxValue, Vector2.Zero));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, (GamepadTrigger)int.MaxValue, 0.0f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, GamepadAxis.LeftX, float.NaN));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, GamepadAxis.LeftX, -1.01f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, GamepadAxis.LeftX, 1.01f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, GamepadStick.Left, new Vector2(float.PositiveInfinity, 0.0f)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, GamepadStick.Left, new Vector2(1.0f, 1.0f)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, GamepadTrigger.Left, float.NaN));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, GamepadTrigger.Left, -0.01f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new InputInjection(0, GamepadTrigger.Left, 1.01f));
    }
}
