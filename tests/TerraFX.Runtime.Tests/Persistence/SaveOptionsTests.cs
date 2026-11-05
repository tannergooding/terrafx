// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX.Hosting;

namespace TerraFX.Runtime.Tests.Persistence;

[TestFixture]
internal sealed class SaveOptionsTests
{
    [Test]
    public void RejectsRelativeOrUndefinedSaveOptions()
    {
        var absoluteSavePath = Path.Combine(Path.GetTempPath(), "saves", "world.tfx");

        _ = Assert.Throws<ArgumentException>(() => new SaveOptions(string.Empty));
        _ = Assert.Throws<ArgumentException>(() => new SaveOptions("world.tfx"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SaveOptions(absoluteSavePath, SaveDurabilityPolicy.None));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SaveOptions(absoluteSavePath, (SaveDurabilityPolicy)3));
    }

    [Test]
    public void RequiresAbsoluteSavePathAndPreservesSelectedDurabilityPolicy()
    {
        var absoluteSavePath = Path.Combine(Path.GetTempPath(), "saves", "world.tfx");
        var required = new SaveOptions(absoluteSavePath);
        var bestEffort = new SaveOptions(absoluteSavePath, SaveDurabilityPolicy.BestEffort);

        Assert.That(required.AbsoluteSavePath, Is.EqualTo(absoluteSavePath));
        Assert.That(required.DurabilityPolicy, Is.EqualTo(SaveDurabilityPolicy.RequireDurable));
        Assert.That(bestEffort.DurabilityPolicy, Is.EqualTo(SaveDurabilityPolicy.BestEffort));
    }
}
