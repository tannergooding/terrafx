// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class TextureReadbackTests
{
    private static OwnedBuffer<byte> CreateStorage(nint byteLength)
    {
        var storage = UnmanagedBuffer<byte>.Allocate(byteLength, new BufferAllocationOptions(byteLength, 1));
        return storage.TransferToOwnedBuffer();
    }

    [Test]
    public void TextureReadbackOwnsTheExactPitchedSubresource()
    {
        using var storage = CreateStorage(36);
        using var readback = new TextureReadback(
            TextureFormat.Rgba8Unorm,
            new TextureSubresource(1, 2, TextureAspect.Color),
            width: 2,
            height: 3,
            depth: 1,
            rowPitch: 12,
            slicePitch: 36,
            storage);

        Assert.That(readback.Format, Is.EqualTo(TextureFormat.Rgba8Unorm));
        Assert.That(readback.Subresource, Is.EqualTo(new TextureSubresource(1, 2, TextureAspect.Color)));
        Assert.That(readback.Width, Is.EqualTo(2));
        Assert.That(readback.Height, Is.EqualTo(3));
        Assert.That(readback.Depth, Is.EqualTo(1));
        Assert.That(readback.RowPitch, Is.EqualTo((nint)12));
        Assert.That(readback.SlicePitch, Is.EqualTo((nint)36));
        Assert.That(readback.ByteLength, Is.EqualTo((nint)36));

        using var lease = readback.AcquireReadLease();
        Assert.That(lease.Length, Is.EqualTo((nint)36));

        readback.Dispose();
        Assert.That(lease.Length, Is.EqualTo((nint)36));
        _ = Assert.Throws<ObjectDisposedException>(() => readback.AcquireReadLease());
    }

    [Test]
    public void TextureReadbackRejectsInvalidExtentPitchAspectAndStorage()
    {
        using var storage = CreateStorage(36);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureReadback(
            TextureFormat.Rgba8Unorm,
            new TextureSubresource(0, 0, TextureAspect.Color),
            width: 0,
            height: 1,
            depth: 1,
            rowPitch: 4,
            slicePitch: 4,
            storage));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureReadback(
            TextureFormat.Rgba8Unorm,
            new TextureSubresource(0, 0, TextureAspect.Color),
            width: 2,
            height: 1,
            depth: 1,
            rowPitch: 7,
            slicePitch: 7,
            storage));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new TextureReadback(
            TextureFormat.Rgba8Unorm,
            new TextureSubresource(0, 0, TextureAspect.Color),
            width: 2,
            height: 2,
            depth: 1,
            rowPitch: 8,
            slicePitch: 15,
            storage));
        _ = Assert.Throws<ArgumentException>(() => new TextureReadback(
            TextureFormat.Rgba8Unorm,
            new TextureSubresource(0, 0, TextureAspect.Depth),
            width: 2,
            height: 3,
            depth: 1,
            rowPitch: 8,
            slicePitch: 24,
            storage));
        _ = Assert.Throws<ArgumentException>(() => new TextureReadback(
            TextureFormat.Rgba8Unorm,
            new TextureSubresource(0, 0, TextureAspect.Color),
            width: 2,
            height: 3,
            depth: 1,
            rowPitch: 8,
            slicePitch: 24,
            storage));
    }
}
