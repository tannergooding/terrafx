// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class RenderPassDescriptionTests
{
    [Test]
    public void DescriptionPreservesStableNameOrderPredecessorsAndSideEffects()
    {
        var predecessor = new RenderPassId(4, 2);
        var predecessors = ImmutableArray.Create(predecessor);
        var description = new RenderPassDescription("resolve", -3, predecessors, hasExternalSideEffects: true);

        Assert.That(description.Name, Is.EqualTo("resolve"));
        Assert.That(description.Order, Is.EqualTo(-3));
        Assert.That(description.Predecessors, Is.EqualTo(predecessors));
        Assert.That(description.HasExternalSideEffects, Is.True);
    }

    [Test]
    public void DescriptionRejectsEmptyNameDefaultPredecessorsAndInvalidIds()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new RenderPassDescription(null!, 0, [], false));
        _ = Assert.Throws<ArgumentException>(() => new RenderPassDescription(string.Empty, 0, [], false));
        _ = Assert.Throws<ArgumentException>(() => new RenderPassDescription("pass", 0, default, false));
        _ = Assert.Throws<ArgumentException>(() => new RenderPassDescription("pass", 0, [default], false));
    }
}
