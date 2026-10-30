// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class EbinPayloadWriterTests
{
    [Test]
    public void FixedArrayWriterEmitsDescriptorAndElementBytes()
    {
        Vector3[] values = [new Vector3(1, 2, 3)];
        var destination = new byte[44];

        Assert.That(EbinPayloadWriter.WriteFixedArray(values, EbinReadLimits.Foundation, destination, out var payloadBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(destination.Length));
        Assert.That(destination, Is.EqualTo(Convert.FromHexString("000000000000000000000000000000000f0000000c00000000000000000000000000803f0000004000004040")));
    }

    [Test]
    public void FixedArrayWriterPreservesTheElementDescriptorForAnEmptyArray()
    {
        Guid[] values = [];
        var destination = new byte[32];

        Assert.That(EbinPayloadWriter.WriteFixedArray(values, EbinReadLimits.Foundation, destination, out var payloadBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(32));
        Assert.That(destination, Is.EqualTo(Convert.FromHexString("000000000000000000000000000000000b000000100000000000000000000000")));
    }

    [Test]
    public void GuidAndFloatWritersPreserveCanonicalBytes()
    {
        var guid = new Guid("01020304-0506-0708-090a-0b0c0d0e0f10");
        var guidDestination = new byte[16];

        Assert.That(EbinPayloadWriter.Write(guid, guidDestination, out var payloadBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(16));
        Assert.That(guidDestination, Is.EqualTo(Convert.FromHexString("0102030405060708090a0b0c0d0e0f10")));

        var negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        var floatDestination = new byte[4];
        Assert.That(EbinPayloadWriter.Write(negativeZero, floatDestination, out payloadBytes, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(4));
        Assert.That(floatDestination, Is.EqualTo(Convert.FromHexString("00000080")));
    }

    [Test]
    public void MeasureAndWriteRecordArrayEmitContiguousCanonicalChildren()
    {
        var child = Convert.FromHexString(
            "4542494e010000004000000001000000"
            + "000102030405060708090a0b0c0d0e0f"
            + "7000000000000000"
            + "2000000000000000"
            + "01000000000000000000000000000000"
            + "0100000009000100600000000000000001000000000000000100000000000000"
            + "01"
            + "000000000000000000000000000000");
        var childStorage = new byte[child.Length * 2];
        child.CopyTo(childStorage, 0);
        child.CopyTo(childStorage, child.Length);
        EbinPayloadSlice[] children = [
            new EbinPayloadSlice(0, child.Length),
            new EbinPayloadSlice(child.Length, child.Length),
        ];

        Assert.That(EbinPayloadWriter.MeasureRecordArray(children, childStorage, EbinReadLimits.Foundation, out var payloadBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(payloadBytes, Is.EqualTo(256));

        var destination = new byte[payloadBytes];
        Assert.That(EbinPayloadWriter.WriteRecordArray(children, childStorage, EbinReadLimits.Foundation, destination, out var written, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(written, Is.EqualTo(payloadBytes));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(destination), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(destination.AsSpan(8)), Is.EqualTo(32));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(destination.AsSpan(16)), Is.EqualTo(144));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(destination.AsSpan(24)), Is.EqualTo(256));
        Assert.That(destination.AsSpan(32, child.Length).ToArray(), Is.EqualTo(child));
        Assert.That(destination.AsSpan(144, child.Length).ToArray(), Is.EqualTo(child));
    }

    [Test]
    public void RecordArrayWriterRejectsTrailingChildDataAndPreservesShortWorkspace()
    {
        var child = Convert.FromHexString(
            "4542494e010000004000000001000000"
            + "000102030405060708090a0b0c0d0e0f"
            + "7000000000000000"
            + "2000000000000000"
            + "01000000000000000000000000000000"
            + "0100000009000100600000000000000001000000000000000100000000000000"
            + "01"
            + "000000000000000000000000000000");
        var childWithTrailingData = new byte[child.Length + 1];
        child.CopyTo(childWithTrailingData, 0);
        EbinPayloadSlice[] children = [new EbinPayloadSlice(0, childWithTrailingData.Length)];

        Assert.That(EbinPayloadWriter.MeasureRecordArray(children, childWithTrailingData, EbinReadLimits.Foundation, out var payloadBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(payloadBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("recordarray-nested-total"));
        Assert.That(diagnostic?.Source, Is.EqualTo(EbinStructuralDiagnosticSource.EncodedInput));

        children = [new EbinPayloadSlice(0, child.Length)];
        var destination = new byte[child.Length + 31];
        destination.AsSpan().Fill(0xA5);
        Assert.That(EbinPayloadWriter.WriteRecordArray(children, child, EbinReadLimits.Foundation, destination, out var payloadBytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.WorkspaceTooSmall));
        Assert.That(payloadBytesWritten, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("workspace-too-small"));
        Assert.That(destination, Is.All.EqualTo(0xA5));
    }

    [Test]
    public void ScalarWriterLeavesAShortDestinationUnchanged()
    {
        var destination = new byte[11];
        destination.AsSpan().Fill(0xA5);

        Assert.That(EbinPayloadWriter.Write(new Vector3(1, 2, 3), destination, out var payloadBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.WorkspaceTooSmall));
        Assert.That(payloadBytes, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("workspace-too-small"));
        Assert.That(diagnostic?.Claim, Is.EqualTo(destination.Length));
        Assert.That(diagnostic?.Limit, Is.EqualTo(12));
        Assert.That(destination, Is.All.EqualTo(0xA5));
    }
}
