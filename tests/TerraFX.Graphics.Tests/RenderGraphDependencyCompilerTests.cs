// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class RenderGraphDependencyCompilerTests
{
    private static BufferDescription CreateBufferDescription()
        => new(64, BufferUsage.Storage, GpuMemoryKind.DeviceLocal, null, string.Empty);

    private static TextureDescription CreateTextureDescription()
        => new(TextureDimension.Dimension2D, 16, 16, 1, 2, 5, TextureFormat.Rgba8Unorm, TextureUsage.Storage | TextureUsage.Sampled, string.Empty);

    [Test]
    public void TryOrderAcceptsOnlyMatchingPriorVersionAttachmentLoads()
    {
        var passId = new RenderPassId(7, 0);
        var description = new TextureDescription(
            TextureDimension.Dimension2D,
            16,
            16,
            1,
            2,
            5,
            TextureFormat.Rgba8Unorm,
            TextureUsage.ColorAttachment,
            string.Empty);
        var fullRange = new GraphTextureRange(0, 2, 0, 1, TextureAspect.Color);
        var partialRange = new GraphTextureRange(1, 1, 0, 1, TextureAspect.Color);
        var previousVersion = new GraphTextureVersion(1, 4, 0, description, fullRange);
        var nextVersion = new GraphTextureVersion(1, 4, 1, description, fullRange);
        var passes = ImmutableArray.Create(
            new RenderGraphPassNode(passId, new RenderPassDescription("pass", 0, [], false)));
        var cases = new[] {
            (LoadRange: fullRange, WriteRange: fullRange, WriteVersion: nextVersion, ExpectedValid: true),
            (LoadRange: fullRange, WriteRange: partialRange, WriteVersion: nextVersion, ExpectedValid: false),
            (LoadRange: fullRange, WriteRange: fullRange, WriteVersion: previousVersion, ExpectedValid: false),
        };

        foreach (var (loadRange, writeRange, writeVersion, expectedValid) in cases)
        {
            var accesses = ImmutableArray.Create(
                new GraphResourceAccess(
                    passId,
                    previousVersion,
                    loadRange,
                    TextureUsage.ColorAttachment,
                    AttachmentLoadAction.Load,
                    AttachmentStoreAction.Store,
                    isWrite: false,
                    contentsValid: true),
                new GraphResourceAccess(
                    passId,
                    writeVersion,
                    writeRange,
                    TextureUsage.ColorAttachment,
                    AttachmentLoadAction.Load,
                    AttachmentStoreAction.Store,
                    isWrite: true,
                    contentsValid: true));

            Assert.That(
                RenderGraphDependencyCompiler.TryOrder(passes, accesses, out _, out var accessesValid),
                Is.True);
            Assert.That(accessesValid, Is.EqualTo(expectedValid));
        }
    }

    [Test]
    public void TryOrderAddsBufferReadAfterWriteDependency()
    {
        var writer = new RenderPassId(7, 0);
        var reader = new RenderPassId(7, 1);
        var description = CreateBufferDescription();
        var previousVersion = new GraphBufferVersion(1, 4, 0, description, new GraphBufferRange(0, 64));
        var writtenVersion = new GraphBufferVersion(1, 4, 1, description, previousVersion.Range);
        var passes = ImmutableArray.Create(
            new RenderGraphPassNode(reader, new RenderPassDescription("reader", -1, [], false)),
            new RenderGraphPassNode(writer, new RenderPassDescription("writer", 1, [], false)));
        var accesses = ImmutableArray.Create(
            new GraphResourceAccess(writer, writtenVersion, new GraphBufferRange(0, 16), GraphBufferUsage.StorageWrite, isWrite: true),
            new GraphResourceAccess(reader, writtenVersion, new GraphBufferRange(0, 16), GraphBufferUsage.StorageRead, isWrite: false));

        Assert.That(RenderGraphDependencyCompiler.TryOrder(passes, accesses, out var orderedPasses), Is.True);
        Assert.That(orderedPasses, Is.EqualTo(ImmutableArray.Create(writer, reader)));
    }

    [Test]
    public void TryOrderAddsBufferWriteAfterReadAndWriteAfterWriteDependencies()
    {
        var reader = new RenderPassId(7, 0);
        var firstWriter = new RenderPassId(7, 1);
        var secondWriter = new RenderPassId(7, 2);
        var description = CreateBufferDescription();
        var version0 = new GraphBufferVersion(1, 4, 0, description, new GraphBufferRange(0, 64));
        var version1 = new GraphBufferVersion(1, 4, 1, description, version0.Range);
        var version2 = new GraphBufferVersion(1, 4, 2, description, version0.Range);
        var passes = ImmutableArray.Create(
            new RenderGraphPassNode(secondWriter, new RenderPassDescription("second", -2, [], false)),
            new RenderGraphPassNode(reader, new RenderPassDescription("read-old", 1, [], false)),
            new RenderGraphPassNode(firstWriter, new RenderPassDescription("first", 0, [], false)));
        var accesses = ImmutableArray.Create(
            new GraphResourceAccess(reader, version0, new GraphBufferRange(0, 16), GraphBufferUsage.StorageRead, isWrite: false),
            new GraphResourceAccess(firstWriter, version1, new GraphBufferRange(0, 16), GraphBufferUsage.StorageWrite, isWrite: true),
            new GraphResourceAccess(secondWriter, version2, new GraphBufferRange(0, 16), GraphBufferUsage.StorageWrite, isWrite: true));

        Assert.That(RenderGraphDependencyCompiler.TryOrder(passes, accesses, out var orderedPasses), Is.True);
        Assert.That(orderedPasses, Is.EqualTo(ImmutableArray.Create(reader, firstWriter, secondWriter)));
    }

    [Test]
    public void TryOrderAddsTextureSubresourceReadAfterWriteDependency()
    {
        var writer = new RenderPassId(7, 0);
        var reader = new RenderPassId(7, 1);
        var description = CreateTextureDescription();
        var version = new GraphTextureVersion(
            1,
            4,
            1,
            description,
            new GraphTextureRange(0, 5, 0, 2, TextureAspect.Color));
        var passes = ImmutableArray.Create(
            new RenderGraphPassNode(reader, new RenderPassDescription("reader", -1, [], false)),
            new RenderGraphPassNode(writer, new RenderPassDescription("writer", 1, [], false)));
        var accesses = ImmutableArray.Create(
            new GraphResourceAccess(
                writer,
                version,
                new GraphTextureRange(0, 2, 0, 1, TextureAspect.Color),
                GraphTextureUsage.StorageWrite,
                isWrite: true),
            new GraphResourceAccess(
                reader,
                version,
                new GraphTextureRange(1, 1, 0, 1, TextureAspect.Color),
                GraphTextureUsage.SampledRead,
                isWrite: false));

        Assert.That(RenderGraphDependencyCompiler.TryOrder(passes, accesses, out var orderedPasses), Is.True);
        Assert.That(orderedPasses, Is.EqualTo(ImmutableArray.Create(writer, reader)));
    }

    [Test]
    public void TryOrderLeavesDisjointBufferRangesUnordered()
    {
        var reader = new RenderPassId(7, 0);
        var writer = new RenderPassId(7, 1);
        var description = CreateBufferDescription();
        var version0 = new GraphBufferVersion(1, 4, 0, description, new GraphBufferRange(0, 64));
        var version1 = new GraphBufferVersion(1, 4, 1, description, version0.Range);
        var passes = ImmutableArray.Create(
            new RenderGraphPassNode(writer, new RenderPassDescription("writer", 1, [], false)),
            new RenderGraphPassNode(reader, new RenderPassDescription("reader", -1, [], false)));
        var accesses = ImmutableArray.Create(
            new GraphResourceAccess(reader, version0, new GraphBufferRange(0, 16), GraphBufferUsage.StorageRead, isWrite: false),
            new GraphResourceAccess(writer, version1, new GraphBufferRange(16, 16), GraphBufferUsage.StorageWrite, isWrite: true));

        Assert.That(RenderGraphDependencyCompiler.TryOrder(passes, accesses, out var orderedPasses), Is.True);
        Assert.That(orderedPasses, Is.EqualTo(ImmutableArray.Create(reader, writer)));
    }

    [Test]
    public void TryOrderRejectsCyclesWithoutReturningAnOrder()
    {
        var first = new RenderPassId(1, 0);
        var second = new RenderPassId(1, 1);
        var passes = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [second], false)),
            new RenderGraphPassNode(second, new RenderPassDescription("second", 0, [first], false)));

        Assert.That(RenderGraphDependencyCompiler.TryOrder(passes, [], out var orderedPasses), Is.False);
        Assert.That(orderedPasses, Is.Empty);
    }

    [Test]
    public void TryOrderRejectsDuplicateIdsAndMixedGraphOwnership()
    {
        var first = new RenderPassId(1, 0);
        var duplicateIds = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [], false)),
            new RenderGraphPassNode(first, new RenderPassDescription("second", 0, [], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(duplicateIds, [], out _));

        var mixedOwners = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [], false)),
            new RenderGraphPassNode(new RenderPassId(2, 0), new RenderPassDescription("second", 0, [], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(mixedOwners, [], out _));
    }

    [Test]
    public void TryOrderRejectsForeignMissingAndDuplicatePredecessors()
    {
        var first = new RenderPassId(1, 0);
        var foreign = new RenderPassId(2, 0);

        var foreignPass = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [foreign], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(foreignPass, [], out _));

        var second = new RenderPassId(1, 1);
        var missingPass = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [], false)),
            new RenderGraphPassNode(second, new RenderPassDescription("second", 0, [new RenderPassId(1, 2)], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(missingPass, [], out _));

        var duplicatePredecessorPass = ImmutableArray.Create(
            new RenderGraphPassNode(first, new RenderPassDescription("first", 0, [], false)),
            new RenderGraphPassNode(second, new RenderPassDescription("second", 0, [first, first], false)));
        _ = Assert.Throws<ArgumentException>(() => RenderGraphDependencyCompiler.TryOrder(duplicatePredecessorPass, [], out _));
    }

    [Test]
    public void TryOrderReportsOverlappingSamePassStateConflict()
    {
        var passId = new RenderPassId(7, 0);
        var description = new BufferDescription(
            64,
            BufferUsage.Storage | BufferUsage.CopySource,
            GpuMemoryKind.DeviceLocal,
            structuredStride: null,
            debugName: string.Empty);
        var range = new GraphBufferRange(0, 16);
        var version = new GraphBufferVersion(1, 4, 0, description, range);
        var passes = ImmutableArray.Create(
            new RenderGraphPassNode(passId, new RenderPassDescription("pass", 0, [], false)));
        var accesses = ImmutableArray.Create(
            new GraphResourceAccess(passId, version, range, GraphBufferUsage.StorageRead, isWrite: false),
            new GraphResourceAccess(passId, version, range, GraphBufferUsage.CopySource, isWrite: false));

        Assert.That(RenderGraphDependencyCompiler.TryOrder(passes, accesses, out var orderedPasses, out var accessesValid), Is.True);
        Assert.That(accessesValid, Is.False);
        Assert.That(orderedPasses, Is.EqualTo(ImmutableArray.Create(passId)));
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

        Assert.That(RenderGraphDependencyCompiler.TryOrder(passes, [], out var orderedPasses), Is.True);
        Assert.That(orderedPasses, Is.EqualTo(ImmutableArray.Create(preceding, independent, dependent)));
    }
}
