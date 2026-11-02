// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphContentLedgerTests
{
    private static BufferDescription CreateBufferDescription()
        => new(64, BufferUsage.Storage, GpuMemoryKind.DeviceLocal, null, string.Empty);

    private static TextureDescription CreateTextureDescription()
        => new(TextureDimension.Dimension2D, 16, 16, 1, 2, 5, TextureFormat.Rgba8Unorm, TextureUsage.ColorAttachment, string.Empty);

    [Test]
    public void BufferCoverageTracksPartialWritesAcrossVersions()
    {
        var description = CreateBufferDescription();
        var initialVersion = new GraphBufferVersion(1, 4, 0, description, new GraphBufferRange(0, 64));
        var ledger = new GraphBufferContentLedger(initialVersion, contentsValid: false);

        Assert.That(ledger.IsInitialized(initialVersion, new GraphBufferRange(0, 16)), Is.False);

        var firstWrite = ledger.Write(initialVersion, new GraphBufferRange(0, 16));
        Assert.That(ledger.IsInitialized(firstWrite, new GraphBufferRange(0, 16)), Is.True);
        Assert.That(ledger.IsInitialized(firstWrite, new GraphBufferRange(16, 16)), Is.False);

        var secondWrite = ledger.Write(firstWrite, new GraphBufferRange(16, 16));
        Assert.That(ledger.IsInitialized(secondWrite, new GraphBufferRange(0, 32)), Is.True);
        Assert.That(ledger.IsInitialized(initialVersion, new GraphBufferRange(0, 32)), Is.False);
    }

    [Test]
    public void TextureCoverageCombinesRectanglesAndAttachmentDiscardInvalidatesOnlyItsRange()
    {
        var description = CreateTextureDescription();
        var initialVersion = new GraphTextureVersion(
            1,
            4,
            0,
            description,
            new GraphTextureRange(0, 5, 0, 2, TextureAspect.Color));
        var ledger = new GraphTextureContentLedger(initialVersion, contentsValid: false);
        var writtenRange = new GraphTextureRange(0, 2, 0, 2, TextureAspect.Color);

        _ = Assert.Throws<InvalidOperationException>(
            () => ledger.WriteAttachment(initialVersion, writtenRange, AttachmentLoadAction.Load, AttachmentStoreAction.Store));

        var firstWrite = ledger.WriteAttachment(
            initialVersion,
            new GraphTextureRange(0, 1, 0, 2, TextureAspect.Color),
            AttachmentLoadAction.Clear,
            AttachmentStoreAction.Store);
        var secondWrite = ledger.WriteAttachment(
            firstWrite,
            new GraphTextureRange(1, 1, 0, 2, TextureAspect.Color),
            AttachmentLoadAction.Clear,
            AttachmentStoreAction.Store);
        Assert.That(ledger.IsInitialized(secondWrite, writtenRange), Is.True);

        var loadedWrite = ledger.WriteAttachment(
            secondWrite,
            new GraphTextureRange(1, 1, 1, 1, TextureAspect.Color),
            AttachmentLoadAction.Load,
            AttachmentStoreAction.Store);
        var discardedWrite = ledger.WriteAttachment(
            loadedWrite,
            new GraphTextureRange(1, 1, 1, 1, TextureAspect.Color),
            AttachmentLoadAction.DontCare,
            AttachmentStoreAction.Discard);

        Assert.That(ledger.IsInitialized(discardedWrite, new GraphTextureRange(0, 1, 0, 2, TextureAspect.Color)), Is.True);
        Assert.That(ledger.IsInitialized(discardedWrite, new GraphTextureRange(1, 1, 0, 1, TextureAspect.Color)), Is.True);
        Assert.That(ledger.IsInitialized(discardedWrite, new GraphTextureRange(1, 1, 1, 1, TextureAspect.Color)), Is.False);
        Assert.That(ledger.IsInitialized(firstWrite, new GraphTextureRange(1, 1, 0, 1, TextureAspect.Color)), Is.False);
        Assert.That(ledger.IsInitialized(secondWrite, writtenRange), Is.True);
    }
}
