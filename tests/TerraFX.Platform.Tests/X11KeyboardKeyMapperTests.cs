// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class X11KeyboardKeyMapperTests
{
    [TestCase(38u, 30u, (ushort)0x04)]
    [TestCase(9u, 1u, (ushort)0x29)]
    [TestCase(37u, 29u, (ushort)0xE0)]
    [TestCase(90u, 82u, (ushort)0x62)]
    public void MapsEvdevKeycodesToHidUsages(uint xKeycode, uint expectedEvdevKeycode, ushort expectedHidUsage)
    {
        Assert.That(X11KeyboardKeyMapper.TryMap(xKeycode, out var evdevKeycode, out var hidUsage), Is.True);
        Assert.That(evdevKeycode, Is.EqualTo(expectedEvdevKeycode));
        Assert.That(hidUsage, Is.EqualTo(expectedHidUsage));
    }

    [TestCase(7u)]
    [TestCase(8u)]
    [TestCase(255u)]
    [TestCase(256u)]
    public void RejectsReservedOrUnmappedKeycodes(uint xKeycode)
    {
        var mapped = X11KeyboardKeyMapper.TryMap(xKeycode, out var evdevKeycode, out var hidUsage);

        Assert.That(mapped, Is.False);
        Assert.That(evdevKeycode, Is.EqualTo((xKeycode is >= 8 and <= 255) ? xKeycode - 8 : 0));
        Assert.That(hidUsage, Is.Zero);
    }
}
