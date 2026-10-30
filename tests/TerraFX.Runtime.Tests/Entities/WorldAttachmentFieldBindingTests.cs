// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldAttachmentFieldBindingTests
{
    private static ContentFieldDescriptor CreateField(
        uint tag,
        ushort wireKind,
        bool isRequired,
        ContentReferenceKind reference)
        => new(
            tag,
            wireKind,
            0,
            isRequired,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            reference,
            ContentAuthoring.None,
            null);

    private static (CompiledContentSchemaRegistry Registry, ContentSchemaReference Schema) CreateRegistry()
    {
        var assignment = new ContentSchemaIdentityAssignment("application/attachment-state/field-binding-test/v1");
        var schema = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var metadata = new DataSchema(
            schema,
            assignment,
            ContentSchemaRole.AttachmentState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [
                CreateField(1, 8, isRequired: true, ContentReferenceKind.None),
                CreateField(2, 11, isRequired: false, ContentReferenceKind.Entity),
            ]);
        return (new CompiledContentSchemaRegistry([metadata]), schema);
    }

    [Test]
    public void OptionalEntityReferenceRequiresOptionalEntityMetadata()
    {
        var (registry, schema) = CreateRegistry();
        var binding = WorldAttachmentFieldBinding<ProbeAttachment, ProbeAuthor>.OptionalEntityReference<
            AttachmentTargetReader,
            AuthorTargetReader>(
                registry,
                schema,
                new ComponentFieldPath([ComponentPropertySegment.FieldTag(2)]),
                default,
                default);

        Assert.That(binding.Kind, Is.EqualTo(WorldAttachmentFieldBindingKind.OptionalEntityReference));
        Assert.That(binding.Descriptor.Field.Reference, Is.EqualTo(ContentReferenceKind.Entity));
        Assert.That(binding.Descriptor.Field.IsRequired, Is.False);
        Assert.That(
            () => WorldAttachmentFieldBinding<ProbeAttachment, ProbeAuthor>.OptionalEntityReference<
                AttachmentTargetReader,
                AuthorTargetReader>(
                    registry,
                    schema,
                    new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]),
                    default,
                    default),
            Throws.ArgumentException);
    }

    [Test]
    public void ValueBindingRequiresOpaqueRequiredValueMetadata()
    {
        var (registry, schema) = CreateRegistry();
        var binding = WorldAttachmentFieldBinding<ProbeAttachment, ProbeAuthor>.Value<
            long,
            AttachmentCounterReader,
            AuthorCounterReader>(
                registry,
                schema,
                new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]),
                default,
                default);

        Assert.That(binding.Kind, Is.EqualTo(WorldAttachmentFieldBindingKind.Value));
        Assert.That(binding.Descriptor.Field.WireKind, Is.EqualTo(8));
        Assert.That(
            () => WorldAttachmentFieldBinding<ProbeAttachment, ProbeAuthor>.Value<
                long,
                AttachmentCounterReader,
                AuthorCounterReader>(
                    registry,
                    schema,
                    new ComponentFieldPath([ComponentPropertySegment.FieldTag(2)]),
                    default,
                    default),
            Throws.ArgumentException);
    }

    private readonly struct AttachmentCounterReader : IComponentValueReader<ProbeAttachment, long>
    {
        public long Get(in ProbeAttachment value) => value.Counter;
    }

    private readonly struct AttachmentTargetReader : IComponentOptionalReader<ProbeAttachment, Entity>
    {
        public Entity GetPresent(in ProbeAttachment value)
            => value.Target ?? throw new InvalidOperationException();

        public bool IsPresent(in ProbeAttachment value) => value.Target.HasValue;
    }

    private readonly struct AuthorCounterReader : IComponentValueReader<ProbeAuthor, long>
    {
        public long Get(in ProbeAuthor value) => value.Counter;
    }

    private readonly struct AuthorTargetReader : IComponentOptionalReader<ProbeAuthor, Guid>
    {
        public Guid GetPresent(in ProbeAuthor value)
            => value.TargetId ?? throw new InvalidOperationException();

        public bool IsPresent(in ProbeAuthor value) => value.TargetId.HasValue;
    }

    private sealed class ProbeAttachment
    {
        internal long Counter { get; set; }

        internal Entity? Target { get; set; }
    }

    private readonly record struct ProbeAuthor(long Counter, Guid? TargetId);
}
