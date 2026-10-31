// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;
using TerraFX;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class MonitorHandleTests
{
    [Test]
    public void DefaultIsInvalidAndIssuedIdentityIncludesGeneration()
    {
        var first = new MonitorHandle(1, 0, 1);
        var same = new MonitorHandle(1, 0, 1);

        Assert.That(default(MonitorHandle).IsValid, Is.False);
        Assert.That(first.IsValid, Is.True);
        Assert.That(first, Is.EqualTo(same));
        Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
        Assert.That(first, Is.Not.EqualTo(new MonitorHandle(2, 0, 1)));
        Assert.That(first, Is.Not.EqualTo(new MonitorHandle(1, 1, 1)));
        Assert.That(first, Is.Not.EqualTo(new MonitorHandle(1, 0, 2)));
    }

    [Test]
    public void IssuedIdentityRequiresOwnerAndGeneration()
    {
        Assert.That(() => new MonitorHandle(0, 0, 1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        Assert.That(() => new MonitorHandle(1, 0, 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
    }
}
