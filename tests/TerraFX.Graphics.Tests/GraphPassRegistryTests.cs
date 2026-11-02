// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphPassRegistryTests
{
    [Test]
    public void AddPassIssuesGraphScopedSequentialIdsAndPreservesPassDescriptions()
    {
        var registry = new GraphPassRegistry(9);
        var first = registry.AddPass(new RenderPassDescription("first", 1, [], false));
        var second = registry.AddPass(new RenderPassDescription("second", -1, [first], true));

        Assert.That(first.IsValid, Is.True);
        Assert.That(first.Value, Is.Zero);
        Assert.That(second.IsValid, Is.True);
        Assert.That(second.Value, Is.EqualTo(1));
        Assert.That(registry.IsRegistered(first), Is.True);
        Assert.That(registry.IsRegistered(second), Is.True);
        Assert.That(registry.IsRegistered(new RenderPassId(9, 2)), Is.False);
        Assert.That(registry.IsRegistered(new RenderPassId(10, 0)), Is.False);
        Assert.That(registry.Passes, Has.Length.EqualTo(2));
        Assert.That(registry.Passes[1].Description.Predecessors, Is.EqualTo(ImmutableArray.Create(first)));
    }

    [Test]
    public void AddPassRejectsForeignAndUnregisteredPredecessors()
    {
        var registry = new GraphPassRegistry(9);
        var foreignPass = new RenderPassId(10, 0);
        var missingPass = new RenderPassId(9, 0);

        _ = Assert.Throws<ArgumentException>(
            () => registry.AddPass(new RenderPassDescription("foreign", 0, [foreignPass], false)));
        _ = Assert.Throws<ArgumentException>(
            () => registry.AddPass(new RenderPassDescription("missing", 0, [missingPass], false)));
    }

    [Test]
    public void ConstructorRejectsDefaultGraphOwnerIdentity()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphPassRegistry(0));
    }
}
