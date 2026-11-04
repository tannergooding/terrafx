// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX.Hosting;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class SaveOptionsTests
{
    [Test]
    public void ModeDefaultsToStandardAndAcceptsStrictLocal()
    {
        var path = Path.GetFullPath("state2.epkg");
        var standard = new SaveOptions(path);
        var strictLocal = new SaveOptions(path) {
            Mode = WorldPersistenceMode.StrictLocal,
        };

        Assert.Multiple(() => {
            Assert.That(standard.Mode, Is.EqualTo(WorldPersistenceMode.Standard));
            Assert.That(strictLocal.Mode, Is.EqualTo(WorldPersistenceMode.StrictLocal));
        });
    }

    [Test]
    public void RejectsUnknownMode()
    {
        var path = Path.GetFullPath("state2.epkg");

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SaveOptions(path) {
            Mode = (WorldPersistenceMode)2,
        });
    }
}
