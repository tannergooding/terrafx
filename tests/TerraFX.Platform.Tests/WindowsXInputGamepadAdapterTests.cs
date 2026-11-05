// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Versioning;
using NUnit.Framework;
using TerraFX.Input;

namespace TerraFX.Platform.Tests;

[TestFixture]
[SupportedOSPlatform("windows10.0.22000")]
internal sealed class WindowsXInputGamepadAdapterTests
{
    [Test]
    public void PollsConnectedChangesAndReusesDeviceIdentityAfterReconnect()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The Windows XInput adapter targets Windows 11.");
        }

        var states = new WindowsXInputGamepadAdapter.NativeState[4];
        var results = new uint[4];
        Array.Fill(results, 1167u);
        results[0] = 0;
        states[0].Gamepad = new WindowsXInputGamepadAdapter.NativeGamepad {
            Buttons = 0x1000,
            LeftTrigger = byte.MaxValue,
            ThumbLX = short.MinValue,
            ThumbRY = short.MaxValue,
        };
        var samples = new List<PlatformInputSample>();

        uint Query(uint userIndex, out WindowsXInputGamepadAdapter.NativeState state)
        {
            state = states[checked((int)userIndex)];
            return results[checked((int)userIndex)];
        }

        var adapter = new WindowsXInputGamepadAdapter(samples.Add, TimeProvider.System, Query);
        adapter.Pump();

        Assert.That(samples.Count, Is.EqualTo(7));
        Assert.That(samples[0].Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(samples[0].DeviceKind, Is.EqualTo(PlatformInputDeviceKind.Gamepad));
        Assert.That(samples[1].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadButtonDown));
        Assert.That(samples[1].GamepadButton, Is.EqualTo(GamepadButtonValue.South));
        Assert.That(samples[2].Axis, Is.EqualTo(GamepadAxis.LeftX));
        Assert.That(samples[2].AxisValue, Is.EqualTo(-1.0f));
        Assert.That(samples[3].Axis, Is.EqualTo(GamepadAxis.RightY));
        Assert.That(samples[3].AxisValue, Is.EqualTo(1.0f));
        Assert.That(samples[4].Stick, Is.EqualTo(GamepadStick.Left));
        Assert.That(samples[4].StickValue!.Value.X, Is.EqualTo(-1.0f));
        Assert.That(samples[5].Stick, Is.EqualTo(GamepadStick.Right));
        Assert.That(samples[5].StickValue!.Value.Y, Is.EqualTo(1.0f));
        Assert.That(samples[6].Trigger, Is.EqualTo(GamepadTrigger.Left));
        Assert.That(samples[6].TriggerValue, Is.EqualTo(1.0f));
        var deviceIdentity = samples[0].NativeDeviceIdentity;

        samples.Clear();
        states[0].Gamepad = default;
        adapter.Pump();

        Assert.That(samples.Count, Is.EqualTo(6));
        Assert.That(samples[0].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadButtonUp));
        Assert.That(samples[0].GamepadButton, Is.EqualTo(GamepadButtonValue.South));
        Assert.That(samples[1].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadAxis));
        Assert.That(samples[2].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadAxis));
        Assert.That(samples[3].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadStick));
        Assert.That(samples[4].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadStick));
        Assert.That(samples[5].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadTrigger));

        samples.Clear();
        results[0] = 1167;
        adapter.Pump();

        Assert.That(samples.Count, Is.EqualTo(1));
        Assert.That(samples[0].Kind, Is.EqualTo(PhysicalInputEventKind.DeviceRemoved));
        Assert.That(samples[0].NativeDeviceIdentity, Is.EqualTo(deviceIdentity));

        samples.Clear();
        results[0] = 0;
        states[0].Gamepad = new WindowsXInputGamepadAdapter.NativeGamepad { Buttons = 0x1000 };
        adapter.Pump();

        Assert.That(samples[0].Kind, Is.EqualTo(PhysicalInputEventKind.DeviceAdded));
        Assert.That(samples[0].NativeDeviceIdentity, Is.EqualTo(deviceIdentity));
        Assert.That(samples[1].Kind, Is.EqualTo(PhysicalInputEventKind.GamepadButtonDown));
    }

    [Test]
    public void PropagatesUnexpectedXInputStatus()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The Windows XInput adapter targets Windows 11.");
        }

        static uint Query(uint userIndex, out WindowsXInputGamepadAdapter.NativeState state)
        {
            _ = userIndex;
            state = default;
            return 5;
        }

        var adapter = new WindowsXInputGamepadAdapter(
            static _ => { },
            TimeProvider.System,
            Query);

        Assert.That(adapter.Pump, Throws.TypeOf<Win32Exception>());
    }
}
