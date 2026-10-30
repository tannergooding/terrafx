// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class EbinStructuralReaderTests
{
    [Test]
    public void CanonicalPolicyRejectsFieldGapsAcceptedByStructuralPolicy()
    {
        var encoded = EbinPrimitives.Encode(new EbinRecord(
            new Guid("01234567-89ab-4cde-8f01-23456789abcd"),
            1,
            0,
            [EbinPrimitives.UInt8(1, 0x33)]));
        var noncanonical = new byte[encoded.Length + 16];
        encoded.AsSpan(0, 96).CopyTo(noncanonical);
        encoded.AsSpan(96).CopyTo(noncanonical.AsSpan(112));
        BinaryPrimitives.WriteUInt64LittleEndian(noncanonical.AsSpan(32), checked((ulong)noncanonical.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(noncanonical.AsSpan(72), 112);

        Assert.That(EbinStructuralReader.Validate(noncanonical, EbinReadLimits.Foundation, EbinReadPolicy.Structural, out var consumed, out var diagnostic), Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(consumed, Is.EqualTo(noncanonical.Length));
        Assert.That(EbinStructuralReader.Validate(noncanonical, EbinReadLimits.Foundation, EbinReadPolicy.Canonical, out consumed, out diagnostic), Is.EqualTo(EbinReadStatus.InvalidRecord));
        Assert.That(diagnostic?.Category, Is.EqualTo("noncanonical-record-layout"));
        Assert.That(consumed, Is.Zero);
    }

    [Test]
    public void LimitsRejectArrayAndAggregateArrayBounds()
    {
        var elementPayload = new byte[64];
        Assert.That(EbinPrimitives.WriteFixedArray(1, 2, [0x11, 0x22], elementPayload, out var payloadBytes, out _), Is.EqualTo(EbinStatus.Complete));
        var record = new EbinRecord(
            new Guid("01234567-89ab-4cde-8f01-23456789abcd"),
            1,
            0,
            [new EbinField(1, 23, 1, 2, elementPayload.AsSpan(0, payloadBytes))]);
        var bytes = EbinPrimitives.Encode(record);
        var perArrayLimits = new EbinReadLimits(65_536, 64, 64, 1, 1_048_576, 1_048_576, 1_048_576, 1_048_576);

        Assert.That(EbinStructuralReader.Validate(bytes, perArrayLimits, EbinReadPolicy.Structural, out var consumed, out var diagnostic), Is.EqualTo(EbinReadStatus.InvalidRecord));
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("array-count-budget"));
        Assert.That(diagnostic?.Claim, Is.EqualTo(2));
        Assert.That(diagnostic?.Limit, Is.EqualTo(1));

        var aggregateLimits = new EbinReadLimits(65_536, 64, 64, 8, 1_048_576, 1_048_576, 1_048_576, 1);
        Assert.That(EbinStructuralReader.Validate(bytes, aggregateLimits, EbinReadPolicy.Structural, out consumed, out diagnostic), Is.EqualTo(EbinReadStatus.InvalidRecord));
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("aggregate-array-budget"));
        Assert.That(diagnostic?.Claim, Is.EqualTo(2));
        Assert.That(diagnostic?.Limit, Is.EqualTo(1));
    }

    [Test]
    public void ReadExecutesTheActionWithBorrowedNestedAndArrayViews()
    {
        var child = new EbinRecord(
            new Guid("01234567-89ab-4cde-8f01-23456789abcd"),
            1,
            0,
            [EbinPrimitives.UInt8(3, 0x7F)]);
        var root = new EbinRecord(
            new Guid("11234567-89ab-4cde-8f01-23456789abcd"),
            1,
            0,
            [EbinPrimitives.Record(1, child), EbinPrimitives.RecordArray(2, [child])]);
        var bytes = EbinPrimitives.Encode(root);
        var action = new RecordReadAction();

        Assert.That(EbinStructuralReader.Read(bytes, EbinReadLimits.Foundation, EbinReadPolicy.Structural, ref action, out var consumed, out var diagnostic), Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(consumed, Is.EqualTo(bytes.Length));
        Assert.That(action.Invoked, Is.True);
        Assert.That(action.NestedValue, Is.EqualTo(0x7F));
        Assert.That(action.ArrayValue, Is.EqualTo(0x7F));
    }

    [Test]
    public void ReadHeaderConsumesACompleteHeaderBeforeItsBody()
    {
        var record = EbinPrimitives.Encode(new EbinRecord(
            new Guid("01234567-89ab-4cde-8f01-23456789abcd"),
            1,
            2,
            [EbinPrimitives.UInt8(1, 3)]));

        Assert.That(EbinStructuralReader.ReadHeader(record.AsSpan(0, 64), EbinReadLimits.Foundation, out var header, out var consumed, out var diagnostic), Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(diagnostic, Is.Null);
        Assert.That(consumed, Is.EqualTo(64));
        Assert.That(header.SchemaId, Is.EqualTo(new Guid("01234567-89ab-4cde-8f01-23456789abcd")));
        Assert.That(header.Major, Is.EqualTo(1));
        Assert.That(header.Minor, Is.EqualTo(2));
        Assert.That(header.FieldCount, Is.EqualTo(1));
        Assert.That(header.RecordByteLength, Is.EqualTo(record.Length));
    }

    [Test]
    public void ReadLeavesIncompleteRecordsUnconsumedAndDoesNotInvokeTheAction()
    {
        var record = EbinPrimitives.Encode(new EbinRecord(
            new Guid("01234567-89ab-4cde-8f01-23456789abcd"),
            1,
            0,
            [EbinPrimitives.UInt8(1, 3)]));
        var action = new RecordReadAction();

        Assert.That(EbinStructuralReader.Read(record.AsSpan(0, 64), EbinReadLimits.Foundation, EbinReadPolicy.Structural, ref action, out var consumed, out var diagnostic), Is.EqualTo(EbinReadStatus.NeedMoreData));
        Assert.That(diagnostic, Is.Null);
        Assert.That(consumed, Is.Zero);
        Assert.That(action.Invoked, Is.False);
    }

    [Test]
    public void ReadRejectsUnknownRequiredKindsWithoutInvokingTheAction()
    {
        var record = EbinPrimitives.Encode(new EbinRecord(
            new Guid("01234567-89ab-4cde-8f01-23456789abcd"),
            1,
            0,
            [new EbinField(1, 65_000, 0, 2, [0x12, 0x34])]));
        record[70] = 1;
        var action = new RecordReadAction();

        Assert.That(EbinStructuralReader.Read(record, EbinReadLimits.Foundation, EbinReadPolicy.Structural, ref action, out var consumed, out var diagnostic), Is.EqualTo(EbinReadStatus.UnsupportedSchema));
        Assert.That(diagnostic?.Category, Is.EqualTo("unsupported-required-kind"));
        Assert.That(consumed, Is.Zero);
        Assert.That(action.Invoked, Is.False);
    }

    [Test]
    public void ValidateRejectsAggregateRecordAndFieldLimits()
    {
        var child = new EbinRecord(
            new Guid("01234567-89ab-4cde-8f01-23456789abcd"),
            1,
            0,
            [EbinPrimitives.UInt8(1, 1)]);
        var bytes = EbinPrimitives.Encode(new EbinRecord(
            new Guid("11234567-89ab-4cde-8f01-23456789abcd"),
            1,
            0,
            [EbinPrimitives.Record(1, child)]));
        var recordLimits = new EbinReadLimits(65_536, 64, 64, 1_048_576, 1_048_576, 1, 1_048_576, 1_048_576);

        Assert.That(EbinStructuralReader.Validate(bytes, recordLimits, EbinReadPolicy.Structural, out var consumed, out var diagnostic), Is.EqualTo(EbinReadStatus.InvalidRecord));
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("aggregate-record-budget"));
        Assert.That(diagnostic?.Claim, Is.EqualTo(2));
        Assert.That(diagnostic?.Limit, Is.EqualTo(1));

        var fieldLimits = new EbinReadLimits(65_536, 64, 64, 1_048_576, 1_048_576, 4, 1, 1_048_576);
        Assert.That(EbinStructuralReader.Validate(bytes, fieldLimits, EbinReadPolicy.Structural, out consumed, out diagnostic), Is.EqualTo(EbinReadStatus.InvalidRecord));
        Assert.That(consumed, Is.Zero);
        Assert.That(diagnostic?.Category, Is.EqualTo("aggregate-field-budget"));
        Assert.That(diagnostic?.Claim, Is.EqualTo(2));
        Assert.That(diagnostic?.Limit, Is.EqualTo(1));
    }

    private struct RecordReadAction : IAction<EbinRecordReadView>
    {
        private bool _invoked;
        private byte _arrayValue;
        private byte _nestedValue;

        internal readonly byte ArrayValue => _arrayValue;

        internal readonly bool Invoked => _invoked;

        internal readonly byte NestedValue => _nestedValue;

        public void Invoke(scoped EbinRecordReadView argument)
        {
            _nestedValue = argument.GetRecord(1).GetField(0).Payload[0];
            _arrayValue = argument.GetRecordArrayElement(2, 0).GetField(0).Payload[0];
            _invoked = true;
        }
    }
}
