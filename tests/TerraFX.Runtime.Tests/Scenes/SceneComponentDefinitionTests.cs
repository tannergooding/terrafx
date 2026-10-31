// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneComponentDefinitionTests
{
    [Test]
    public void BuiltInTransformsUseDeclaredDefaultsAndWorldMutationValidation()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = SceneComponentDefinitions.RegisterLocalTransform(builder, schemas);
        _ = SceneComponentDefinitions.RegisterRootPosition(builder, schemas);
        var registration = builder.Build();
        var transformRegistration = registration.TryGetComponentType<SceneComponentDefinitions.LocalTransformComponent>(out var componentRegistration)
            ? componentRegistration ?? throw new InvalidOperationException("The LocalTransform registration was null.")
            : throw new InvalidOperationException("The LocalTransform component was not registered.");

        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var entity = world.CreateEntity(new EntityDescription());
        world.Add<SceneComponentDefinitions.LocalTransformComponent>(entity);
        world.Add<SceneComponentDefinitions.RootPositionComponent>(entity);

        Assert.That(world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(entity, out var stored), Is.True);
        Assert.That(stored.Position, Is.EqualTo(Vector3.Zero));
        Assert.That(stored.Rotation, Is.EqualTo(Quaternion.Identity));
        Assert.That(stored.Scale, Is.EqualTo(Vector3.One));
        Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(entity, out var root), Is.True);
        Assert.That(root.Position, Is.EqualTo(default(SpatialPosition3)));

        var assigned = SceneComponentDefinitions.CreateAssignedLocalTransform(
            new Vector3(1, 2, 3),
            new Quaternion(0, 0, 0, 2),
            new Vector3(-2, 3, 4));
        Assert.That(assigned.Rotation, Is.EqualTo(Quaternion.Identity));
        world.Set(entity, in assigned);
        Assert.That(world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(entity, out stored), Is.True);
        Assert.That(stored.Position, Is.EqualTo(assigned.Position));
        Assert.That(stored.Rotation, Is.EqualTo(assigned.Rotation));
        Assert.That(stored.Scale, Is.EqualTo(assigned.Scale));
        var assignedRoot = new SpatialPosition3(new ChunkIndex3(2, -3, 4), new Vector3(1, 2, 3));
        var rootValue = new SceneComponentDefinitions.RootPositionComponent(assignedRoot);
        world.Set(entity, in rootValue);
        Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(entity, out root), Is.True);
        Assert.That(root.Position, Is.EqualTo(assignedRoot));

        var invalidValues = new (SceneComponentDefinitions.LocalTransformComponent Value, string Field)[]
        {
            (new(new Vector3(float.NaN, 0, 0), Quaternion.Identity, Vector3.One), "Position"),
            (new(Vector3.Zero, new Quaternion(0, 0, 0, 0), Vector3.One), "Rotation"),
            (new(Vector3.Zero, new Quaternion(float.MaxValue, 0, 0, 1), Vector3.One), "Rotation"),
            (new(Vector3.Zero, Quaternion.Identity, new Vector3(0.00009f, 1, 1)), "Scale"),
            (new(Vector3.Zero, Quaternion.Identity, new Vector3(1000.0001f, 1, 1)), "Scale"),
        };

        foreach (var (invalidValue, field) in invalidValues)
        {
            var diagnostics = transformRegistration.ValidateValue(in invalidValue, CancellationToken.None);

            Assert.That(diagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo("SceneLocalTransformInvalid"));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostics[0].Operation, Is.EqualTo("Component.Validate"));
            Assert.That(diagnostics[0].Context, Has.Length.EqualTo(1));
            Assert.That(diagnostics[0].Context[0], Is.EqualTo(new DiagnosticContextEntry("Field", field)));

            Assert.That(() => world.Set(entity, invalidValue), Throws.TypeOf<ArgumentException>());
            Assert.That(world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(entity, out stored), Is.True);
            Assert.That(stored.Position, Is.EqualTo(assigned.Position));
            Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(entity, out root), Is.True);
            Assert.That(root.Position, Is.EqualTo(assignedRoot));
        }

        var receipt = world.Step();
        Assert.That(receipt.CompletedTick, Is.EqualTo(0));
    }
}
