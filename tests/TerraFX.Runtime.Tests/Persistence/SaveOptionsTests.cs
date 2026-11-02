// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Hosting;

namespace TerraFX.Runtime.Tests.Persistence;

[TestFixture]
internal sealed class SaveOptionsTests
{
    [Test]
    public void RejectsRelativeOrUndefinedSaveOptions()
    {
        _ = Assert.Throws<ArgumentException>(() => new SaveOptions(string.Empty));
        _ = Assert.Throws<ArgumentException>(() => new SaveOptions("world.tfx"));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SaveOptions(@"C:\saves\world.tfx", SaveDurabilityPolicy.None));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SaveOptions(@"C:\saves\world.tfx", (SaveDurabilityPolicy)3));
    }

    [Test]
    public void RequiresAbsoluteSavePathAndPreservesSelectedDurabilityPolicy()
    {
        var required = new SaveOptions(@"C:\saves\world.tfx");
        var bestEffort = new SaveOptions(@"C:\saves\world.tfx", SaveDurabilityPolicy.BestEffort);

        Assert.That(required.AbsoluteSavePath, Is.EqualTo(@"C:\saves\world.tfx"));
        Assert.That(required.DurabilityPolicy, Is.EqualTo(SaveDurabilityPolicy.RequireDurable));
        Assert.That(bestEffort.DurabilityPolicy, Is.EqualTo(SaveDurabilityPolicy.BestEffort));
    }
}
