// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneSupportRegistrationTests
{
    private static CompiledContentSchemaRegistry CreateSchemaRegistry()
        => new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);

    private static ContentSchemaReference GetSceneComponentSchema(string name)
    {
        foreach (var schema in SceneSchemaCatalogue.Schemas)
        {
            if (StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, name))
            {
                return schema.Schema;
            }
        }

        throw new InvalidOperationException($"The Scene schema '{name}' is not registered.");
    }

    [Test]
    public void CreatesOneSceneParticipantPerWorldFromSharedRegistration()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(support);
        var registration = builder.Build();

        using var first = World.Create(new WorldOptions(registration));
        using var second = World.Create(new WorldOptions(registration));
        var firstParticipant = support.GetParticipant(first);
        var secondParticipant = support.GetParticipant(second);

        Assert.That(firstParticipant, Is.Not.SameAs(secondParticipant));
        Assert.That(support.GetParticipant(first), Is.SameAs(firstParticipant));
        Assert.That(firstParticipant.OwnedByteReservation, Is.GreaterThan(0));
    }

    [Test]
    public void RegistersBuiltInComponentsBeforeWorldConstruction()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();

        Assert.That(builder.RegisterSceneSupport(support), Is.SameAs(builder));
        var registration = builder.Build();

        Assert.That(registration.SceneSupport, Is.SameAs(support));

        foreach (var schemaName in new[] { "scene/local-transform", "scene/root-position", "scene/camera" })
        {
            var schema = GetSceneComponentSchema(schemaName);
            Assert.That(registration.GetComponentBinding(schema).Schema, Is.EqualTo(schema));
        }
    }

    [Test]
    public void RejectsConflictingComponentsAtomically()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        _ = SceneComponentDefinitions.RegisterCamera(builder, schemas);

        Assert.That(
            () => builder.RegisterSceneSupport(SceneSupport.CreateRegistration()),
            Throws.ArgumentException);

        var registration = builder.Build();
        Assert.That(registration.TryGetComponentType<SceneComponentDefinitions.LocalTransformComponent>(out _), Is.False);
        Assert.That(registration.TryGetComponentType<SceneComponentDefinitions.RootPositionComponent>(out _), Is.False);
        Assert.That(registration.TryGetComponentType<SceneComponentDefinitions.CameraComponent>(out _), Is.True);
    }

    [Test]
    public void RejectsDuplicateSceneSupport()
    {
        var builder = new WorldRegistrationBuilder(CreateSchemaRegistry());
        var support = SceneSupport.CreateRegistration();

        _ = builder.RegisterSceneSupport(support);

        Assert.That(
            () => builder.RegisterSceneSupport(SceneSupport.CreateRegistration()),
            Throws.ArgumentException);
        Assert.That(builder.Build().SceneSupport, Is.SameAs(support));
    }

    [Test]
    public void SceneStorageReservationsAreThreadSafeAndRespectTheWorldBudget()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(support);
        var registration = builder.Build();
        long baselineWorldBytes;

        using (var baselineWorld = World.Create(new WorldOptions(registration)))
        {
            baselineWorldBytes = baselineWorld.GetWorldOwnedStateBytes();
        }

        const int admittedReservationCount = 128;
        const int reservationAttemptCount = 160;
        const long reservationBytes = 8;
        var maximumOwnedStateBytes = checked(baselineWorldBytes + (admittedReservationCount * reservationBytes));
        var defaults = WorldSchedulingLimits.Default;
        var limits = new WorldSchedulingLimits(
            defaults.MaximumRegistrationBytes,
            defaults.MaximumGraphBytes,
            defaults.MaximumWorkPlanBytes,
            defaults.MaximumCommandStagingBytes,
            maximumOwnedStateBytes,
            defaults.MaximumDiagnosticBytes,
            defaults.MaximumConflictComparisons);
        using var world = World.Create(new WorldOptions(registration) {
            SchedulingLimits = limits,
        });
        var participant = support.GetParticipant(world);
        var reservations = new WorldSceneStorageReservation?[reservationAttemptCount];

        try
        {
            _ = Parallel.For(
                0,
                reservations.Length,
                index => reservations[index] = world.TryReserveSceneStorage(participant, reservationBytes));

            var admittedCount = 0;

            for (var index = 0; index < reservations.Length; index++)
            {
                admittedCount += reservations[index] is null ? 0 : 1;
            }

            Assert.That(admittedCount, Is.EqualTo(admittedReservationCount));
            Assert.That(
                world.GetWorldOwnedStateBytes(),
                Is.EqualTo(maximumOwnedStateBytes));
        }
        finally
        {
            for (var index = 0; index < reservations.Length; index++)
            {
                reservations[index]?.Dispose();
            }
        }

        Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(baselineWorldBytes));
    }

    [Test]
    public void WorldHierarchyParentChangesPreserveWorldOwnershipAndCycleSafety()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(support);
        var registration = builder.Build();
        using var world = World.Create(new WorldOptions(registration));
        using var foreignWorld = World.Create(new WorldOptions(registration));
        var participant = support.GetParticipant(world);
        var parent = world.CreateEntity(new EntityDescription());
        var child = world.CreateEntity(new EntityDescription());
        var foreignParent = foreignWorld.CreateEntity(new EntityDescription());

        world.ApplyHierarchyParentChange(participant, child, parent);

        Assert.That(world.TryGetHierarchyParent(child, out var actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(parent));
        Assert.That(world.TryGetFirstHierarchyChild(parent, out var firstChild), Is.True);
        Assert.That(firstChild, Is.EqualTo(child));
        Assert.That(world.TryGetNextHierarchySibling(child, out _), Is.False);
        Assert.That(world.TryGetHierarchyParent(parent, out _), Is.False);

        _ = Assert.Throws<InvalidOperationException>(
            () => world.ApplyHierarchyParentChange(participant, parent, child));
        _ = Assert.Throws<ArgumentException>(
            () => world.ApplyHierarchyParentChange(participant, child, foreignParent));

        Assert.That(world.TryGetHierarchyParent(child, out actualParent), Is.True);
        Assert.That(actualParent, Is.EqualTo(parent));
    }
}
