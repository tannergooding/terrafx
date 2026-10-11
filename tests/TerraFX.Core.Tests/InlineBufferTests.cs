// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Linq;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class InlineBufferTests
{
    [Test]
    public void RentedBufferCreateClearsInlineAndRentedRanges()
    {
        var inline = RentedBuffer.Create<ulong>(128, out var inlineOwner);

        try
        {
            Assert.That(inline.Length, Is.EqualTo(128));
            Assert.That(inline.ToArray(), Is.EqualTo(new ulong[128]));
            inline.Fill(42);
        }
        finally
        {
            inlineOwner.Dispose();
        }

        var rented = RentedBuffer.CreateUninitialized<byte>(2048, out var dirtyOwner);
        rented.Fill(0xA5);
        dirtyOwner.Dispose();

        var cleared = RentedBuffer.Create<byte>(2048, out var clearedOwner);

        try
        {
            Assert.That(cleared.Length, Is.EqualTo(2048));
            Assert.That(cleared.ToArray(), Is.EqualTo(new byte[2048]));
        }
        finally
        {
            clearedOwner.Dispose();
        }
    }

    [Test]
    public void RentedBufferUninitializedRangeCanBeFullyWritten()
    {
        var inline = RentedBuffer.CreateUninitialized<ulong>(128, out var inlineBuffer);

        try
        {
            inline.Fill(17);
            Assert.That(inline.ToArray(), Is.EqualTo(Enumerable.Repeat(17ul, 128)));
        }
        finally
        {
            inlineBuffer.Dispose();
        }

        var span = RentedBuffer.CreateUninitialized<int>(1024, out var rentedBuffer);

        try
        {
            span.Fill(17);
            Assert.That(span.ToArray(), Is.EqualTo(Enumerable.Repeat(17, 1024)));
        }
        finally
        {
            rentedBuffer.Dispose();
        }
    }
}
