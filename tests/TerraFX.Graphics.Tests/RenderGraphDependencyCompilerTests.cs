// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class RenderGraphDependencyCompilerTests
{
    [Test]
    public void TryOrderRejectsCyclesWithoutReturningAnOrder()
    {
        var first = new RenderPassId(1, 0);
        var second = new RenderPassId(1, 1);
        var passes = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [second], false)),
            new RenderGraphPassNode(second, new RenderPassDescription("second", 0, [first], false)));

        Assert.That(RenderGraphDependencyCompiler.TryOrder(passes, out var orderedPasses), Is.False);
        Assert.That(orderedPasses, Is.Empty);
    }

    [Test]
    public void TryOrderRejectsDuplicateIdsAndMixedGraphOwnership()
    {
        var first = new RenderPassId(1, 0);
        var duplicateIds = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [], false)),
            new RenderGraphPassNode(first, new RenderPassDescription("second", 0, [], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(duplicateIds, out _));

        var mixedOwners = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [], false)),
            new RenderGraphPassNode(new RenderPassId(2, 0), new RenderPassDescription("second", 0, [], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(mixedOwners, out _));
    }

    [Test]
    public void TryOrderRejectsForeignMissingAndDuplicatePredecessors()
    {
        var first = new RenderPassId(1, 0);
        var foreign = new RenderPassId(2, 0);

        var foreignPass = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [foreign], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(foreignPass, out _));

        var second = new RenderPassId(1, 1);
        var missingPass = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [], false)),
            new RenderGraphPassNode(second, new RenderPassDescription("second", 0, [new RenderPassId(1, 2)], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(missingPass, out _));

        var duplicatePredecessorPass = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [], false)),
            new RenderGraphPassNode(second, new RenderPassDescription("second", 0, [first, first], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(duplicatePredecessorPass, out _));
    }

    [Test]
    public void TryOrderUsesDeclaredOrderThenOrdinalNameWithinReadyPasses()
    {
        var preceding = new RenderPassId(7, 0);
        var dependent = new RenderPassId(7, 1);
        var independent = new RenderPassId(7, 2);
        var passes = ImmutableArray.Create(
            new RenderGraphPassNode(dependent, new RenderPassDescription("z-dependent", 0, [preceding], false)),
            new RenderGraphPassNode(independent, new RenderPassDescription("a-independent", 0, [], false)),
            new RenderGraphPassNode(preceding, new RenderPassDescription("late", -1, [], false)));

        Assert.That(RenderGraphDependencyCompiler.TryOrder(passes, out var orderedPasses), Is.True);
        Assert.That(orderedPasses, Is.EqualTo(ImmutableArray.Create(preceding, independent, dependent)));
    }
}
