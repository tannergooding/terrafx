// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldAttachmentRecordBindingTests
{
    private static WorldAttachmentFactoryBinding<RootRuntime, RootAuthor> CreateBinding(
        out CompiledContentSchemaRegistry schemas)
    {
        var childAssignment = new ContentSchemaIdentityAssignment("application/attachment-record/child/v1");
        var childSchema = new ContentSchemaReference(
            childAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var childValue = new ContentFieldDescriptor(
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
            [childValue]);
        var rootAssignment = new ContentSchemaIdentityAssignment("application/attachment-record/root/v1");
        var rootSchema = new ContentSchemaReference(
            rootAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var records = new ContentFieldDescriptor(
            1,
            24,
            0,
            true,
            0,
            4,
            [childSchema],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);
        var entityId = new ContentFieldDescriptor(
            2,
            11,
            0,
            true,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.Entity,
            ContentAuthoring.None,
            null);
        var rootMetadata = new DataSchema(
            rootSchema,
            rootAssignment,
            ContentSchemaRole.AttachmentState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [records, entityId]);
        schemas = new CompiledContentSchemaRegistry([childMetadata, rootMetadata]);
        var childValueBinding = WorldAttachmentRecordFieldBinding<ChildRuntime, ChildAuthor>.Value<
            long,
            ChildRuntimeValueReader,
            ChildAuthorValueReader>(
                childValue,
                ContentReferenceKind.None,
                default,
                default);
        var childBinding = WorldAttachmentRecordBinding<ChildRuntime, ChildAuthor>.Create(
            schemas,
            childSchema,
            [childValueBinding]);
        var recordsBinding = WorldAttachmentRecordFieldBinding<RootRuntime, RootAuthor>.Records<
            ChildRuntime,
            ChildAuthor,
            RootRuntimeChildrenReader,
            RootAuthorChildrenReader>(
                records,
                childBinding,
                default,
                default);
        var entityBinding = WorldAttachmentRecordFieldBinding<RootRuntime, RootAuthor>.Value<
            Guid,
            RootRuntimeEntityIdReader,
            RootAuthorEntityIdReader>(
                entityId,
                ContentReferenceKind.Entity,
                default,
                default);
        var rootBinding = WorldAttachmentRecordBinding<RootRuntime, RootAuthor>.Create(
            schemas,
            rootSchema,
            [recordsBinding, entityBinding]);

        return WorldAttachmentFactoryBinding<RootRuntime, RootAuthor>.CreateAggregate(
            Guid.NewGuid(),
            1,
            schemas,
            rootSchema,
            rootBinding,
            new RootAttachmentProvider());
    }

    [Test]
    public void AggregateFactoryRetainsDirectFieldsAndValidatesNestedRecordsAgainstTheLiveWorld()
    {
        var binding = CreateBinding(out var schemas);
        var rootSchema = binding.Descriptor.Schema;
        var entityId = new Guid("70000000-0000-4000-8000-000000000001");
        RootAuthor author = new(
            [
                new ChildAuthor(11),
                new ChildAuthor(12),
            ],
            entityId);
        binding.ValidateAuthor(in author);

        Assert.Multiple(() => {
            Assert.That(binding.Descriptor.IsAggregate, Is.True);
            Assert.That(binding.Descriptor.Fields, Has.Length.EqualTo(2));
            Assert.That(binding.Descriptor.Fields[0].Field.Tag, Is.EqualTo(1));
            Assert.That(binding.Descriptor.Fields[0].Path.Segments, Has.Length.EqualTo(1));
            Assert.That(binding.Fields, Is.Empty);
        });

        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterAttachment(AttachmentScope.World, binding);
        using var domain = new WorldAdmissionDomain(4 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(builder.Build()) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var target = world.CreateEntity(new EntityDescription());
        var liveEntityId = world.GetEntityId(target);
        var runtimeValue = new RootRuntime(
            [new ChildRuntime(11), new ChildRuntime(12)],
            liveEntityId);
        world.SetAttachment(runtimeValue, AttachmentOwnership.Transferred);

        Assert.That(world.TryGetAttachment<RootRuntime>(out var stored), Is.True);
        Assert.That(stored, Is.SameAs(runtimeValue));
        _ = Assert.Throws<InvalidOperationException>(() => world.DestroyEntity(
            target,
            HierarchyDestroyMode.DetachChildrenKeepWorld));
        Assert.That(world.GetEntityId(target), Is.EqualTo(liveEntityId));
        _ = Assert.Throws<InvalidOperationException>(() => world.SetAttachment(
            new RootRuntime([new ChildRuntime(13)], Guid.NewGuid()),
            AttachmentOwnership.Transferred));
        Assert.That(world.TryGetAttachment<RootRuntime>(out stored), Is.True);
        Assert.That(stored, Is.SameAs(runtimeValue));
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

    private sealed class RootAttachmentProvider : IWorldAttachmentStateProvider<RootRuntime, RootAuthor>
    {
        public nint Count(RootRuntime value, WorldAttachmentCaptureBinding binding, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public WorldAttachmentReadResult<RootAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            WorldAttachmentReadBinding<RootAuthor> binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Release(RootRuntime value, WorldAttachmentReleaseBinding binding)
        {
        }

        public RootRuntime Restore(
            in RootAuthor author,
            WorldAttachmentRestoreBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> Validate(
            RootRuntime value,
            WorldAttachmentValidationBinding binding,
            CancellationToken cancellationToken)
            => [];

        public nint Write(
            RootRuntime value,
            scoped Span<byte> destination,
            WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RootAuthor
    {
        internal RootAuthor(ImmutableArray<ChildAuthor> children, Guid entityId)
        {
            Children = children;
            EntityId = entityId;
        }

        internal ImmutableArray<ChildAuthor> Children { get; }

        internal Guid EntityId { get; }
    }

    private readonly struct RootAuthorChildrenReader : IComponentArrayReader<RootAuthor, ChildAuthor>
    {
        public int GetCount(in RootAuthor value) => value.Children.Length;

        public ChildAuthor GetElement(in RootAuthor value, int index) => value.Children[index];
    }

    private readonly struct RootAuthorEntityIdReader : IComponentValueReader<RootAuthor, Guid>
    {
        public Guid Get(in RootAuthor value) => value.EntityId;
    }

    private sealed class RootRuntime
    {
        internal RootRuntime(ImmutableArray<ChildRuntime> children, Guid entityId)
        {
            Children = children;
            EntityId = entityId;
        }

        internal ImmutableArray<ChildRuntime> Children { get; }

        internal Guid EntityId { get; }
    }

    private readonly struct RootRuntimeChildrenReader : IComponentArrayReader<RootRuntime, ChildRuntime>
    {
        public int GetCount(in RootRuntime value) => value.Children.Length;

        public ChildRuntime GetElement(in RootRuntime value, int index) => value.Children[index];
    }

    private readonly struct RootRuntimeEntityIdReader : IComponentValueReader<RootRuntime, Guid>
    {
        public Guid Get(in RootRuntime value) => value.EntityId;
    }
}
