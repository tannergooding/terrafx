// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldAttachmentRecordFieldKindsTests
{
    private static WorldAttachmentRecordBinding<ComplexRuntime, ComplexAuthor> CreateBinding()
    {
        var childAssignment = new ContentSchemaIdentityAssignment("application/attachment-record/optional-child/v1");
        var childSchema = new ContentSchemaReference(
            childAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var childField = new ContentFieldDescriptor(
            1,
            8,
            0,
            true,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);
        var childMetadata = new DataSchema(
            childSchema,
            childAssignment,
            ContentSchemaRole.Auxiliary,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [childField]);
        var rootAssignment = new ContentSchemaIdentityAssignment("application/attachment-record/field-kinds/v1");
        var rootSchema = new ContentSchemaReference(rootAssignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var fields = ImmutableArray.Create(
            new ContentFieldDescriptor(
                1,
                13,
                0,
                true,
                24,
                24,
                [],
                0,
                ContentUnit.None,
                ContentReferenceKind.None,
                ContentAuthoring.None,
                null),
            new ContentFieldDescriptor(
                2,
                23,
                10,
                true,
                1,
                4,
                [],
                0,
                ContentUnit.None,
                ContentReferenceKind.None,
                ContentAuthoring.None,
                null),
            new ContentFieldDescriptor(
                3,
                22,
                0,
                false,
                1,
                1,
                [childSchema],
                0,
                ContentUnit.None,
                ContentReferenceKind.None,
                ContentAuthoring.None,
                null),
            new ContentFieldDescriptor(
                4,
                8,
                0,
                false,
                1,
                1,
                [],
                0,
                ContentUnit.None,
                ContentReferenceKind.None,
                ContentAuthoring.None,
                null));
        var rootMetadata = new DataSchema(
            rootSchema,
            rootAssignment,
            ContentSchemaRole.AttachmentState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            fields);
        var schemas = new CompiledContentSchemaRegistry([childMetadata, rootMetadata]);
        var childValue = WorldAttachmentRecordFieldBinding<ChildRuntime, ChildAuthor>.Value<
            long,
            ChildRuntimeValueReader,
            ChildAuthorValueReader>(
                childField,
                ContentReferenceKind.None,
                default,
                default);
        var childBinding = WorldAttachmentRecordBinding<ChildRuntime, ChildAuthor>.Create(
            schemas,
            childSchema,
            [childValue]);
        var bindings = ImmutableArray.Create(
            WorldAttachmentRecordFieldBinding<ComplexRuntime, ComplexAuthor>.Bytes<
                ComplexRuntimeBytesReader,
                ComplexAuthorBytesReader>(fields[0], default, default),
            WorldAttachmentRecordFieldBinding<ComplexRuntime, ComplexAuthor>.FixedValues<
                float,
                ComplexRuntimeWeightsReader,
                ComplexAuthorWeightsReader>(fields[1], default, default),
            WorldAttachmentRecordFieldBinding<ComplexRuntime, ComplexAuthor>.OptionalRecord<
                ChildRuntime,
                ChildAuthor,
                ComplexRuntimeOptionalRecordReader,
                ComplexAuthorOptionalRecordReader>(fields[2], childBinding, default, default),
            WorldAttachmentRecordFieldBinding<ComplexRuntime, ComplexAuthor>.Optional<
                long,
                ComplexRuntimeOptionalTickReader,
                ComplexAuthorOptionalTickReader>(
                    fields[3],
                    ContentReferenceKind.None,
                    default,
                    default));

        return WorldAttachmentRecordBinding<ComplexRuntime, ComplexAuthor>.Create(
            schemas,
            rootSchema,
            bindings);
    }

    [Test]
    public void BytesFixedValuesAndOptionalRecordFieldsValidateTheirActualShapes()
    {
        var binding = CreateBinding();
        ComplexAuthor present = new(
            [.. new byte[24]],
            [0.25f, 0.75f],
            new ChildAuthor(3),
            optionalTick: null);
        ComplexAuthor absent = new(
            [.. new byte[24]],
            [1.0f],
            transition: null,
            optionalTick: 4);

        binding.ValidateAuthor(in present);
        binding.ValidateAuthor(in absent);

        ComplexAuthor invalidBytes = new(
            [.. new byte[23]],
            [0.5f],
            transition: null,
            optionalTick: null);
        _ = Assert.Throws<InvalidDataException>(() => binding.ValidateAuthor(in invalidBytes));
    }

    private sealed class ChildAuthor
    {
        internal ChildAuthor(long value)
        {
            Value = value;
        }

        internal long Value { get; }
    }

    private readonly struct ChildAuthorValueReader : IComponentValueReader<ChildAuthor, long>
    {
        public long Get(in ChildAuthor value) => value.Value;
    }

    private sealed class ChildRuntime
    {
        internal ChildRuntime(long value)
        {
            Value = value;
        }

        internal long Value { get; }
    }

    private readonly struct ChildRuntimeValueReader : IComponentValueReader<ChildRuntime, long>
    {
        public long Get(in ChildRuntime value) => value.Value;
    }

    private sealed class ComplexAuthor
    {
        internal ComplexAuthor(
            ImmutableArray<byte> bytes,
            ImmutableArray<float> weights,
            ChildAuthor? transition,
            long? optionalTick)
        {
            Bytes = bytes;
            Weights = weights;
            Transition = transition;
            OptionalTick = optionalTick;
        }

        internal ImmutableArray<byte> Bytes { get; }

        internal long? OptionalTick { get; }

        internal ChildAuthor? Transition { get; }

        internal ImmutableArray<float> Weights { get; }
    }

    private readonly struct ComplexAuthorBytesReader : IComponentArrayReader<ComplexAuthor, byte>
    {
        public int GetCount(in ComplexAuthor value) => value.Bytes.Length;

        public byte GetElement(in ComplexAuthor value, int index) => value.Bytes[index];
    }

    private readonly struct ComplexAuthorOptionalRecordReader : IComponentOptionalReader<ComplexAuthor, ChildAuthor>
    {
        public ChildAuthor GetPresent(in ComplexAuthor value)
            => value.Transition ?? throw new InvalidOperationException();

        public bool IsPresent(in ComplexAuthor value) => value.Transition is not null;
    }

    private readonly struct ComplexAuthorOptionalTickReader : IComponentOptionalReader<ComplexAuthor, long>
    {
        public long GetPresent(in ComplexAuthor value)
            => value.OptionalTick ?? throw new InvalidOperationException();

        public bool IsPresent(in ComplexAuthor value) => value.OptionalTick.HasValue;
    }

    private readonly struct ComplexAuthorWeightsReader : IComponentArrayReader<ComplexAuthor, float>
    {
        public int GetCount(in ComplexAuthor value) => value.Weights.Length;

        public float GetElement(in ComplexAuthor value, int index) => value.Weights[index];
    }

    private sealed class ComplexRuntime
    {
        internal ComplexRuntime(
            ImmutableArray<byte> bytes,
            ImmutableArray<float> weights,
            ChildRuntime? transition,
            long? optionalTick)
        {
            Bytes = bytes;
            Weights = weights;
            Transition = transition;
            OptionalTick = optionalTick;
        }

        internal ImmutableArray<byte> Bytes { get; }

        internal long? OptionalTick { get; }

        internal ChildRuntime? Transition { get; }

        internal ImmutableArray<float> Weights { get; }
    }

    private readonly struct ComplexRuntimeBytesReader : IComponentArrayReader<ComplexRuntime, byte>
    {
        public int GetCount(in ComplexRuntime value) => value.Bytes.Length;

        public byte GetElement(in ComplexRuntime value, int index) => value.Bytes[index];
    }

    private readonly struct ComplexRuntimeOptionalRecordReader : IComponentOptionalReader<ComplexRuntime, ChildRuntime>
    {
        public ChildRuntime GetPresent(in ComplexRuntime value)
            => value.Transition ?? throw new InvalidOperationException();

        public bool IsPresent(in ComplexRuntime value) => value.Transition is not null;
    }

    private readonly struct ComplexRuntimeOptionalTickReader : IComponentOptionalReader<ComplexRuntime, long>
    {
        public long GetPresent(in ComplexRuntime value)
            => value.OptionalTick ?? throw new InvalidOperationException();

        public bool IsPresent(in ComplexRuntime value) => value.OptionalTick.HasValue;
    }

    private readonly struct ComplexRuntimeWeightsReader : IComponentArrayReader<ComplexRuntime, float>
    {
        public int GetCount(in ComplexRuntime value) => value.Weights.Length;

        public float GetElement(in ComplexRuntime value, int index) => value.Weights[index];
    }
}
