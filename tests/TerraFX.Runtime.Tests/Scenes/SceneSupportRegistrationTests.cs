// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    public void IndependentUseCreditsShareWorldHeadroomAcrossUses()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(support);
        var defaults = WorldSchedulingLimits.Default;
        const long maximumOwnedStateBytes = 64L * 1024 * 1024;
        var limits = new WorldSchedulingLimits(
            defaults.MaximumRegistrationBytes,
            defaults.MaximumGraphBytes,
            defaults.MaximumWorkPlanBytes,
            defaults.MaximumCommandStagingBytes,
            maximumOwnedStateBytes,
            defaults.MaximumDiagnosticBytes,
            defaults.MaximumConflictComparisons);
        using var world = World.Create(new WorldOptions(builder.Build()) {
            SchedulingLimits = limits,
        });
        var participant = support.GetParticipant(world);
        var remainingWorldBytes = maximumOwnedStateBytes - world.GetWorldOwnedStateBytes();
        Assert.That(remainingWorldBytes, Is.GreaterThan(512));
        var workspaceLimit = remainingWorldBytes / 2;
        WorldGuidUse firstUse = default;
        WorldGuidUse secondUse = default;
        WorldSceneStorageReservation? extraReservation = null;

        try
        {
            firstUse = world.AcquireGuidUse(workspaceLimit);

            _ = Assert.Throws<InvalidOperationException>(
                () => secondUse = world.AcquireGuidUse(workspaceLimit));
            extraReservation = world.TryReserveSceneStorage(participant, workspaceLimit);
            Assert.That(extraReservation, Is.Null);
            extraReservation?.Dispose();
            extraReservation = null;

            firstUse.Dispose();
            firstUse = default;
            secondUse = world.AcquireGuidUse(workspaceLimit);
        }
        finally
        {
            extraReservation?.Dispose();
            secondUse.Dispose();
            firstUse.Dispose();
        }
    }

    [Test]
    public void MeasuresSceneStorageReservationConstruction()
    {
        var worldRegistrationBuilder = new WorldRegistrationBuilder(CreateSchemaRegistry());
        var support = SceneSupport.CreateRegistration();
        _ = worldRegistrationBuilder.RegisterSceneSupport(support);
        using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
        using var use = world.AcquireGuidUse(workspaceLimit: 4096);
        var participant = support.GetParticipant(world);

        var warmReservation = world.TryReserveSceneStorage(participant, 512, use.Control)
            ?? throw new InvalidOperationException("The warm Scene storage reservation was unexpectedly refused.");
        warmReservation.Dispose();

        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var measuredReservation = world.TryReserveSceneStorage(participant, 512, use.Control)
            ?? throw new InvalidOperationException("The measured Scene storage reservation was unexpectedly refused.");
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        TestContext.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}/{RuntimeInformation.ProcessArchitecture}");
        TestContext.WriteLine($"WorldSceneStorageReservation construction: {allocatedBytes} bytes");
        Assert.Multiple(() => {
            Assert.That(allocatedBytes, Is.EqualTo(72));
            Assert.That(measuredReservation.Bytes, Is.EqualTo(584));
            Assert.That(measuredReservation.DataBytes, Is.EqualTo(512));
        });
        GC.KeepAlive(measuredReservation);
        measuredReservation.Dispose();
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
    public void SceneActiveWorkspaceTransfersIntoRetainedStorageWithoutReturningToTheUse()
    {
        var builder = new WorldRegistrationBuilder(CreateSchemaRegistry());
        var support = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(support);
        using var world = World.Create(new WorldOptions(builder.Build()));
        using var use = world.AcquireGuidUse(workspaceLimit: 1024);
        var participant = support.GetParticipant(world);
        var baselineWorldBytes = world.GetWorldOwnedStateBytes();
        using var reservation = world.TryReserveSceneWorkspace(participant, use.Control, 256)
            ?? throw new InvalidOperationException("The Scene workspace reservation was unexpectedly refused.");

        Assert.That(
            use.Control.TryTransferActiveWorkspaceToWorldReservation(use.Control.DomainReservation, 512),
            Is.False);
        Assert.That(use.Control.Anchor.ActiveWorkspaceBytes, Is.EqualTo(328));
        Assert.That(use.Control.Anchor.OperationBytes, Is.EqualTo(WorldGuidUseControl.ControlByteCharge + 1024));

        Assert.That(reservation.TryTransferWorkspaceToStorage(), Is.True);
        Assert.Multiple(() => {
            Assert.That(use.Control.WorkspaceLimit, Is.EqualTo(696));
            Assert.That(use.Control.Anchor.ActiveWorkspaceBytes, Is.Zero);
            Assert.That(use.Control.Anchor.OperationBytes, Is.EqualTo(WorldGuidUseControl.ControlByteCharge + 696));
            Assert.That(use.Control.DomainReservation.IndependentUseBytes, Is.EqualTo(WorldGuidUseControl.ControlByteCharge + 696));
            Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(baselineWorldBytes + 328));
        });

        reservation.Dispose();
        Assert.Multiple(() => {
            Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(baselineWorldBytes));
            Assert.That(use.Control.WorkspaceLimit, Is.EqualTo(696));
            Assert.That(use.Control.Anchor.OperationBytes, Is.EqualTo(WorldGuidUseControl.ControlByteCharge + 696));
        });
    }

    [Test]
    public void SceneAssetCodecReceiptsRequireTheBuiltInRegistrationPath()
    {
        var schemas = CreateSchemaRegistry();
        var profiledBuilder = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas));
        var profiledRegistration = profiledBuilder.Build();

        Assert.That(SceneSupport.HasBuiltInAssetCodecReceipts(profiledRegistration), Is.True);

        var ordinaryBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = ordinaryBuilder.RegisterCodec(new SceneAssetCodec());
        _ = ordinaryBuilder.RegisterCodec(new ScenePrefabAssetCodec());

        Assert.That(SceneSupport.HasBuiltInAssetCodecReceipts(ordinaryBuilder.Build()), Is.False);
    }

    [Test]
    public void SceneExpansionReservationSourceUsesWorldAndPreparationOwners()
    {
        var worldRegistrationBuilder = new WorldRegistrationBuilder(CreateSchemaRegistry());
        var support = SceneSupport.CreateRegistration();
        _ = worldRegistrationBuilder.RegisterSceneSupport(support);
        using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
        var registry = (SceneWorldRegistry)world.GetSceneParticipant(support);
        TestContext.Progress.WriteLine(
            $"Scene reservation payload sizes: limits {Unsafe.SizeOf<SceneExpansionLimits>()} B, source {Unsafe.SizeOf<SceneExpansionReservationSource>()} B.");

        var worldSource = SceneExpansionReservationSource.ForWorld(
            world,
            registry,
            useControl: null,
            useRestoredStorage: false);
        using var worldReservation = worldSource.ReserveStorage(512)
            ?? throw new InvalidOperationException("The World-owned Scene reservation was unexpectedly refused.");
        Assert.That(worldReservation.DataBytes, Is.EqualTo(512));

        using var use = world.AcquireGuidUse(workspaceLimit: 4096);
        var preparationSource = SceneExpansionReservationSource.ForWorld(
            world,
            registry,
            use.Control,
            useRestoredStorage: false);
        using var preparationStorage = preparationSource.ReserveStorage(512)
            ?? throw new InvalidOperationException("The preparation-owned Scene reservation was unexpectedly refused.");
        using var preparationWorkspace = preparationSource.ReserveWorkspace(256)
            ?? throw new InvalidOperationException("The preparation workspace reservation was unexpectedly refused.");
        Assert.Multiple(() => {
            Assert.That(preparationStorage.DataBytes, Is.EqualTo(512));
            Assert.That(preparationWorkspace.DataBytes, Is.EqualTo(256));
        });
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
        var maximumOwnedStateBytes = checked(baselineWorldBytes + (admittedReservationCount * (reservationBytes + 72)));
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
    public void SceneWorkspaceReturnsToIndependentUseWhileRetainedStorageTransfersOut()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(support);
        using var world = World.Create(new WorldOptions(builder.Build()));
        var participant = support.GetParticipant(world);
        WorldGuidUse? oversizedUse = null;

        try
        {
            _ = Assert.Throws<InvalidOperationException>(
                () => oversizedUse = world.AcquireGuidUse(workspaceLimit: 3_000_000_000));
        }
        finally
        {
            oversizedUse?.Dispose();
        }

        var use = world.AcquireGuidUse(workspaceLimit: 1024);
        WorldSceneStorageReservation? workspaceReservation = null;
        WorldSceneStorageReservation? retainedReservation = null;

        try
        {
            var admittedWorldBytes = world.GetWorldOwnedStateBytes();
            workspaceReservation = world.TryReserveSceneWorkspace(participant, use.Control, 256);

            Assert.That(workspaceReservation, Is.Not.Null);
            var admittedWorkspaceReservation = workspaceReservation
                ?? throw new InvalidOperationException("The Scene workspace reservation was unexpectedly refused.");
            Assert.That(use.Control.WorkspaceLimit, Is.EqualTo(696));
            Assert.That(use.Control.Anchor.ActiveWorkspaceBytes, Is.EqualTo(328));
            Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(admittedWorldBytes));

            admittedWorkspaceReservation.Dispose();
            workspaceReservation = null;

            Assert.That(use.Control.WorkspaceLimit, Is.EqualTo(1024));
            Assert.That(use.Control.Anchor.ActiveWorkspaceBytes, Is.Zero);

            retainedReservation = world.TryReserveSceneStorage(participant, 512, use.Control);

            Assert.That(retainedReservation, Is.Not.Null);
            var admittedRetainedReservation = retainedReservation
                ?? throw new InvalidOperationException("The retained Scene reservation was unexpectedly refused.");
            Assert.That(use.Control.WorkspaceLimit, Is.EqualTo(440));
            Assert.That(use.Control.Anchor.OperationBytes, Is.EqualTo(WorldGuidUseControl.ControlByteCharge + 440));
            Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(admittedWorldBytes + 584));

            admittedRetainedReservation.Dispose();
            retainedReservation = null;

            Assert.That(use.Control.WorkspaceLimit, Is.EqualTo(440));
            Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(admittedWorldBytes));
        }
        finally
        {
            retainedReservation?.Dispose();
            workspaceReservation?.Dispose();
            use.Dispose();
        }
    }

    [Test]
    public void SceneWorkspaceTransferRollbackReturnsPreviouslyTransferredCapacity()
    {
        var worldRegistrationBuilder = new WorldRegistrationBuilder(CreateSchemaRegistry());
        _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
        using var use = world.AcquireGuidUse(4096);
        var control = use.Control;
        var reservation = control.DomainReservation;
        var originalBytes = reservation.Bytes;

        Assert.That(control.TryTransferWorkspaceToWorldReservation(reservation, 3072), Is.True);
        Assert.That(reservation.IndependentUseBytes, Is.EqualTo(1280));
        Assert.That(control.TryReturnWorkspaceFromWorldReservation(reservation, 3072), Is.True);
        Assert.That(control.WorkspaceLimit, Is.EqualTo(4096));
        Assert.That(control.Anchor.OperationBytes, Is.EqualTo(4352));
        Assert.That(reservation.IndependentUseBytes, Is.EqualTo(4352));
        Assert.That(reservation.Bytes, Is.EqualTo(originalBytes));
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

    [Test]
    public void WorldIndependentUseAndSceneStorageRaceForTheSameHeadroom()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(support);
        var registration = builder.Build();
        const long maximumOwnedStateBytes = 64L * 1024 * 1024;
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
        var remainingWorldBytes = maximumOwnedStateBytes - world.GetWorldOwnedStateBytes();
        Assert.That(remainingWorldBytes, Is.GreaterThan(512));
        var workspaceLimit = remainingWorldBytes / 2;
        WorldGuidUse use = default;
        WorldSceneStorageReservation? reservation = null;
        InvalidOperationException? useRefusal = null;

        Parallel.Invoke(
            () => {
                try
                {
                    use = world.AcquireGuidUse(workspaceLimit);
                }
                catch (InvalidOperationException exception)
                {
                    useRefusal = exception;
                }
            },
            () => reservation = world.TryReserveSceneStorage(participant, workspaceLimit));

        try
        {
            Assert.That(use.IsValid ^ (reservation is not null), Is.True);
            Assert.That(use.IsValid, Is.EqualTo(useRefusal is null));
        }
        finally
        {
            reservation?.Dispose();
            use.Dispose();
        }

        Assert.That(world.GetWorldOwnedStateBytes(), Is.EqualTo(maximumOwnedStateBytes - remainingWorldBytes));
    }

    [Test]
    public void WorldIndependentUseGrowsInterestArrayWhenOnlyOneSlotRemains()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(support);
        var registration = builder.Build();
        using var domain = new WorldAdmissionDomain(8_589_934_592L);
        var worlds = new World?[15];
        var interestArrayField = typeof(WorldAdmissionDomain).GetField(
            "_interests",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The World admission domain omitted its interest array.");
        var activeInterestCountField = typeof(WorldAdmissionDomain).GetField(
            "_activeInterestCount",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The World admission domain omitted its active-interest count.");

        try
        {
            for (var index = 0; index < worlds.Length; index++)
            {
                worlds[index] = World.Create(new WorldOptions(registration) {
                    AdmissionDomain = domain,
                });
            }

            var interestsBeforeUse = (Array?)interestArrayField.GetValue(domain)
                ?? throw new InvalidOperationException("The World admission domain lost its interest array.");
            Assert.That(interestsBeforeUse.Length, Is.EqualTo(16));
            Assert.That(activeInterestCountField.GetValue(domain), Is.EqualTo(15));

            var use = (worlds[0]
                ?? throw new InvalidOperationException("The first World was not created."))
                .AcquireGuidUse(workspaceLimit: 1024);

            try
            {
                var interestsAfterUse = (Array?)interestArrayField.GetValue(domain)
                    ?? throw new InvalidOperationException("The World admission domain lost its grown interest array.");
                Assert.That(interestsAfterUse.Length, Is.EqualTo(32));
                Assert.That(activeInterestCountField.GetValue(domain), Is.EqualTo(17));
            }
            finally
            {
                use.Dispose();
            }

            Assert.That(activeInterestCountField.GetValue(domain), Is.EqualTo(15));
        }
        finally
        {
            foreach (var world in worlds)
            {
                world?.Dispose();
            }
        }
    }

    [Test]
    public void WorldRetirementWaitsForIndependentGuidUseRelease()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();
        _ = builder.RegisterSceneSupport(support);
        var registration = builder.Build();
        var worldGuid = Guid.NewGuid();
        var world = World.Create(new WorldOptions(registration) {
            WorldGuid = worldGuid,
        });
        var use = world.AcquireGuidUse(workspaceLimit: 1024);
        World? conflictingWorld = null;
        World? replacementWorld = null;

        try
        {
            world.Dispose();

            _ = Assert.Throws<ArgumentException>(() =>
                conflictingWorld = World.Create(new WorldOptions(registration) {
                    WorldGuid = worldGuid,
                }));

            use.Dispose();
            replacementWorld = World.Create(new WorldOptions(registration) {
                WorldGuid = worldGuid,
            });
        }
        finally
        {
            replacementWorld?.Dispose();
            conflictingWorld?.Dispose();
            world.Dispose();
            use.Dispose();
        }
    }
}
