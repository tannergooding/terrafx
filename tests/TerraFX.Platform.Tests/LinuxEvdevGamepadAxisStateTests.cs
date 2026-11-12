// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class LinuxEvdevGamepadAxisStateTests
{
    [Test]
    public void NormalizesRoundedDiagonalSticksToUnitDisk()
    {
        var state = new LinuxEvdevGamepadAxisState([
            (0x0000, new LinuxEvdevAxisInfo(-1_000_000, 1_000_000, Fuzz: 0, Flat: 0), 949_356),
            (0x0001, new LinuxEvdevAxisInfo(-1_000_000, 1_000_000, Fuzz: 0, Flat: 0), 704_826),
        ]);
        var leftStick = state.GetCurrentState()[4].StickValue
            ?? throw new InvalidOperationException("The initial left-stick state has no value.");

        Assert.That(TerraFX.Numerics.Vector2.LengthSquared(leftStick), Is.LessThanOrEqualTo(1.0f));
    }

    [Test]
    public void NormalizesStandardStickAndTriggerAxes()
    {
        var state = new LinuxEvdevGamepadAxisState([
            (0x0000, new LinuxEvdevAxisInfo(-100, 100, Fuzz: 0, Flat: 0), 0),
            (0x0001, new LinuxEvdevAxisInfo(-100, 100, Fuzz: 0, Flat: 0), 0),
            (0x0002, new LinuxEvdevAxisInfo(0, 100, Fuzz: 0, Flat: 0), 0),
            (0x0003, new LinuxEvdevAxisInfo(-100, 100, Fuzz: 0, Flat: 0), 0),
            (0x0004, new LinuxEvdevAxisInfo(-100, 100, Fuzz: 0, Flat: 0), 0),
            (0x0005, new LinuxEvdevAxisInfo(0, 100, Fuzz: 0, Flat: 0), 0),
        ]);

        var initialState = state.GetCurrentState();
        Assert.That(initialState, Has.Length.EqualTo(8));
        Assert.That(initialState[0].Axis, Is.EqualTo(GamepadAxis.LeftX));
        Assert.That(initialState[0].AxisValue, Is.Zero);
        Assert.That(initialState[1].Axis, Is.EqualTo(GamepadAxis.LeftY));
        Assert.That(initialState[1].AxisValue, Is.Zero);
        Assert.That(initialState[2].Axis, Is.EqualTo(GamepadAxis.RightX));
        Assert.That(initialState[2].AxisValue, Is.Zero);
        Assert.That(initialState[3].Axis, Is.EqualTo(GamepadAxis.RightY));
        Assert.That(initialState[3].AxisValue, Is.Zero);
        Assert.That(initialState[4].Stick, Is.EqualTo(GamepadStick.Left));
        var initialLeftStick = initialState[4].StickValue
            ?? throw new InvalidOperationException("The initial left-stick state has no value.");
        Assert.That(initialLeftStick.X, Is.Zero);
        Assert.That(initialLeftStick.Y, Is.Zero);
        Assert.That(initialState[5].Stick, Is.EqualTo(GamepadStick.Right));
        var initialRightStick = initialState[5].StickValue
            ?? throw new InvalidOperationException("The initial right-stick state has no value.");
        Assert.That(initialRightStick.X, Is.Zero);
        Assert.That(initialRightStick.Y, Is.Zero);
        Assert.That(initialState[6].Trigger, Is.EqualTo(GamepadTrigger.Left));
        Assert.That(initialState[6].TriggerValue, Is.Zero);
        Assert.That(initialState[7].Trigger, Is.EqualTo(GamepadTrigger.Right));
        Assert.That(initialState[7].TriggerValue, Is.Zero);

        Assert.That(state.TryUpdate(0x0000, 100, out var leftXChanges), Is.True);
        Assert.That(leftXChanges, Has.Length.EqualTo(2));
        Assert.That(leftXChanges[0].Axis, Is.EqualTo(GamepadAxis.LeftX));
        Assert.That(leftXChanges[0].AxisValue, Is.EqualTo(1.0f));
        Assert.That(leftXChanges[1].Stick, Is.EqualTo(GamepadStick.Left));
        var horizontalStick = leftXChanges[1].StickValue
            ?? throw new InvalidOperationException("The left-stick update has no value.");
        Assert.That(horizontalStick.X, Is.EqualTo(1.0f));
        Assert.That(horizontalStick.Y, Is.Zero);

        Assert.That(state.TryUpdate(0x0001, -100, out var leftYChanges), Is.True);
        Assert.That(leftYChanges, Has.Length.EqualTo(2));
        Assert.That(leftYChanges[0].Axis, Is.EqualTo(GamepadAxis.LeftY));
        Assert.That(leftYChanges[0].AxisValue, Is.EqualTo(1.0f));
        var diagonalStick = leftYChanges[1].StickValue
            ?? throw new InvalidOperationException("The diagonal stick update has no value.");
        Assert.That(diagonalStick.X, Is.EqualTo(MathF.Sqrt(0.5f)).Within(1e-6f));
        Assert.That(diagonalStick.Y, Is.EqualTo(MathF.Sqrt(0.5f)).Within(1e-6f));

        Assert.That(state.TryUpdate(0x0003, -100, out var rightXChanges), Is.True);
        Assert.That(rightXChanges[0].Axis, Is.EqualTo(GamepadAxis.RightX));
        Assert.That(rightXChanges[0].AxisValue, Is.EqualTo(-1.0f));
        var horizontalRightStick = rightXChanges[1].StickValue
            ?? throw new InvalidOperationException("The right-stick update has no value.");
        Assert.That(horizontalRightStick.X, Is.EqualTo(-1.0f));
        Assert.That(horizontalRightStick.Y, Is.Zero);

        Assert.That(state.TryUpdate(0x0004, 100, out var rightYChanges), Is.True);
        Assert.That(rightYChanges[0].Axis, Is.EqualTo(GamepadAxis.RightY));
        Assert.That(rightYChanges[0].AxisValue, Is.EqualTo(-1.0f));
        var diagonalRightStick = rightYChanges[1].StickValue
            ?? throw new InvalidOperationException("The diagonal right-stick update has no value.");
        Assert.That(diagonalRightStick.X, Is.EqualTo(-MathF.Sqrt(0.5f)).Within(1e-6f));
        Assert.That(diagonalRightStick.Y, Is.EqualTo(-MathF.Sqrt(0.5f)).Within(1e-6f));

        Assert.That(state.TryUpdate(0x0002, 100, out var leftTriggerChanges), Is.True);
        Assert.That(leftTriggerChanges, Has.Length.EqualTo(1));
        Assert.That(leftTriggerChanges[0].Trigger, Is.EqualTo(GamepadTrigger.Left));
        Assert.That(leftTriggerChanges[0].TriggerValue, Is.EqualTo(1.0f));

        Assert.That(state.TryUpdate(0x0005, 50, out var rightTriggerChanges), Is.True);
        Assert.That(rightTriggerChanges, Has.Length.EqualTo(1));
        Assert.That(rightTriggerChanges[0].Trigger, Is.EqualTo(GamepadTrigger.Right));
        Assert.That(rightTriggerChanges[0].TriggerValue, Is.EqualTo(0.5f));
    }

    [Test]
    public void RejectsUnsupportedDuplicateAndInvalidAxisDescriptors()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new LinuxEvdevGamepadAxisState([
                (0x0010, new LinuxEvdevAxisInfo(-1, 1, Fuzz: 0, Flat: 0), 0),
            ]));

        _ = Assert.Throws<ArgumentException>(
            () => new LinuxEvdevGamepadAxisState([
                (0x0000, new LinuxEvdevAxisInfo(-1, 1, Fuzz: 0, Flat: 0), 0),
                (0x0000, new LinuxEvdevAxisInfo(-1, 1, Fuzz: 0, Flat: 0), 0),
            ]));

        _ = Assert.Throws<InvalidDataException>(
            () => new LinuxEvdevGamepadAxisState([
                (0x0000, new LinuxEvdevAxisInfo(-1, 1, Fuzz: 0, Flat: 0), 2),
            ]));

        var state = new LinuxEvdevGamepadAxisState([]);
        Assert.That(state.TryUpdate(0x0010, 1, out var ignoredHat), Is.False);
        Assert.That(ignoredHat, Is.Empty);
    }

    [Test]
    public void SuppressesFuzzAgainstTheLastAcceptedRawValue()
    {
        var state = new LinuxEvdevGamepadAxisState([
            (0x0000, new LinuxEvdevAxisInfo(-100, 100, Fuzz: 5, Flat: 0), 0),
        ]);

        Assert.That(state.TryUpdate(0x0000, 5, out var firstNoise), Is.False);
        Assert.That(firstNoise, Is.Empty);
        Assert.That(state.TryUpdate(0x0000, 6, out var acceptedChange), Is.True);
        Assert.That(acceptedChange[0].AxisValue, Is.EqualTo(0.06f));
        Assert.That(state.TryUpdate(0x0000, 10, out var secondNoise), Is.False);
        Assert.That(secondNoise, Is.Empty);
        Assert.That(state.TryUpdate(0x0000, 12, out var secondAcceptedChange), Is.True);
        Assert.That(secondAcceptedChange[0].AxisValue, Is.EqualTo(0.12f));
    }
}
