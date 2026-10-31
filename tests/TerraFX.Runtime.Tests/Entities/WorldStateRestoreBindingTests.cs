// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldStateRestoreBindingTests
{
    [Test]
    public void OptionalEntityResolutionReturnsNullOnlyForOptionalField()
    {
        var identity = new ContentSchemaIdentityAssignment("test/optional-entity-reference");
        var schemaReference = new ContentSchemaReference(
            identity.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var schema = new DataSchema(
            schemaReference,
            identity,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [
                new ContentFieldDescriptor(
                    tag: 1,
                    wireKind: 11,
                    elementKind: 0,
                    isRequired: false,
                    minimumCount: 1,
                    maximumCount: 1,
                    nestedSchemas: [],
                    nestedRoleMask: 0,
                    unit: ContentUnit.None,
                    reference: ContentReferenceKind.Entity,
                    authoringFlags: ContentAuthoring.None,
                    defaultValue: null),
                new ContentFieldDescriptor(
                    tag: 2,
                    wireKind: 11,
                    elementKind: 0,
                    isRequired: true,
                    minimumCount: 1,
                    maximumCount: 1,
                    nestedSchemas: [],
                    nestedRoleMask: 0,
                    unit: ContentUnit.None,
                    reference: ContentReferenceKind.Entity,
                    authoringFlags: ContentAuthoring.None,
                    defaultValue: null),
            ]);
        var schemas = new CompiledContentSchemaRegistry([schema]);
        var componentSchema = ComponentSchema<int>.FromRegistry(
            schemas,
            schemaReference,
            new ComponentLayout(Guid.NewGuid(), Unsafe.SizeOf<int>()));
        var optionalField = componentSchema.GetField(
            new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]));
        var requiredField = componentSchema.GetField(
            new ComponentFieldPath([ComponentPropertySegment.FieldTag(2)]));
        var targetId = new Guid("a1000000-0000-4000-8000-000000000001");
        var absentTargetId = new Guid("a1000000-0000-4000-8000-000000000002");
        var registration = new WorldRegistrationBuilder(schemas).Build();

        using var world = World.Create(new WorldOptions(registration));
        var target = world.CreateEntity(new EntityDescription());
        var binding = new WorldStateRestoreBinding(new RestoreContext(target, targetId, absentTargetId));

        Assert.That(binding.ResolveOptionalEntity(optionalField, targetId), Is.EqualTo(target));
        Assert.That(binding.ResolveOptionalEntity(optionalField, absentTargetId), Is.Null);
        Assert.That(
            () => binding.ResolveOptionalEntity(requiredField, targetId),
            Throws.ArgumentException);
        Assert.That(
            () => binding.ResolveOptionalEntity(optionalField, Guid.Empty),
            Throws.ArgumentException);

        binding.Revoke();
        Assert.That(
            () => binding.ResolveOptionalEntity(optionalField, targetId),
            Throws.InvalidOperationException);
    }

    private sealed class RestoreContext : IComponentRestoreContext
    {
        private readonly Guid _absentTargetId;
        private readonly Entity _target;
        private readonly Guid _targetId;

        internal RestoreContext(Entity target, Guid targetId, Guid absentTargetId)
        {
            _target = target;
            _targetId = targetId;
            _absentTargetId = absentTargetId;
            OwnerThreadId = Environment.CurrentManagedThreadId;
        }

        public bool IsActive => true;

        public int OwnerThreadId { get; }

        public TDependency ReadSameEntity<TDependency>()
            where TDependency : unmanaged
            => throw new InvalidOperationException("The test restore context has no Derived dependencies.");

        public Entity ResolveEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
            => throw new InvalidOperationException("The test restore context has no required Entity references.");

        public Entity ResolveEntity(WorldSystemFieldDescriptor referenceField, Guid targetEntityId)
            => throw new InvalidOperationException("The test restore context has no system Entity references.");

        public Entity? ResolveOptionalEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
        {
            if (targetEntityId == _targetId)
            {
                return _target;
            }

            if (targetEntityId == _absentTargetId)
            {
                return null;
            }

            throw new InvalidOperationException("The test restore context does not contain the selected target.");
        }
    }
}
