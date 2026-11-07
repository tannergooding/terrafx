// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class XkbKeyNameMapperTests
{
    [TestCase("AC01", (ushort)0x04)]
    [TestCase("AE01", (ushort)0x1E)]
    [TestCase("LCTL", (ushort)0xE0)]
    [TestCase("KP7", (ushort)0x5F)]
    [TestCase("FK12", (ushort)0x45)]
    public void MapsPhysicalXkbKeyNamesToHidUsages(string keyName, ushort expectedHidUsage)
    {
        Assert.That(XkbKeyNameMapper.TryMap(keyName, out var hidUsage), Is.True);
        Assert.That(hidUsage, Is.EqualTo(expectedHidUsage));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("ac01")]
    [TestCase("UNKNOWN")]
    public void RejectsUnknownXkbKeyNames(string? keyName)
    {
        Assert.That(XkbKeyNameMapper.TryMap(keyName, out var hidUsage), Is.False);
        Assert.That(hidUsage, Is.Zero);
    }
}
