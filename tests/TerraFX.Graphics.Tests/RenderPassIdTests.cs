// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class RenderPassIdTests
{
    [Test]
    public void PassIdIsGraphScopedAndUsesZeroBasedOrdinals()
    {
        var first = new RenderPassId(7, 0);
        var same = new RenderPassId(7, 0);
        var otherGraph = new RenderPassId(8, 0);
        var otherPass = new RenderPassId(7, 1);

        Assert.That(default(RenderPassId).IsValid, Is.False);
        Assert.That(first.IsValid, Is.True);
        Assert.That(first.Value, Is.Zero);
        Assert.That(first, Is.EqualTo(same));
        Assert.That(first, Is.Not.EqualTo(otherGraph));
        Assert.That(first, Is.Not.EqualTo(otherPass));
    }

    [Test]
    public void PassIdRejectsInvalidOwnerAndOrdinal()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RenderPassId(0, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RenderPassId(1, -1));
    }
}
