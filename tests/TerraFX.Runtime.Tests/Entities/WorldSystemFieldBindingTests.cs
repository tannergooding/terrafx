// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldSystemFieldBindingTests
{
    private static WorldSystemFieldDescriptor CreateField(
        ushort wireKind,
        bool isRequired,
        ContentReferenceKind reference)
    {
        var assignment = new ContentSchemaIdentityAssignment("application/system-state/field-binding-test/v1");
        var schemaReference = new ContentSchemaReference(
            assignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
            1,
            wireKind,
            0,
            isRequired,
            isRequired ? 1UL : 0UL,
            1,
            [],
            0,
            ContentUnit.None,
            reference,
            ContentAuthoring.None,
            null);
        var metadata = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.SystemState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [field]);
        var registry = new CompiledContentSchemaRegistry([metadata]);
        return WorldSystemSchema<ProbeSystem>.FromRegistry(registry, schemaReference).Fields[0];
    }

    [Test]
    public void EntityReferenceBindingRequiresEntityReferenceMetadata()
    {
        var field = CreateField(11, true, ContentReferenceKind.None);
        var runtimePath = new WorldSystemSourcePath(["Target"]);
        var authorPath = new WorldSystemSourcePath(["TargetId"]);

        Assert.That(
            () => WorldSystemFieldBinding<ProbeSystem, ProbeAuthor>.EntityReference<EntityRuntimeReader, EntityAuthorReader>(
                field,
                [runtimePath],
                [authorPath],
                default,
                default),
            Throws.ArgumentException);
    }

    [Test]
    public void ScalarBindingCapturesExactFieldAndSourcePaths()
    {
        var field = CreateField(7, true, ContentReferenceKind.None);
        var runtimePath = new WorldSystemSourcePath(["Counter"]);
        var authorPath = new WorldSystemSourcePath(["Counter"]);
        var binding = WorldSystemFieldBinding<ProbeSystem, ProbeAuthor>.Scalar<int, RuntimeReader, AuthorReader>(
            field,
            [runtimePath],
            [authorPath],
            default,
            default);

        Assert.That(binding.Descriptor, Is.SameAs(field));
        Assert.That(binding.RuntimeSourcePaths, Has.Length.EqualTo(1));
        Assert.That(binding.RuntimeSourcePaths[0], Is.EqualTo(runtimePath));
        Assert.That(binding.AuthorSourcePaths[0], Is.EqualTo(authorPath));
    }

    private readonly struct AuthorReader : IWorldSystemValueReader<ProbeAuthor, int>
    {
        public int Invoke(in ProbeAuthor source) => source.Counter;
    }

    private readonly struct EntityAuthorReader : IWorldSystemValueReader<ProbeAuthor, Guid>
    {
        public Guid Invoke(in ProbeAuthor source) => source.TargetId;
    }

    private readonly struct EntityRuntimeReader : IWorldSystemValueReader<ProbeSystem, Entity>
    {
        public Entity Invoke(in ProbeSystem source) => source.Target;
    }

    private sealed class ProbeAuthor
    {
        internal int Counter { get; set; }

        internal Guid TargetId { get; set; }
    }

    private sealed class ProbeSystem : IWorldSystem
    {
        internal int Counter { get; set; }

        internal Entity Target { get; set; }

        public void Execute(WorldSystemContext context)
        {
        }
    }

    private readonly struct RuntimeReader : IWorldSystemValueReader<ProbeSystem, int>
    {
        public int Invoke(in ProbeSystem source) => source.Counter;
    }
}
