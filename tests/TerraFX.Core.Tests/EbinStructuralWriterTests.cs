// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class EbinStructuralWriterTests
{
    [Test]
    public void CommitValidatesTheCandidateBeforeReportingDestinationCapacity()
    {
        var candidate = Convert.FromHexString(
            "4542494e010000004000000001000000"
            + "000102030405060708090a0b0c0d0e0f"
            + "7000000000000000"
            + "2000000000000000"
            + "01000000000000000000000000000000"
            + "0100000009000100600000000000000001000000000000000100000000000000"
            + "01"
            + "000000000000000000000000000000");
        var destination = new byte[candidate.Length - 1];
        destination.AsSpan().Fill(0xA5);

        Assert.That(EbinStructuralWriter.Commit(candidate, EbinReadLimits.Foundation, destination, out var bytesWritten, out var diagnostic), Is.EqualTo(EbinWriteStatus.DestinationTooSmall));
        Assert.That(bytesWritten, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("destination-too-small"));
        Assert.That(diagnostic?.Claim, Is.EqualTo(destination.Length));
        Assert.That(diagnostic?.Limit, Is.EqualTo(candidate.Length));
        Assert.That(destination, Is.All.EqualTo(0xA5));

        candidate[96] = 2;
        Assert.That(EbinStructuralWriter.Commit(candidate, EbinReadLimits.Foundation, destination, out bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.InvalidRecord));
        Assert.That(bytesWritten, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("boolean-domain"));
        Assert.That(diagnostic?.Source, Is.EqualTo(EbinStructuralDiagnosticSource.Candidate));
        Assert.That(destination, Is.All.EqualTo(0xA5));
    }

    [Test]
    public void MeasurePrepareCommitAndWriteMatchTheCanonicalRecordLiteral()
    {
        var expected = Convert.FromHexString(
            "4542494e010000004000000001000000"
            + "000102030405060708090a0b0c0d0e0f"
            + "7000000000000000"
            + "2000000000000000"
            + "01000000000000000000000000000000"
            + "0100000009000100600000000000000001000000000000000100000000000000"
            + "01"
            + "000000000000000000000000000000");
        EbinFieldSource[] fields = [new EbinFieldSource(1, 9, 1, 1, new EbinPayloadSlice(0, 1))];
        byte[] payloadStorage = [1];
        var source = new EbinRecordWriteView(
            new Guid("00010203-0405-0607-0809-0a0b0c0d0e0f"),
            1,
            0,
            fields,
            payloadStorage);

        Assert.That(EbinStructuralWriter.Measure(source, EbinReadLimits.Foundation, out var recordBytes, out var diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(recordBytes, Is.EqualTo(expected.Length));

        var candidateStorage = new byte[expected.Length];
        Assert.That(EbinStructuralWriter.Prepare(source, EbinReadLimits.Foundation, candidateStorage, out var candidateBytes, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(candidateBytes, Is.EqualTo(expected.Length));
        Assert.That(candidateStorage, Is.EqualTo(expected));

        var committed = new byte[expected.Length];
        Assert.That(EbinStructuralWriter.Commit(candidateStorage, EbinReadLimits.Foundation, committed, out var bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(expected.Length));
        Assert.That(committed, Is.EqualTo(expected));

        var written = new byte[expected.Length];
        Assert.That(EbinStructuralWriter.Write(source, EbinReadLimits.Foundation, candidateStorage, written, out bytesWritten, out diagnostic), Is.EqualTo(EbinWriteStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(bytesWritten, Is.EqualTo(expected.Length));
        Assert.That(written, Is.EqualTo(expected));
    }
}
