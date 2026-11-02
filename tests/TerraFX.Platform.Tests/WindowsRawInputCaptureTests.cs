// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WindowsRawInputCaptureTests
{
    [Test]
    public void MapsCommonWindowsScanCodesToPhysicalHidUsages()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The Windows Raw Input adapter targets Windows 11.");
        }

        Assert.That(WindowsRawInputDecoder.TryMapKeyboardUsage(0x1E, 0, out var letterA), Is.True);
        Assert.That(letterA, Is.EqualTo(0x04));
        Assert.That(WindowsRawInputDecoder.TryMapKeyboardUsage(0x02, 0, out var digitOne), Is.True);
        Assert.That(digitOne, Is.EqualTo(0x1E));
        Assert.That(WindowsRawInputDecoder.TryMapKeyboardUsage(0x1D, 0x0002, out var rightControl), Is.True);
        Assert.That(rightControl, Is.EqualTo(0xE4));
        Assert.That(WindowsRawInputDecoder.TryMapKeyboardUsage(0x48, 0x0002, out var upArrow), Is.True);
        Assert.That(upArrow, Is.EqualTo(0x52));
        Assert.That(WindowsRawInputDecoder.TryMapKeyboardUsage(0x7F, 0, out var unknown), Is.False);
        Assert.That(unknown, Is.Zero);
    }
}
