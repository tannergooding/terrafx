// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldCreationTests
{
    private static WorldRegistration CreateEmptyRegistration()
        => new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();

    private static WorldRegistration CreateReferenceCommandRegistration(out DeferredReferenceProbe probe)
    {
        var schemas = new CompiledContentSchemaRegistry([]);
        var component = new DeferredReferenceRegistration();
        var description = new SystemDescription(
            Guid.NewGuid(),
            0,
            SimulationStage.Input,
            [new SystemResourceAccess(
                SystemResourceKind.Component,
                component.SchemaReference.SchemaId,
                SystemAccessMode.Write)],
            [],
            [],
            SystemThreadMode.OwnerThread);
        probe = new DeferredReferenceProbe(component.Path);
        var factory = WorldSystemFactoryBinding.Stateless<
            DeferredReferenceSystem,
            DeferredReferenceSystemCreate,
            DeferredReferenceSystemRelease>(
                Guid.NewGuid(),
                1,
                new WorldSystemSourceStateDeclaration(typeof(DeferredReferenceSystem), []),
                new DeferredReferenceSystemCreate(probe),
                default);
        var schedule = SystemExecutionSchedule.Compile([description], WorldSchedulingLimits.Default);
        return new WorldRegistration(
            schemas,
            [component],
            [],
            systems: [new SystemRegistration(description, factory)],
            schedule: schedule);
    }

    private static WorldRegistration CreateSetCommandRegistration(
        int value,
        out DeferredSetProbe probe,
        out TestComponentRegistration component)
    {
        var schemas = new CompiledContentSchemaRegistry([]);
        component = new TestComponentRegistration();
        var description = new SystemDescription(
            Guid.NewGuid(),
            0,
            SimulationStage.Input,
            [new SystemResourceAccess(
                SystemResourceKind.Component,
                component.SchemaReference.SchemaId,
                SystemAccessMode.Write)],
            [],
            [],
            SystemThreadMode.OwnerThread);
        probe = new DeferredSetProbe(component.SchemaReference.SchemaId, value);
        var factory = WorldSystemFactoryBinding.Stateless<
            DeferredSetSystem,
            DeferredSetSystemCreate,
            DeferredSetSystemRelease>(
                Guid.NewGuid(),
                1,
                new WorldSystemSourceStateDeclaration(typeof(DeferredSetSystem), []),
                new DeferredSetSystemCreate(probe),
                default);
        var schedule = SystemExecutionSchedule.Compile([description], WorldSchedulingLimits.Default);
        return new WorldRegistration(
            schemas,
            [component],
            [],
            systems: [new SystemRegistration(description, factory)],
            schedule: schedule);
    }

    private static WorldRegistration CreateStructuralCommandRegistration(
        out DeferredStructuralProbe probe,
        out TestComponentRegistration component)
    {
        var schemas = new CompiledContentSchemaRegistry([]);
        component = new TestComponentRegistration();
        var tagId = Guid.NewGuid();
        probe = new DeferredStructuralProbe(tagId);
        var description = new SystemDescription(
            Guid.NewGuid(),
            0,
            SimulationStage.Input,
            [new SystemResourceAccess(
                SystemResourceKind.Component,
                component.SchemaReference.SchemaId,
                SystemAccessMode.Write)],
            [],
            [],
            SystemThreadMode.OwnerThread);
        var factory = WorldSystemFactoryBinding.Stateless<
            DeferredStructuralSystem,
            DeferredStructuralSystemCreate,
            DeferredStructuralSystemRelease>(
                Guid.NewGuid(),
                1,
                new WorldSystemSourceStateDeclaration(typeof(DeferredStructuralSystem), []),
                new DeferredStructuralSystemCreate(probe),
                default);
        var schedule = SystemExecutionSchedule.Compile([description], WorldSchedulingLimits.Default);
        return new WorldRegistration(
            schemas,
            [component],
            [new EntityTagSchema(tagId, 1)],
            systems: [new SystemRegistration(description, factory)],
            schedule: schedule);
    }

    private static ComponentFactoryDescriptor CreateTestDescriptor(
        ContentSchemaReference schema,
        ComponentLayout layout,
        ComponentStorageFlags flags,
        ComponentTransientRestore restore,
        ImmutableArray<ComponentDerivedDependency> dependencies)
        => new(
            schema,
            layout,
            new ComponentStorageDeclaration(
                layout.LogicalBytes,
                [],
                [new ComponentPaddingInterval(0, layout.LogicalBytes)]),
            restore,
            flags,
            Guid.NewGuid(),
            1,
            [],
            dependencies);

    [Test]
    public void AdmissionCreatesIdentityFramesAndWorldOwnedDiagnostics()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var worldGuid = Guid.NewGuid();
        var lineageId = Guid.NewGuid();
        var options = new WorldOptions(CreateEmptyRegistration()) {
            AdmissionDomain = domain,
            InitialReplayLineageId = lineageId,
            WorldGuid = worldGuid,
        };
        var world = World.Create(options);

        Assert.That(world.WorldGuid, Is.EqualTo(worldGuid));
        Assert.That(world.ReplayLineageId, Is.EqualTo(lineageId));
        Assert.That(world.IdentityBranchId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(world.OwnerId, Is.Not.Zero);
        Assert.That(world.TickRate, Is.EqualTo(60));
        Assert.That(world.Profile, Is.EqualTo(SimulationProfile.Standard));
        Assert.That(world.CurrentFrame3.OwnerId, Is.EqualTo(world.OwnerId));
        Assert.That(world.CurrentFrame3.Revision, Is.EqualTo(1));
        Assert.That(world.CurrentFrame3.Minimum, Is.EqualTo(new TerraFX.Numerics.Vector3(-4096, -4096, -4096)));
        Assert.That(world.CurrentFrame3.Maximum, Is.EqualTo(new TerraFX.Numerics.Vector3(4096, 4096, 4096)));
        Assert.That(world.CurrentFrame2, Is.Not.Null);
        var diagnostics = world.Diagnostics;
        Assert.That(diagnostics.GetSnapshot(new DiagnosticQuery(8, DiagnosticSeverity.Trace, null, null)).Records, Is.Empty);

        world.Dispose();

        Assert.That(() => _ = world.Diagnostics, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(
            () => diagnostics.GetSnapshot(new DiagnosticQuery(8, DiagnosticSeverity.Trace, null, null)),
            Throws.TypeOf<ObjectDisposedException>());
        Assert.That(domain.Retirement.IsCompleted, Is.False);
        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void ComponentAndTagTransitionsPreserveSharedColumnValues()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var tagId = Guid.NewGuid();
        var registration = new WorldRegistration(
            new CompiledContentSchemaRegistry([]),
            [new TestComponentRegistration()],
            [new EntityTagSchema(tagId, 1)]);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var initialValue = 37;
        var entity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in initialValue)]));

        world.AddTag(entity, tagId);
        Assert.That(world.HasTag(entity, tagId), Is.True);
        Assert.That(world.TryGet<int>(entity, out var value), Is.True);
        Assert.That(value, Is.EqualTo(initialValue));

        world.RemoveTag(entity, tagId);
        Assert.That(world.HasTag(entity, tagId), Is.False);
        Assert.That(world.TryGet<int>(entity, out value), Is.True);
        Assert.That(value, Is.EqualTo(initialValue));

        var emptyEntity = world.CreateEntity(new EntityDescription());
        var addedValue = 108;
        world.Add(emptyEntity, in addedValue);
        Assert.That(world.TryGet<int>(emptyEntity, out value), Is.True);
        Assert.That(value, Is.EqualTo(addedValue));
        world.Remove<int>(emptyEntity);
        Assert.That(world.Has<int>(emptyEntity), Is.False);

        _ = Assert.Throws<InvalidOperationException>(() => world.RemoveTag(entity, tagId));
        world.AddTag(entity, tagId);
        _ = Assert.Throws<InvalidOperationException>(() => world.AddTag(entity, tagId));
        _ = Assert.Throws<KeyNotFoundException>(() => world.HasTag(entity, Guid.NewGuid()));
    }

    [Test]
    public void ComponentValidationRejectsBeforeCommitAndFaultsInvalidWritableQuery()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registration = new WorldRegistration(
            new CompiledContentSchemaRegistry([]),
            [new TestComponentRegistration()],
            []);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var invalidValue = -1;

        _ = Assert.Throws<ArgumentException>(() => world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in invalidValue)])));

        var validValue = 15;
        var entity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in validValue)]));
        _ = Assert.Throws<ArgumentException>(() => world.Set(entity, in invalidValue));
        Assert.That(world.TryGet<int>(entity, out var storedValue), Is.True);
        Assert.That(storedValue, Is.EqualTo(validValue));

        var schemaId = registration.Components[0].SchemaReference.SchemaId;
        var query = new EntityQueryDescription(
            [schemaId],
            [],
            [new SystemResourceAccess(SystemResourceKind.Component, schemaId, SystemAccessMode.Write)]);
        var writer = new InvalidQueryWriter();

        _ = Assert.Throws<InvalidOperationException>(() => world.ForEachChunk(query, ref writer));
        Assert.That(world.LastFault, Is.Not.Null);
        Assert.That(world.LastFault!.Code, Is.EqualTo("TEST_INVALID_COMPONENT"));
        var diagnosticSnapshot = world.Diagnostics.GetSnapshot(new DiagnosticQuery(
            8,
            DiagnosticSeverity.Error,
            world.WorldGuid,
            null));
        Assert.That(diagnosticSnapshot.Records, Has.Length.EqualTo(1));
    }

    [Test]
    public void DeferredCreateReservedUuidV8FaultsBeforeAdmission()
    {
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.Create = true;
        probe.CreateDescription = new EntityDescription(
            new Guid("00000000-0000-8000-8000-000000000001"));

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(((ArgumentException)exception.InnerException!).ParamName, Is.EqualTo("description"));
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredDestroyLiveEntitiesCommitReceiptIdentitiesInCommandOrder()
    {
        var firstStableId = Guid.NewGuid();
        var secondStableId = Guid.NewGuid();
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var firstEntity = world.CreateEntity(new EntityDescription(firstStableId));
        var secondEntity = world.CreateEntity(new EntityDescription(secondStableId));
        var firstIdentity = world.GetEntityIdentity(firstEntity);
        var secondIdentity = world.GetEntityIdentity(secondEntity);
        probe.Entity = firstEntity;
        probe.SecondEntity = secondEntity;
        probe.DestroyLiveEntity = true;
        probe.DestroySecondLiveEntity = true;

        var publication = world.Step().Publication;

        Assert.That(publication.CommandReceipts, Has.Length.EqualTo(1));
        Assert.That(
            publication.CommandReceipts[0].DestroyedEntityIdentities,
            Is.EqualTo(ImmutableArray.Create(firstIdentity, secondIdentity)));
        Assert.That(world.TryGetEntity(firstStableId, out _), Is.False);
        Assert.That(world.TryGetEntity(secondStableId, out _), Is.False);
    }

    [Test]
    public void DeferredDestroyLiveEntityRejectsASecondDestroyInTheSameBatch()
    {
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.Entity = world.CreateEntity(new EntityDescription());
        probe.DestroyLiveEntity = true;
        probe.DestroyLiveTwice = true;

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredDestroyLiveEntityValidatesModeBeforeTargetAuthority()
    {
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.DestroyLiveEntity = true;
        probe.DestroyMode = (HierarchyDestroyMode)2;

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(((ArgumentOutOfRangeException)exception.InnerException!).ParamName, Is.EqualTo("mode"));
    }

    [Test]
    public void DeferredDestroyTokenCancelsCreateAndEarlierTokenCommands()
    {
        var value = 15;
        var stableId = Guid.NewGuid();
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.Create = true;
        probe.UseTokenCommands = true;
        probe.DestroyCreatedToken = true;
        probe.CreateDescription = new EntityDescription(
            stableId,
            components: [ComponentInitialization.Value(in value)]);

        var receipt = world.Step();
        var commandReceipt = receipt.Publication.GetCommandReceipt(probe.Token);

        Assert.That(commandReceipt.TryResolve(probe.Token, out var canceledEntity), Is.False);
        Assert.That(canceledEntity, Is.EqualTo(default(Entity)));
        Assert.That(world.TryGetEntity(stableId, out _), Is.False);
        Assert.That(commandReceipt.DestroyedEntityIdentities, Is.Empty);
    }

    [Test]
    public void DeferredDestroyTokenRejectsLaterCommandsAgainstTheCanceledToken()
    {
        var stableId = Guid.NewGuid();
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.Create = true;
        probe.DestroyCreatedToken = true;
        probe.CommandAfterTokenDestroy = true;
        probe.CreateDescription = new EntityDescription(stableId);

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(((ArgumentException)exception.InnerException!).ParamName, Is.EqualTo("entity"));
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredDestroyTokenValidatesModeBeforeTokenAuthority()
    {
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.Create = true;
        probe.DestroyCreatedToken = true;
        probe.DestroyInvalidToken = true;
        probe.DestroyMode = (HierarchyDestroyMode)2;
        probe.CreateDescription = new EntityDescription();

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(((ArgumentOutOfRangeException)exception.InnerException!).ParamName, Is.EqualTo("mode"));
    }

    [Test]
    public void DeferredReferenceCommandsSetAndClearOptionalFieldsAtTheStructuralBarrier()
    {
        var absent = new OptionalEntityReferenceComponent(false, default);
        var registration = CreateReferenceCommandRegistration(out var probe);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var target = world.CreateEntity(new EntityDescription());
        var owner = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in absent)]));
        probe.Owner = owner;
        probe.Target = target;
        probe.SetBeforeReference = true;

        _ = world.Step();

        Assert.That(world.TryGet<OptionalEntityReferenceComponent>(owner, out var storedValue), Is.True);
        Assert.That(storedValue.IsPresent, Is.True);
        Assert.That(storedValue.Target, Is.EqualTo(target));

        probe.Target = null;
        probe.UseTokenTarget = true;
        _ = world.Step();

        Assert.That(world.TryGet<OptionalEntityReferenceComponent>(owner, out storedValue), Is.True);
        Assert.That(storedValue.IsPresent, Is.False);
        Assert.That(storedValue.Target, Is.EqualTo(default(Entity)));
    }

    [Test]
    public void DeferredReferenceDestroyAllowsAReferenceClearedInTheSameBatch()
    {
        var registration = CreateReferenceCommandRegistration(out var probe);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var target = world.CreateEntity(new EntityDescription());
        var targetIdentity = world.GetEntityIdentity(target);
        var ownerValue = new OptionalEntityReferenceComponent(true, target);
        var owner = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in ownerValue)]));
        probe.Owner = owner;
        probe.Target = target;
        probe.ClearAndDestroyTarget = true;

        var publication = world.Step().Publication;

        Assert.That(world.TryGetEntity(targetIdentity.EntityId, out _), Is.False);
        Assert.That(world.TryGet<OptionalEntityReferenceComponent>(owner, out var updatedOwner), Is.True);
        Assert.That(updatedOwner.IsPresent, Is.False);
        Assert.That(
            publication.CommandReceipts[0].DestroyedEntityIdentities,
            Is.EqualTo(ImmutableArray.Create(targetIdentity)));
    }

    [Test]
    public void DeferredReferenceDestroyRejectsAReferenceThatRemainsInTheBatch()
    {
        var registration = CreateReferenceCommandRegistration(out var probe);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var target = world.CreateEntity(new EntityDescription());
        var ownerValue = new OptionalEntityReferenceComponent(true, target);
        var owner = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in ownerValue)]));
        probe.Owner = owner;
        probe.Target = target;
        probe.DestroyLiveTarget = true;

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredReferenceForeignTargetFaultsBeforeAdmission()
    {
        var absent = new OptionalEntityReferenceComponent(false, default);
        var registration = CreateReferenceCommandRegistration(out var probe);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        using var foreignWorld = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var owner = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in absent)]));
        var target = foreignWorld.CreateEntity(new EntityDescription());
        probe.Owner = owner;
        probe.Target = target;

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(((ArgumentException)exception.InnerException!).ParamName, Is.EqualTo("target"));
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredReferenceTokenTargetCanceledCreateRejectsTheCompleteBatch()
    {
        var absent = new OptionalEntityReferenceComponent(false, default);
        var registration = CreateReferenceCommandRegistration(out var probe);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.Owner = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in absent)]));
        probe.CreateReferenceTargets = true;
        probe.DestroyTargetAfterReferences = true;

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredReferenceTokenTargetRefusalUsesTheDeclaredArgument()
    {
        var absent = new OptionalEntityReferenceComponent(false, default);
        var registration = CreateReferenceCommandRegistration(out var probe);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var owner = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in absent)]));
        probe.Owner = owner;
        probe.TargetToken = new EntityToken(Guid.NewGuid(), 0);
        probe.UseTokenTarget = true;

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(((ArgumentException)exception.InnerException!).ParamName, Is.EqualTo("target"));
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredReferenceTokenTargetsResolveAgainstCreatedEntitiesInTheBatch()
    {
        var absent = new OptionalEntityReferenceComponent(false, default);
        var registration = CreateReferenceCommandRegistration(out var probe);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.Owner = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in absent)]));
        probe.CreateReferenceTargets = true;

        var receipt = world.Step();

        Assert.That(receipt.Publication.CommandReceipts, Has.Length.EqualTo(1));
        var targetToken = probe.TargetToken
            ?? throw new InvalidOperationException("The reference target token was not issued.");
        var commandReceipt = receipt.Publication.GetCommandReceipt(targetToken);
        Assert.That(commandReceipt.TryResolve(targetToken, out var target), Is.True);
        Assert.That(commandReceipt.TryResolve(probe.TokenOwner, out var owner), Is.True);
        Assert.That(world.TryGet<OptionalEntityReferenceComponent>(probe.Owner, out var liveOwnerValue), Is.True);
        Assert.That(liveOwnerValue.IsPresent, Is.True);
        Assert.That(liveOwnerValue.Target, Is.EqualTo(target));
        Assert.That(world.TryGet<OptionalEntityReferenceComponent>(owner, out var tokenOwnerValue), Is.True);
        Assert.That(tokenOwnerValue.IsPresent, Is.True);
        Assert.That(tokenOwnerValue.Target, Is.EqualTo(target));
    }

    [Test]
    public void DeferredSetCommandCommitsAtStructuralBarrierAndPublishesReceipt()
    {
        var initialValue = 15;
        var commandValue = 42;
        var registration = CreateSetCommandRegistration(commandValue, out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var entity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in initialValue)]));
        probe.Entity = entity;

        var receipt = world.Step();

        Assert.That(world.TryGet<int>(entity, out var storedValue), Is.True);
        Assert.That(storedValue, Is.EqualTo(commandValue));
        Assert.That(probe.ObservedValue, Is.EqualTo(initialValue));
        Assert.That(receipt.Publication.CommandReceipts, Has.Length.EqualTo(1));
        Assert.That(receipt.Publication.CommandReceipts[0].DestroyedEntityIdentities, Is.Empty);
    }

    [Test]
    public void DeferredSetCommandLimitFaultsBeforeAnyWrite()
    {
        var initialValue = 15;
        var secondInitialValue = 16;
        var commandValue = 42;
        var registration = CreateSetCommandRegistration(commandValue, out var probe, out var component);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            MaximumDeferredCommands = 1,
            WorldGuid = Guid.NewGuid(),
        });
        var entity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in initialValue)]));
        var secondEntity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in secondInitialValue)]));
        probe.Entity = entity;
        probe.SecondEntity = secondEntity;
        var initialInitializationCount = component.InitializeColumnCalls;

        _ = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(component.InitializeColumnCalls, Is.EqualTo(initialInitializationCount));
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredSetInvalidValueFaultsBeforeMutatingTheComponent()
    {
        var initialValue = 15;
        var invalidValue = -1;
        var registration = CreateSetCommandRegistration(invalidValue, out var probe, out var component);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var entity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in initialValue)]));
        probe.Entity = entity;
        var initialInitializationCount = component.InitializeColumnCalls;

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(component.InitializeColumnCalls, Is.EqualTo(initialInitializationCount));
        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredSetQueryPartitionsProduceDistinctOrderedReceipts()
    {
        var commandValue = 42;
        var registration = CreateSetCommandRegistration(commandValue, out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var entities = new List<Entity>(65);

        for (var index = 0; index < 65; index++)
        {
            var value = index;
            entities.Add(world.CreateEntity(new EntityDescription(
                components: [ComponentInitialization.Value(in value)])));
        }

        probe.WriteFromQuery = true;
        var receipt = world.Step();

        Assert.That(receipt.Publication.CommandReceipts, Has.Length.EqualTo(2));

        foreach (var entity in entities)
        {
            Assert.That(world.TryGet<int>(entity, out var value), Is.True);
            Assert.That(value, Is.EqualTo(commandValue));
        }
    }

    [Test]
    public void DeferredSetStagingByteLimitFaultsBeforeAnyWrite()
    {
        var initialValue = 15;
        var commandValue = 42;
        var registration = CreateSetCommandRegistration(commandValue, out var probe, out var component);
        var defaults = WorldSchedulingLimits.Default;
        var limits = new WorldSchedulingLimits(
            defaults.MaximumRegistrationBytes,
            defaults.MaximumGraphBytes,
            defaults.MaximumWorkPlanBytes,
            4096,
            defaults.MaximumOwnedStateBytes,
            defaults.MaximumDiagnosticBytes,
            defaults.MaximumConflictComparisons);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            SchedulingLimits = limits,
            WorldGuid = Guid.NewGuid(),
        });
        var entity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in initialValue)]));
        probe.Entity = entity;
        probe.CommandCount = 21;
        var initialInitializationCount = component.InitializeColumnCalls;

        _ = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(component.InitializeColumnCalls, Is.EqualTo(initialInitializationCount));
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredStructuralCommandsMigrateComponentsAndTagsTogether()
    {
        var addValue = 15;
        var setValue = 42;
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var entity = world.CreateEntity(new EntityDescription());
        probe.Entity = entity;
        probe.AddValue = addValue;
        probe.SetValue = setValue;

        _ = world.Step();

        Assert.That(world.Has<int>(entity), Is.True);
        Assert.That(world.HasTag(entity, probe.TagId), Is.True);
        Assert.That(world.TryGet<int>(entity, out var storedValue), Is.True);
        Assert.That(storedValue, Is.EqualTo(setValue));

        probe.Remove = true;
        _ = world.Step();

        Assert.That(world.Has<int>(entity), Is.False);
        Assert.That(world.HasTag(entity, probe.TagId), Is.False);
    }

    [Test]
    public void DeferredStructuralCommandsMigrateMultipleEntitiesFromOneSourceArchetype()
    {
        var addValue = 15;
        var setValue = 42;
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var firstEntity = world.CreateEntity(new EntityDescription());
        var secondEntity = world.CreateEntity(new EntityDescription());
        probe.Entity = firstEntity;
        probe.SecondEntity = secondEntity;
        probe.AddValue = addValue;
        probe.SetValue = setValue;

        _ = world.Step();

        Assert.That(world.TryGet<int>(firstEntity, out var firstValue), Is.True);
        Assert.That(firstValue, Is.EqualTo(setValue));
        Assert.That(world.HasTag(firstEntity, probe.TagId), Is.True);
        Assert.That(world.Has<int>(secondEntity), Is.False);
        Assert.That(world.HasTag(secondEntity, probe.TagId), Is.True);
    }

    [Test]
    public void DeferredStructuralCommandsRejectBeforeAnyDestinationRowInitialization()
    {
        var addValue = 15;
        var registration = CreateStructuralCommandRegistration(out var probe, out var component);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.Entity = world.CreateEntity(new EntityDescription());
        probe.SecondEntity = world.CreateEntity(new EntityDescription());
        probe.AddValue = addValue;
        probe.FailAfterAdd = true;

        _ = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(component.InitializeColumnCalls, Is.Zero);
        Assert.That(world.LastFault!.Code, Is.EqualTo("WORLD_STEP_FAILED"));
    }

    [Test]
    public void DeferredStructuralCreateResolvesItsTokenThroughThePublishedReceipt()
    {
        var value = 15;
        var finalValue = 42;
        var registration = CreateStructuralCommandRegistration(out var probe, out _);
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        probe.Create = true;
        probe.CreateTwice = true;
        probe.UseTokenCommands = true;
        probe.SetValue = finalValue;
        probe.CreateDescription = new EntityDescription(
            components: [ComponentInitialization.Value(in value)]);

        var receipt = world.Step();

        Assert.That(probe.Token.IsValid, Is.True);
        Assert.That(receipt.Publication.CommandReceipts, Has.Length.EqualTo(1));
        var commandReceipt = receipt.Publication.GetCommandReceipt(probe.Token);
        Assert.That(commandReceipt, Is.SameAs(receipt.Publication.CommandReceipts[0]));
        Assert.That(commandReceipt.TryResolve(probe.Token, out var entity), Is.True);
        Assert.That(commandReceipt.TryResolve(probe.SecondToken, out var secondEntity), Is.True);
        Assert.That(secondEntity, Is.Not.EqualTo(entity));
        Assert.That(world.TryGet<int>(entity, out var storedValue), Is.True);
        Assert.That(storedValue, Is.EqualTo(finalValue));
        Assert.That(world.HasTag(entity, probe.TagId), Is.True);
        Assert.That(world.TryGet<int>(secondEntity, out storedValue), Is.True);
        Assert.That(storedValue, Is.EqualTo(finalValue));
        Assert.That(world.HasTag(secondEntity, probe.TagId), Is.True);
    }

    [Test]
    public void DestroyRetiresIdentityAndReusesSlotWithNextGeneration()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var stableId = Guid.NewGuid();
        var entity = world.CreateEntity(new EntityDescription(stableId));

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => world.DestroyEntity(entity, (HierarchyDestroyMode)2));
        Assert.That(world.TryGetEntity(stableId, out var stillLive), Is.True);
        Assert.That(stillLive, Is.EqualTo(entity));

        world.DestroyEntity(entity, HierarchyDestroyMode.DetachChildrenKeepWorld);
        Assert.That(world.TryGetEntity(stableId, out var retired), Is.False);
        Assert.That(retired, Is.EqualTo(default(Entity)));
        Assert.That(() => world.GetEntityId(entity), Throws.ArgumentException);

        var replacement = world.CreateEntity(new EntityDescription());
        Assert.That(replacement.Index, Is.EqualTo(entity.Index));
        Assert.That(replacement.Generation, Is.EqualTo(entity.Generation + 1));

        world.Dispose();
        Assert.That(world.IdentityLedger.AssignmentCount, Is.Zero);
    }

    [Test]
    public void DisposedWorldRetiresItsFreshLineageForReuse()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registration = CreateEmptyRegistration();
        var lineageId = Guid.NewGuid();

        using (var firstWorld = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = lineageId,
            WorldGuid = Guid.NewGuid(),
        }))
        {
            Assert.That(firstWorld.ReplayLineageId, Is.EqualTo(lineageId));
        }

        using (var secondWorld = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = lineageId,
            WorldGuid = Guid.NewGuid(),
        }))
        {
            Assert.That(secondWorld.ReplayLineageId, Is.EqualTo(lineageId));
        }

        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void EntityCreationPublishesStableAndWorldQualifiedLookups()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var stableId = Guid.NewGuid();
        var entity = world.CreateEntity(new EntityDescription(stableId));

        Assert.That(entity.IsValid, Is.True);
        Assert.That(entity.Index, Is.EqualTo(1));
        Assert.That(entity.Generation, Is.EqualTo(1));
        Assert.That(world.GetEntityId(entity), Is.EqualTo(stableId));
        Assert.That(world.TryGetEntity(stableId, out var byStableId), Is.True);
        Assert.That(byStableId, Is.EqualTo(entity));

        var identity = world.GetEntityIdentity(entity);
        Assert.That(identity.WorldGuid, Is.EqualTo(world.WorldGuid));
        Assert.That(identity.CreationLineageId, Is.EqualTo(world.ReplayLineageId));
        Assert.That(identity.CreationBranchId, Is.EqualTo(world.IdentityBranchId));
        Assert.That(world.TryGetEntity(identity, out var byQualifiedIdentity), Is.True);
        Assert.That(byQualifiedIdentity, Is.EqualTo(entity));

        Assert.That(world.TryGetEntity(Guid.NewGuid(), out var absent), Is.False);
        Assert.That(absent, Is.EqualTo(default(Entity)));
        Assert.That(() => world.TryGetEntity(Guid.Empty, out _), Throws.ArgumentException);
    }

    [Test]
    public void EntityMembershipTransitionsPreserveDerivedDependencies()
    {
        var persistentAssignment = new ContentSchemaIdentityAssignment("application/component/dependency-test/v1");
        var persistentReference = new ContentSchemaReference(
            persistentAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var persistentMetadata = new DataSchema(
            persistentReference,
            persistentAssignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            []);
        var derivedAssignment = new ContentSchemaIdentityAssignment("application/component/derived-test/v1");
        var derivedReference = new ContentSchemaReference(
            derivedAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var derivedMetadata = new DataSchema(
            derivedReference,
            derivedAssignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Transient,
            ContentTransientRestore.ExactDerivedRehydration,
            []);
        var schemas = new CompiledContentSchemaRegistry([persistentMetadata, derivedMetadata]);
        var persistentSchema = ComponentSchema<int>.FromRegistry(
            schemas,
            persistentReference,
            new ComponentLayout(Guid.NewGuid(), sizeof(int)));
        var derivedSchema = ComponentSchema<long>.FromRegistry(
            schemas,
            derivedReference,
            new ComponentLayout(Guid.NewGuid(), sizeof(long)));
        var dependency = ComponentDerivedDependency.FromSchema(persistentSchema);
        var registration = new WorldRegistration(
            schemas,
            [
                new TestComponentRegistration(CreateTestDescriptor(
                    persistentSchema.Schema,
                    persistentSchema.Layout,
                    ComponentStorageFlags.Persistent,
                    ComponentTransientRestore.None,
                    [])),
                new TestLongComponentRegistration(CreateTestDescriptor(
                    derivedSchema.Schema,
                    derivedSchema.Layout,
                    ComponentStorageFlags.Transient,
                    ComponentTransientRestore.Derived,
                    [dependency])),
            ],
            []);
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var derivedValue = 7L;
        var invalidDescription = new EntityDescription(
            components: [ComponentInitialization.Value(in derivedValue)]);

        _ = Assert.Throws<ArgumentException>(() => world.CreateEntity(invalidDescription));

        var persistentValue = 42;
        var entity = world.CreateEntity(new EntityDescription(components: [
            ComponentInitialization.Value(in persistentValue),
            ComponentInitialization.Value(in derivedValue),
        ]));
        Assert.That(entity.Index, Is.EqualTo(1));
        Assert.That(world.Has<int>(entity), Is.True);
        Assert.That(world.Has<long>(entity), Is.True);

        _ = Assert.Throws<InvalidOperationException>(() => world.Remove<int>(entity));
        Assert.That(world.Has<int>(entity), Is.True);
        Assert.That(world.Has<long>(entity), Is.True);

        world.Remove<long>(entity);
        world.Remove<int>(entity);
        Assert.That(world.Has<int>(entity), Is.False);
        Assert.That(world.Has<long>(entity), Is.False);
    }

    [Test]
    public void FailedEntityAdmissionDoesNotConsumeAnIdentityOrEntitySlot()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var stableId = Guid.NewGuid();

        _ = Assert.Throws<ArgumentException>(() => world.CreateEntity(new EntityDescription(
            stableId,
            tags: [Guid.NewGuid()])));

        Assert.That(world.TryGetEntity(stableId, out var absent), Is.False);
        Assert.That(absent, Is.EqualTo(default(Entity)));

        var entity = world.CreateEntity(new EntityDescription(stableId));
        Assert.That(entity.Index, Is.EqualTo(1));
        Assert.That(world.GetEntityId(entity), Is.EqualTo(stableId));

        _ = Assert.Throws<ArgumentException>(() => world.CreateEntity(new EntityDescription(stableId)));

        var nextEntity = world.CreateEntity(new EntityDescription());
        Assert.That(nextEntity.Index, Is.EqualTo(2));
    }

    [Test]
    public void InvalidOptionsDoNotReserveTheSuppliedWorldGuid()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registration = CreateEmptyRegistration();
        var worldGuid = Guid.NewGuid();
        var invalidOptions = new WorldOptions(registration) {
            AdmissionDomain = domain,
            TickRate = 0,
            WorldGuid = worldGuid,
        };

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = World.Create(invalidOptions));

        using (var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = worldGuid,
        }))
        {
            Assert.That(world.WorldGuid, Is.EqualTo(worldGuid));
        }

        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void OptionalEntityReferenceBindingSkipsAbsentValuesAndValidatesPresentTargets()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        using var foreignWorld = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var assignment = new ContentSchemaIdentityAssignment("application/component/optional-reference-test/v1");
        var schemaReference = new ContentSchemaReference(
            assignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
            1,
            11,
            0,
            false,
            1,
            1,
            [],
            0,
            ContentUnit.None,
            ContentReferenceKind.Entity,
            ContentAuthoring.None,
            null);
        var metadata = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [field]);
        var registry = new CompiledContentSchemaRegistry([metadata]);
        var schema = ComponentSchema<OptionalEntityReferenceComponent>.FromRegistry(
            registry,
            schemaReference,
            new ComponentLayout(Guid.NewGuid(), Unsafe.SizeOf<OptionalEntityReferenceComponent>()));
        var descriptor = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]));
        var storagePath = new ComponentStoragePath([ComponentStorageSegment.Member("Target")]);
        var binding = ComponentFieldBinding<OptionalEntityReferenceComponent, OptionalEntityReferenceAuthor>
            .OptionalEntityReference<OptionalEntityReferenceAccessor, OptionalEntityReferenceReader>(
                descriptor,
                [storagePath],
                default,
                default);
        Assert.That(binding.IsEntityReferencePath(descriptor.Path), Is.True);
        Assert.That(
            binding.IsEntityReferencePath(new ComponentFieldPath([ComponentPropertySegment.FieldTag(2)])),
            Is.False);

        var absent = new OptionalEntityReferenceComponent(isPresent: false, default);
        binding.ValidateEntityReferences(in absent, world, null, null);

        var target = world.CreateEntity(new EntityDescription());
        Assert.That(binding.TrySetEntityReference(ref absent, descriptor.Path, target), Is.True);
        Assert.That(absent.IsPresent, Is.True);
        Assert.That(absent.Target, Is.EqualTo(target));
        var present = new OptionalEntityReferenceComponent(isPresent: true, target);
        binding.ValidateEntityReferences(in present, world, null, null);
        var cleared = present;
        Assert.That(binding.TrySetEntityReference(ref cleared, descriptor.Path, null), Is.True);
        Assert.That(cleared.IsPresent, Is.False);

        var foreignTarget = foreignWorld.CreateEntity(new EntityDescription());
        var foreignPresent = new OptionalEntityReferenceComponent(isPresent: true, foreignTarget);
        _ = Assert.Throws<InvalidOperationException>(
            () => binding.ValidateEntityReferences(in foreignPresent, world, null, null));

        world.DestroyEntity(target, HierarchyDestroyMode.DetachChildrenKeepWorld);
        var stalePresent = new OptionalEntityReferenceComponent(isPresent: true, target);
        _ = Assert.Throws<InvalidOperationException>(
            () => binding.ValidateEntityReferences(in stalePresent, world, null, null));
    }

    [Test]
    public void OptionalNestedAggregateBindingValidatesOnlyPresentEntityReferences()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var nestedAssignment = new ContentSchemaIdentityAssignment("application/descriptor/optional-reference-test/v1");
        var nestedSchemaReference = new ContentSchemaReference(
            nestedAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var nestedField = new ContentFieldDescriptor(
            1,
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
        var nestedSchema = new DataSchema(
            nestedSchemaReference,
            nestedAssignment,
            ContentSchemaRole.Descriptor,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [nestedField]);
        var assignment = new ContentSchemaIdentityAssignment("application/component/optional-nested-reference-test/v1");
        var schemaReference = new ContentSchemaReference(
            assignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var field = new ContentFieldDescriptor(
            1,
            22,
            0,
            false,
            1,
            1,
            [nestedSchemaReference],
            0,
            ContentUnit.None,
            ContentReferenceKind.None,
            ContentAuthoring.None,
            null);
        var metadata = new DataSchema(
            schemaReference,
            assignment,
            ContentSchemaRole.Component,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.Persistent,
            ContentTransientRestore.NotApplicable,
            [field]);
        var registry = new CompiledContentSchemaRegistry([metadata, nestedSchema]);
        var schema = ComponentSchema<OptionalAggregateEntityReferenceComponent>.FromRegistry(
            registry,
            schemaReference,
            new ComponentLayout(Guid.NewGuid(), Unsafe.SizeOf<OptionalAggregateEntityReferenceComponent>()));
        var parentField = schema.GetField(new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]));
        var childField = schema.GetField(new ComponentFieldPath([
            ComponentPropertySegment.FieldTag(1),
            ComponentPropertySegment.FieldTag(1),
        ]));
        var child = ComponentFieldBinding<OptionalAggregateEntityReferenceComponent, OptionalAggregateAuthor>
            .EntityReference<OptionalAggregateEntityAccessor, OptionalAggregateEntityReader>(
                childField,
                [new ComponentStoragePath([
                    ComponentStorageSegment.Member("Nested"),
                    ComponentStorageSegment.Member("Target"),
                ])],
                default,
                default);
        var binding = ComponentFieldBinding<OptionalAggregateEntityReferenceComponent, OptionalAggregateAuthor>
            .OptionalAggregate<OptionalAggregateValue, OptionalAggregateSymbolicValue, OptionalAggregateAccessor, OptionalAggregateReader>(
                parentField,
                [],
                default,
                default,
                [child]);
        var absent = new OptionalAggregateEntityReferenceComponent(isPresent: false, default);
        binding.ValidateEntityReferences(in absent, world, null, null);

        var target = world.CreateEntity(new EntityDescription());
        var present = new OptionalAggregateEntityReferenceComponent(isPresent: true, target);
        binding.ValidateEntityReferences(in present, world, null, null);

        var invalidPresent = new OptionalAggregateEntityReferenceComponent(isPresent: true, default);
        _ = Assert.Throws<InvalidOperationException>(
            () => binding.ValidateEntityReferences(in invalidPresent, world, null, null));
    }

    [Test]
    public void QueryOrdersArchetypesByCanonicalSchemaKeyBeforeSelectingPartitions()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var firstTagId = new Guid("00000000-0000-0000-0000-000000000002");
        var secondTagId = new Guid("00000000-0000-0000-0000-000000000001");
        var registration = new WorldRegistration(
            new CompiledContentSchemaRegistry([]),
            [],
            [new EntityTagSchema(firstTagId, 1), new EntityTagSchema(secondTagId, 1)]);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            MaximumLiveEntities = 4,
            WorldGuid = Guid.NewGuid(),
        });
        var firstEntity = world.CreateEntity(new EntityDescription(tags: [firstTagId]));
        var secondEntity = world.CreateEntity(new EntityDescription(tags: [secondTagId]));
        var range = new SystemPartitionRange(0, 2);
        var query = new EntityQueryDescription([], [], [], range);
        var recorder = new EntityOrderRecorder();
        world.ForEachChunk(query, ref recorder);

        Assert.That(recorder.CallbackCount, Is.EqualTo(2));
        Assert.That(recorder.FirstEntityIndex, Is.EqualTo(secondEntity.Index));
        Assert.That(recorder.SecondEntityIndex, Is.EqualTo(firstEntity.Index));
    }

    [Test]
    public void QueryUsesExactPlanPartitionSlicesAndAdmittedComponentSpans()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var component = new TestComponentRegistration();
        var registration = new WorldRegistration(new CompiledContentSchemaRegistry([]), [component], []);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            MaximumLiveEntities = 65,
            WorldGuid = Guid.NewGuid(),
        });

        for (var index = 0; index < 65; index++)
        {
            var value = index;
            _ = world.CreateEntity(new EntityDescription(components: [ComponentInitialization.Value(in value)]));
        }

        var partitionRange = new SystemPartitionRange(0, 2);
        var readQuery = new EntityQueryDescription(
            [component.SchemaReference.SchemaId],
            [],
            [new SystemResourceAccess(
                SystemResourceKind.Component,
                component.SchemaReference.SchemaId,
                SystemAccessMode.Read,
                partitionRange)],
            partitionRange);
        var recorder = new QueryRecorder();
        world.ForEachChunk(readQuery, ref recorder);

        Assert.That(recorder.CallbackCount, Is.EqualTo(2));
        Assert.That(recorder.EntityCount, Is.EqualTo(65));
        Assert.That(recorder.ValueSum, Is.EqualTo(2080));

        var writeQuery = new EntityQueryDescription(
            [component.SchemaReference.SchemaId],
            [],
            [new SystemResourceAccess(
                SystemResourceKind.Component,
                component.SchemaReference.SchemaId,
                SystemAccessMode.Write,
                partitionRange)],
            partitionRange);
        var writer = new QueryWriter();
        world.ForEachChunk(writeQuery, ref writer);
        Assert.That(world.TryGet<int>(new Entity(world.OwnerId, 1, 1), out var firstValue), Is.True);
        Assert.That(firstValue, Is.EqualTo(1));
        Assert.That(world.TryGet<int>(new Entity(world.OwnerId, 65, 1), out var lastValue), Is.True);
        Assert.That(lastValue, Is.EqualTo(65));

        var wholeChunkQuery = new EntityQueryDescription(
            [component.SchemaReference.SchemaId],
            [],
            [new SystemResourceAccess(
                SystemResourceKind.Component,
                component.SchemaReference.SchemaId,
                SystemAccessMode.Read)]);
        var wholeChunkRecorder = new QueryRecorder();
        world.ForEachChunk(wholeChunkQuery, ref wholeChunkRecorder);
        Assert.That(wholeChunkRecorder.CallbackCount, Is.EqualTo(1));
        Assert.That(wholeChunkRecorder.EntityCount, Is.EqualTo(65));
    }

    [Test]
    public void QueryValidatesSchemaAndRangeBeforeAnEmptySelection()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            MaximumLiveEntities = 4,
            WorldGuid = Guid.NewGuid(),
        });
        var recorder = new QueryRecorder();
        var unknownSchemaQuery = new EntityQueryDescription([Guid.NewGuid()], [], []);

        _ = Assert.Throws<ArgumentException>(() => world.ForEachChunk(unknownSchemaQuery, ref recorder));
        Assert.That(recorder.CallbackCount, Is.Zero);

        var outOfDomainQuery = new EntityQueryDescription([], [], [], new SystemPartitionRange(3, 2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.ForEachChunk(outOfDomainQuery, ref recorder));
        Assert.That(recorder.CallbackCount, Is.Zero);
    }

    [Test]
    public void RegisteredSpatialDefaultsConvertPhysicalExtentsOnce()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var profileId = new SpatialProfileId(Guid.NewGuid());
        var settings = new SpatialSettings(-1, 8);
        SpatialProfiles.Register(profileId, settings);

        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            AdmissionDomain = domain,
            Enable2DFrame = false,
            InitialReplayLineageId = Guid.NewGuid(),
            SpatialProfileId = profileId,
            SpatialSettings = settings,
            WorldGuid = Guid.NewGuid(),
        });

        Assert.That(world.SpatialProfileId, Is.EqualTo(profileId));
        Assert.That(world.SpatialSettings, Is.EqualTo(settings));
        Assert.That(world.CurrentFrame3.Minimum, Is.EqualTo(new TerraFX.Numerics.Vector3(-40_960, -40_960, -40_960)));
        Assert.That(world.CurrentFrame3.Maximum, Is.EqualTo(new TerraFX.Numerics.Vector3(40_960, 40_960, 40_960)));
        Assert.That(world.CurrentFrame2, Is.Null);
        world.Dispose();
        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void TypedWorldAccessReadsAndReplacesRegisteredComponentValues()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registration = new WorldRegistration(
            new CompiledContentSchemaRegistry([]),
            [new TestComponentRegistration()],
            []);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var initialValue = 37;
        var entity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in initialValue)]));

        Assert.That(world.Has<int>(entity), Is.True);
        Assert.That(world.TryGet<int>(entity, out var value), Is.True);
        Assert.That(value, Is.EqualTo(initialValue));

        var updatedValue = 91;
        world.Set(entity, in updatedValue);

        Assert.That(world.TryGet<int>(entity, out value), Is.True);
        Assert.That(value, Is.EqualTo(updatedValue));
        Assert.That(() => world.Has<long>(entity), Throws.InvalidOperationException);
    }

    [Test]
    public void WorldGuidAdmissionIsProcessWideAcrossDomains()
    {
        var firstDomain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var secondDomain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registration = CreateEmptyRegistration();
        var worldGuid = Guid.NewGuid();

        using (var firstWorld = World.Create(new WorldOptions(registration) {
            AdmissionDomain = firstDomain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = worldGuid,
        }))
        {
            _ = Assert.Throws<ArgumentException>(() => _ = World.Create(new WorldOptions(registration) {
                AdmissionDomain = secondDomain,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = worldGuid,
            }));
        }

        using (var replacement = World.Create(new WorldOptions(registration) {
            AdmissionDomain = secondDomain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = worldGuid,
        }))
        {
            Assert.That(replacement.WorldGuid, Is.EqualTo(worldGuid));
        }

        firstDomain.Dispose();
        secondDomain.Dispose();
        Assert.That(firstDomain.Retirement.IsCompleted, Is.True);
        Assert.That(secondDomain.Retirement.IsCompleted, Is.True);
    }

    private sealed class DeferredReferenceProbe
    {
        internal DeferredReferenceProbe(ComponentFieldPath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            Path = path;
        }

        internal bool ClearAndDestroyTarget { get; set; }

        internal bool CreateReferenceTargets { get; set; }

        internal bool DestroyLiveTarget { get; set; }

        internal bool DestroyTargetAfterReferences { get; set; }

        internal Entity Owner { get; set; }

        internal ComponentFieldPath Path { get; }

        internal bool SetBeforeReference { get; set; }

        internal Entity? Target { get; set; }

        internal EntityToken? TargetToken { get; set; }

        internal EntityToken TokenOwner { get; set; }

        internal bool UseTokenTarget { get; set; }
    }

    private sealed class DeferredReferenceRegistration : IComponentRegistration
    {
        private readonly ComponentFactoryDescriptor _descriptor;
        private readonly ComponentFieldPath _path = new([ComponentPropertySegment.FieldTag(1)]);

        internal DeferredReferenceRegistration()
        {
            var schema = new ContentSchemaReference(Guid.NewGuid(), 1, 0, ContentSchemaScope.Content);
            var layout = new ComponentLayout(Guid.NewGuid(), Unsafe.SizeOf<OptionalEntityReferenceComponent>());
            _descriptor = CreateTestDescriptor(
                schema,
                layout,
                ComponentStorageFlags.Persistent,
                ComponentTransientRestore.None,
                []);
            ComponentType = typeof(OptionalEntityReferenceComponent);
            Layout = layout;
            SchemaReference = schema;
        }

        public Type ComponentType { get; }

        public ComponentFactoryDescriptor Descriptor => _descriptor;

        public ComponentLayout Layout { get; }

        public ContentSchemaReference SchemaReference { get; }

        internal ComponentFieldPath Path => _path;

        private static void ValidateEntityReferenceValue(
            in OptionalEntityReferenceComponent value,
            World world,
            Entity? excludedEntity,
            string? parameterName)
        {
            if (value.IsPresent)
            {
                world.ValidateEntityReference(value.Target, excludedEntity, parameterName);
            }
        }

        public ComponentInitialization CaptureColumnRow(object column, int row)
        {
            var value = ((OptionalEntityReferenceComponent[])column)[row];
            return ComponentInitialization.Value(in value);
        }

        public void ClearColumn(object column, int row)
            => ((OptionalEntityReferenceComponent[])column)[row] = default;

        public void CopyColumnRow(object sourceColumn, int sourceRow, object destinationColumn, int destinationRow)
            => ((OptionalEntityReferenceComponent[])destinationColumn)[destinationRow]
                = ((OptionalEntityReferenceComponent[])sourceColumn)[sourceRow];

        public object CreateColumn(int capacity) => new OptionalEntityReferenceComponent[capacity];

        public ComponentInitialization CreateInitializedValue(ComponentInitialization initialization)
        {
            ArgumentNullException.ThrowIfNull(initialization);

            if (initialization.IsDefault)
            {
                var defaultValue = new OptionalEntityReferenceComponent(false, default);
                return ComponentInitialization.Value(in defaultValue);
            }

            if (initialization.TryGetValue<OptionalEntityReferenceComponent>(out _))
            {
                return initialization;
            }

            throw new InvalidOperationException("The test reference component initialization has the wrong type.");
        }

        public long GetColumnArrayBytes(int capacity)
            => checked(64L + ((checked((long)capacity * Unsafe.SizeOf<OptionalEntityReferenceComponent>()) + 7L) & ~7L));

        public void InitializeColumn(object column, int row, ComponentInitialization initialization)
        {
            if (!initialization.TryGetValue<OptionalEntityReferenceComponent>(out var value))
            {
                throw new InvalidOperationException("The test reference component initialization has no value.");
            }

            ((OptionalEntityReferenceComponent[])column)[row] = value;
        }

        public bool IsEntityReferencePath(ComponentFieldPath path)
            => _path.Equals(path);

        public void MoveColumnRow(object column, int sourceRow, int destinationRow)
        {
            var values = (OptionalEntityReferenceComponent[])column;
            values[destinationRow] = values[sourceRow];
        }

        public ComponentInitialization SetEntityReference(
            ComponentInitialization current,
            ComponentFieldPath path,
            Entity? target)
        {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(path);

            if (!_path.Equals(path))
            {
                throw new ArgumentException("The selected path is not a registered Entity-reference field.", nameof(path));
            }

            if (!current.TryGetValue<OptionalEntityReferenceComponent>(out _))
            {
                throw new InvalidOperationException("The current test reference component has no typed value.");
            }

            var value = (target is { } entity)
                ? new OptionalEntityReferenceComponent(true, entity)
                : new OptionalEntityReferenceComponent(false, default);
            return ComponentInitialization.Value(in value);
        }

        public ImmutableArray<Diagnostic> ValidateColumnRow(object column, int row, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }

        public void ValidateEntityReferencesColumnRow(
            object column,
            int row,
            World world,
            Entity? excludedEntity,
            string? parameterName)
        {
            var value = ((OptionalEntityReferenceComponent[])column)[row];
            ValidateEntityReferenceValue(in value, world, excludedEntity, parameterName);
        }

        public void ValidateEntityReferencesInitialization(
            ComponentInitialization initialization,
            World world,
            Entity? excludedEntity,
            string? parameterName)
        {
            if (!initialization.TryGetValue<OptionalEntityReferenceComponent>(out var value))
            {
                throw new InvalidOperationException("The test reference component initialization has no typed value.");
            }

            ValidateEntityReferenceValue(in value, world, excludedEntity, parameterName);
        }

        public void ValidateEntityReferencesValue<TValue>(
            in TValue value,
            World world,
            Entity? excludedEntity,
            string? parameterName)
            where TValue : unmanaged
        {
            if (typeof(TValue) != typeof(OptionalEntityReferenceComponent))
            {
                throw new InvalidOperationException("The test reference component validation has the wrong type.");
            }

            ref var mutableValue = ref Unsafe.AsRef(in value);
            ref var referenceValue = ref Unsafe.As<TValue, OptionalEntityReferenceComponent>(ref mutableValue);
            ValidateEntityReferenceValue(in referenceValue, world, excludedEntity, parameterName);
        }

        public ImmutableArray<Diagnostic> ValidateInitialization(
            ComponentInitialization initialization,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!initialization.TryGetValue<OptionalEntityReferenceComponent>(out _))
            {
                throw new InvalidOperationException("The test reference component initialization has no typed value.");
            }

            return [];
        }

        public ImmutableArray<Diagnostic> ValidateValue<TValue>(in TValue value, CancellationToken cancellationToken)
            where TValue : unmanaged
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (typeof(TValue) != typeof(OptionalEntityReferenceComponent))
            {
                throw new InvalidOperationException("The test reference component validation has the wrong type.");
            }

            return [];
        }
    }

    private sealed class DeferredReferenceSystem : IWorldSystem
    {
        private readonly DeferredReferenceProbe _probe;

        internal DeferredReferenceSystem(DeferredReferenceProbe probe)
        {
            ArgumentNullException.ThrowIfNull(probe);
            _probe = probe;
        }

        public void Execute(WorldSystemContext context)
        {
            if (_probe.ClearAndDestroyTarget)
            {
                var target = _probe.Target
                    ?? throw new InvalidOperationException("The reference probe has no live Entity to destroy.");
                context.Commands.SetEntityReference<OptionalEntityReferenceComponent>(
                    _probe.Owner,
                    _probe.Path,
                    (Entity?)null);
                context.Commands.Destroy(target, HierarchyDestroyMode.DetachChildrenKeepWorld);
                return;
            }

            if (_probe.DestroyLiveTarget)
            {
                var target = _probe.Target
                    ?? throw new InvalidOperationException("The reference probe has no live Entity to destroy.");
                context.Commands.Destroy(target, HierarchyDestroyMode.DetachChildrenKeepWorld);
                return;
            }

            if (_probe.CreateReferenceTargets)
            {
                var targetToken = context.Commands.Create(new EntityDescription());
                var absent = new OptionalEntityReferenceComponent(false, default);
                var ownerToken = context.Commands.Create(new EntityDescription(
                    components: [ComponentInitialization.Value(in absent)]));
                _probe.TargetToken = targetToken;
                _probe.TokenOwner = ownerToken;
                context.Commands.SetEntityReference<OptionalEntityReferenceComponent>(
                    _probe.Owner,
                    _probe.Path,
                    (EntityToken?)targetToken);
                context.Commands.SetEntityReference<OptionalEntityReferenceComponent>(
                    ownerToken,
                    _probe.Path,
                    (EntityToken?)targetToken);

                if (_probe.DestroyTargetAfterReferences)
                {
                    context.Commands.Destroy(targetToken, HierarchyDestroyMode.DetachChildrenKeepWorld);
                }

                return;
            }

            if (_probe.SetBeforeReference)
            {
                var absent = new OptionalEntityReferenceComponent(false, default);
                context.Commands.Set(_probe.Owner, in absent);
            }

            if (_probe.UseTokenTarget)
            {
                context.Commands.SetEntityReference<OptionalEntityReferenceComponent>(
                    _probe.Owner,
                    _probe.Path,
                    _probe.TargetToken);
            }
            else
            {
                context.Commands.SetEntityReference<OptionalEntityReferenceComponent>(
                    _probe.Owner,
                    _probe.Path,
                    _probe.Target);
            }
        }
    }

    private readonly struct DeferredReferenceSystemCreate : IWorldSystemCreate<DeferredReferenceSystem>
    {
        private readonly DeferredReferenceProbe _probe;

        internal DeferredReferenceSystemCreate(DeferredReferenceProbe probe)
        {
            _probe = probe;
        }

        public DeferredReferenceSystem Invoke() => new(_probe);
    }

    private readonly struct DeferredReferenceSystemRelease : IWorldSystemRelease<DeferredReferenceSystem>
    {
        public void Invoke(DeferredReferenceSystem system)
            => ArgumentNullException.ThrowIfNull(system);
    }

    private sealed class DeferredSetProbe
    {
        internal DeferredSetProbe(Guid schemaId, int value)
        {
            SchemaId = schemaId;
            Value = value;
        }

        internal int CommandCount { get; set; } = 1;

        internal Entity Entity { get; set; }

        internal int? ObservedValue { get; set; }

        internal Guid SchemaId { get; }

        internal Entity? SecondEntity { get; set; }

        internal int Value { get; }

        internal bool WriteFromQuery { get; set; }
    }

    private readonly struct DeferredSetQueryAction : IAction<EntityChunkView>
    {
        private readonly CommandWriter _commands;
        private readonly int _value;

        internal DeferredSetQueryAction(CommandWriter commands, int value)
        {
            ArgumentNullException.ThrowIfNull(commands);
            _commands = commands;
            _value = value;
        }

        public readonly void Invoke(scoped EntityChunkView argument)
        {
            var entities = argument.Entities;

            for (var index = 0; index < entities.Length; index++)
            {
                _commands.Set(entities[index], in _value);
            }
        }
    }

    private readonly struct DeferredSetReadAction : IAction<EntityChunkView>
    {
        private readonly DeferredSetProbe _probe;

        internal DeferredSetReadAction(DeferredSetProbe probe)
        {
            _probe = probe;
        }

        public readonly void Invoke(scoped EntityChunkView argument)
        {
            var values = argument.Read<int>();
            _probe.ObservedValue = values[0];
        }
    }

    private sealed class DeferredSetSystem : IWorldSystem
    {
        private readonly DeferredSetProbe _probe;

        internal DeferredSetSystem(DeferredSetProbe probe)
        {
            ArgumentNullException.ThrowIfNull(probe);
            _probe = probe;
        }

        public void Execute(WorldSystemContext context)
        {
            if (_probe.WriteFromQuery)
            {
                var partitionQuery = new EntityQueryDescription(
                    [_probe.SchemaId],
                    [],
                    [new SystemResourceAccess(
                        SystemResourceKind.Component,
                        _probe.SchemaId,
                        SystemAccessMode.Read)]);
                var partitionAction = new DeferredSetQueryAction(context.Commands, _probe.Value);
                context.ForEachChunk(partitionQuery, ref partitionAction);
                return;
            }

            var value = _probe.Value;

            for (var index = 0; index < _probe.CommandCount; index++)
            {
                context.Commands.Set(_probe.Entity, in value);
            }

            if (_probe.SecondEntity is { } secondEntity)
            {
                context.Commands.Set(secondEntity, in value);
            }

            var query = new EntityQueryDescription(
                [_probe.SchemaId],
                [],
                [new SystemResourceAccess(
                    SystemResourceKind.Component,
                    _probe.SchemaId,
                    SystemAccessMode.Read)]);
            var action = new DeferredSetReadAction(_probe);
            context.ForEachChunk(query, ref action);
        }
    }

    private readonly struct DeferredSetSystemCreate : IWorldSystemCreate<DeferredSetSystem>
    {
        private readonly DeferredSetProbe _probe;

        internal DeferredSetSystemCreate(DeferredSetProbe probe)
        {
            _probe = probe;
        }

        public DeferredSetSystem Invoke() => new(_probe);
    }

    private readonly struct DeferredSetSystemRelease : IWorldSystemRelease<DeferredSetSystem>
    {
        public void Invoke(DeferredSetSystem system)
            => ArgumentNullException.ThrowIfNull(system);
    }

    private sealed class DeferredStructuralProbe
    {
        internal DeferredStructuralProbe(Guid tagId)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(tagId, Guid.Empty);
            TagId = tagId;
        }

        internal int AddValue { get; set; }

        internal bool CommandAfterTokenDestroy { get; set; }

        internal bool Create { get; set; }

        internal EntityDescription? CreateDescription { get; set; }

        internal bool CreateTwice { get; set; }

        internal bool DestroyCreatedToken { get; set; }

        internal bool DestroyInvalidToken { get; set; }

        internal bool DestroyLiveEntity { get; set; }

        internal bool DestroyLiveTwice { get; set; }

        internal HierarchyDestroyMode DestroyMode { get; set; }

        internal bool DestroySecondLiveEntity { get; set; }

        internal Entity Entity { get; set; }

        internal bool FailAfterAdd { get; set; }

        internal bool Remove { get; set; }

        internal Entity? SecondEntity { get; set; }

        internal EntityToken SecondToken { get; set; }

        internal int SetValue { get; set; }

        internal Guid TagId { get; }

        internal EntityToken Token { get; set; }

        internal bool UseTokenCommands { get; set; }
    }

    private sealed class DeferredStructuralSystem : IWorldSystem
    {
        private readonly DeferredStructuralProbe _probe;

        internal DeferredStructuralSystem(DeferredStructuralProbe probe)
        {
            ArgumentNullException.ThrowIfNull(probe);
            _probe = probe;
        }

        public void Execute(WorldSystemContext context)
        {
            if (_probe.Create)
            {
                _probe.Token = context.Commands.Create(_probe.CreateDescription
                    ?? throw new InvalidOperationException("The structural probe has no create description."));
                ApplyTokenCommands(context, _probe.Token);

                if (_probe.DestroyCreatedToken)
                {
                    var target = _probe.DestroyInvalidToken
                        ? new EntityToken(Guid.NewGuid(), 0)
                        : _probe.Token;
                    context.Commands.Destroy(target, _probe.DestroyMode);

                    if (_probe.CommandAfterTokenDestroy)
                    {
                        var value = _probe.SetValue;
                        context.Commands.Set(_probe.Token, in value);
                    }
                }

                if (_probe.CreateTwice)
                {
                    _probe.SecondToken = context.Commands.Create(_probe.CreateDescription
                        ?? throw new InvalidOperationException("The structural probe has no create description."));
                    ApplyTokenCommands(context, _probe.SecondToken);
                }

                return;
            }

            if (_probe.DestroyLiveEntity)
            {
                context.Commands.Destroy(_probe.Entity, _probe.DestroyMode);

                if (_probe.DestroyLiveTwice)
                {
                    context.Commands.Destroy(_probe.Entity, _probe.DestroyMode);
                }

                if (_probe.DestroySecondLiveEntity)
                {
                    context.Commands.Destroy(_probe.SecondEntity
                        ?? throw new InvalidOperationException("The second live Entity was not provided."),
                        _probe.DestroyMode);
                }

                return;
            }

            if (_probe.Remove)
            {
                context.Commands.Remove<int>(_probe.Entity);
                context.Commands.RemoveTag(_probe.Entity, _probe.TagId);
                return;
            }

            var addValue = _probe.AddValue;
            context.Commands.Add(_probe.Entity, in addValue);

            if (_probe.FailAfterAdd)
            {
                context.Commands.Remove<int>(_probe.SecondEntity
                    ?? throw new InvalidOperationException("The failing structural probe has no second Entity."));
                return;
            }

            var setValue = _probe.SetValue;
            context.Commands.Set(_probe.Entity, in setValue);
            context.Commands.AddTag(_probe.Entity, _probe.TagId);

            if (_probe.SecondEntity is { } secondEntity)
            {
                context.Commands.AddTag(secondEntity, _probe.TagId);
            }
        }

        private void ApplyTokenCommands(WorldSystemContext context, EntityToken token)
        {
            if (!_probe.UseTokenCommands)
            {
                return;
            }

            var value = _probe.SetValue;
            context.Commands.Set(token, in value);
            context.Commands.AddTag(token, _probe.TagId);
        }
    }

    private readonly struct DeferredStructuralSystemCreate : IWorldSystemCreate<DeferredStructuralSystem>
    {
        private readonly DeferredStructuralProbe _probe;

        internal DeferredStructuralSystemCreate(DeferredStructuralProbe probe)
        {
            _probe = probe;
        }

        public DeferredStructuralSystem Invoke() => new(_probe);
    }

    private readonly struct DeferredStructuralSystemRelease : IWorldSystemRelease<DeferredStructuralSystem>
    {
        public void Invoke(DeferredStructuralSystem system)
            => ArgumentNullException.ThrowIfNull(system);
    }

    private struct EntityOrderRecorder : IAction<EntityChunkView>
    {
        internal int CallbackCount;

        internal uint FirstEntityIndex;

        internal uint SecondEntityIndex;

        public void Invoke(scoped EntityChunkView argument)
        {
            var entity = argument.Entities[0];

            if (CallbackCount == 0)
            {
                FirstEntityIndex = entity.Index;
            }
            else
            {
                SecondEntityIndex = entity.Index;
            }

            CallbackCount++;
        }
    }

    private struct InvalidQueryWriter : IAction<EntityChunkView>
    {
        public readonly void Invoke(scoped EntityChunkView argument)
        {
            argument.Write<int>()[0] = -1;
        }
    }

    private readonly struct OptionalAggregateAccessor : IComponentOptionalAccessor<OptionalAggregateEntityReferenceComponent, OptionalAggregateValue>
    {
        public void Clear(ref OptionalAggregateEntityReferenceComponent value)
            => value = default;

        public OptionalAggregateValue GetPresent(in OptionalAggregateEntityReferenceComponent value)
            => new(value.Target);

        public bool IsPresent(in OptionalAggregateEntityReferenceComponent value) => value.IsPresent;

        public void SetPresent(ref OptionalAggregateEntityReferenceComponent value, in OptionalAggregateValue fieldValue)
            => value = new OptionalAggregateEntityReferenceComponent(isPresent: true, fieldValue.Target);
    }

    private readonly record struct OptionalAggregateAuthor(bool HasAggregate, Guid? TargetEntityId);

    private readonly struct OptionalAggregateEntityAccessor : IComponentValueAccessor<OptionalAggregateEntityReferenceComponent, Entity>
    {
        public Entity Get(in OptionalAggregateEntityReferenceComponent value) => value.Target;

        public void Set(ref OptionalAggregateEntityReferenceComponent value, in Entity fieldValue)
            => value = new OptionalAggregateEntityReferenceComponent(isPresent: true, fieldValue);
    }

    private readonly struct OptionalAggregateEntityReader : IComponentValueReader<OptionalAggregateAuthor, Guid>
    {
        public Guid Get(in OptionalAggregateAuthor value)
            => value.TargetEntityId.GetValueOrDefault();
    }

    private readonly struct OptionalAggregateEntityReferenceComponent
    {
        private readonly bool _isPresent;
        private readonly Entity _target;

        internal OptionalAggregateEntityReferenceComponent(bool isPresent, Entity target)
        {
            _isPresent = isPresent;
            _target = target;
        }

        internal readonly bool IsPresent => _isPresent;

        internal readonly Entity Target => _target;
    }

    private readonly struct OptionalAggregateReader : IComponentOptionalReader<OptionalAggregateAuthor, OptionalAggregateSymbolicValue>
    {
        public OptionalAggregateSymbolicValue GetPresent(in OptionalAggregateAuthor value)
            => new(value.TargetEntityId.GetValueOrDefault());

        public bool IsPresent(in OptionalAggregateAuthor value) => value.HasAggregate;
    }

    private readonly record struct OptionalAggregateSymbolicValue(Guid TargetEntityId);

    private readonly record struct OptionalAggregateValue(Entity Target);

    private readonly struct OptionalEntityReferenceAccessor : IComponentOptionalAccessor<OptionalEntityReferenceComponent, Entity>
    {
        public void Clear(ref OptionalEntityReferenceComponent value)
            => value = default;

        public Entity GetPresent(in OptionalEntityReferenceComponent value) => value.Target;

        public bool IsPresent(in OptionalEntityReferenceComponent value) => value.IsPresent;

        public void SetPresent(ref OptionalEntityReferenceComponent value, in Entity fieldValue)
            => value = new OptionalEntityReferenceComponent(isPresent: true, fieldValue);
    }

    private readonly record struct OptionalEntityReferenceAuthor(Guid? TargetEntityId);

    private readonly struct OptionalEntityReferenceComponent
    {
        private readonly bool _isPresent;
        private readonly Entity _target;

        internal OptionalEntityReferenceComponent(bool isPresent, Entity target)
        {
            _isPresent = isPresent;
            _target = target;
        }

        internal readonly bool IsPresent => _isPresent;

        internal readonly Entity Target => _target;
    }

    private readonly struct OptionalEntityReferenceReader : IComponentOptionalReader<OptionalEntityReferenceAuthor, Guid>
    {
        public Guid GetPresent(in OptionalEntityReferenceAuthor value)
            => value.TargetEntityId.GetValueOrDefault();

        public bool IsPresent(in OptionalEntityReferenceAuthor value)
            => value.TargetEntityId.HasValue;
    }

    private struct QueryRecorder : IAction<EntityChunkView>
    {
        internal int CallbackCount;

        internal int EntityCount;

        internal int ValueSum;

        public void Invoke(scoped EntityChunkView argument)
        {
            CallbackCount++;
            EntityCount += argument.Entities.Length;

            foreach (var value in argument.Read<int>())
            {
                ValueSum += value;
            }
        }
    }

    private struct QueryWriter : IAction<EntityChunkView>
    {
        public readonly void Invoke(scoped EntityChunkView argument)
        {
            var values = argument.Write<int>();

            for (var index = 0; index < values.Length; index++)
            {
                values[index]++;
            }
        }
    }

    private sealed class TestComponentRegistration : IComponentRegistration
    {
        private readonly ComponentFactoryDescriptor _descriptor;
        private int _initializeColumnCalls;

        internal TestComponentRegistration(ComponentFactoryDescriptor? descriptor = null)
        {
            if (descriptor is null)
            {
                var schemaReference = new ContentSchemaReference(Guid.NewGuid(), 1, 0, ContentSchemaScope.Content);
                var layout = new ComponentLayout(Guid.NewGuid(), sizeof(int));
                descriptor = CreateTestDescriptor(
                    schemaReference,
                    layout,
                    ComponentStorageFlags.Persistent,
                    ComponentTransientRestore.None,
                    []);
            }

            _descriptor = descriptor;
            ComponentType = typeof(int);
            Layout = descriptor.Layout;
            SchemaReference = descriptor.Schema;
        }

        public Type ComponentType { get; }

        public ComponentFactoryDescriptor Descriptor => _descriptor;

        public ComponentLayout Layout { get; }

        public ContentSchemaReference SchemaReference { get; }

        internal int InitializeColumnCalls => Volatile.Read(ref _initializeColumnCalls);

        private static ImmutableArray<Diagnostic> CreateInvalidDiagnostics()
            => [
                new Diagnostic(
                    "TEST_INVALID_COMPONENT",
                    DiagnosticSeverity.Error,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Test.ComponentValidation",
                    null,
                    "The test component cannot be negative.",
                    []),
            ];

        public ComponentInitialization CaptureColumnRow(object column, int row)
        {
            var value = ((int[])column)[row];
            return ComponentInitialization.Value(in value);
        }

        public void ClearColumn(object column, int row)
            => ((int[])column)[row] = default;

        public void CopyColumnRow(object sourceColumn, int sourceRow, object destinationColumn, int destinationRow)
            => ((int[])destinationColumn)[destinationRow] = ((int[])sourceColumn)[sourceRow];

        public object CreateColumn(int capacity) => new int[capacity];

        public ComponentInitialization CreateInitializedValue(ComponentInitialization initialization)
        {
            ArgumentNullException.ThrowIfNull(initialization);

            if (initialization.IsDefault)
            {
                return ComponentInitialization.Value(0);
            }

            if (initialization.TryGetValue<int>(out _))
            {
                return initialization;
            }

            throw new InvalidOperationException("The test component initialization has the wrong type.");
        }

        public long GetColumnArrayBytes(int capacity)
            => checked(64L + ((checked((long)capacity * sizeof(int)) + 7L) & ~7L));

        public void InitializeColumn(object column, int row, ComponentInitialization initialization)
        {
            _ = Interlocked.Increment(ref _initializeColumnCalls);

            if (!initialization.TryGetValue<int>(out var value))
            {
                throw new InvalidOperationException("The test component initialization has no value.");
            }

            ((int[])column)[row] = value;
        }

        public bool IsEntityReferencePath(ComponentFieldPath path) => false;

        public void MoveColumnRow(object column, int sourceRow, int destinationRow)
        {
            var values = (int[])column;
            values[destinationRow] = values[sourceRow];
        }

        public ComponentInitialization SetEntityReference(
            ComponentInitialization current,
            ComponentFieldPath path,
            Entity? target)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> ValidateColumnRow(object column, int row, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (((int[])column)[row] < 0) ? CreateInvalidDiagnostics() : [];
        }

        public void ValidateEntityReferencesColumnRow(
            object column,
            int row,
            World world,
            Entity? excludedEntity,
            string? parameterName)
        {
        }

        public void ValidateEntityReferencesInitialization(
            ComponentInitialization initialization,
            World world,
            Entity? excludedEntity,
            string? parameterName)
        {
        }

        public void ValidateEntityReferencesValue<TValue>(
            in TValue value,
            World world,
            Entity? excludedEntity,
            string? parameterName)
            where TValue : unmanaged
        {
        }

        public ImmutableArray<Diagnostic> ValidateInitialization(
            ComponentInitialization initialization,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(initialization);
            cancellationToken.ThrowIfCancellationRequested();

            if (!initialization.TryGetValue<int>(out var value))
            {
                throw new InvalidOperationException("The test component initialization has no value.");
            }

            return (value < 0) ? CreateInvalidDiagnostics() : [];
        }

        public ImmutableArray<Diagnostic> ValidateValue<TValue>(in TValue value, CancellationToken cancellationToken)
            where TValue : unmanaged
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (typeof(TValue) == typeof(int))
            {
                ref var mutableValue = ref Unsafe.AsRef(in value);
                ref var integerValue = ref Unsafe.As<TValue, int>(ref mutableValue);
                return (integerValue < 0) ? CreateInvalidDiagnostics() : [];
            }

            throw new InvalidOperationException("The selected validation value has the wrong type.");
        }
    }

    private sealed class TestLongComponentRegistration : IComponentRegistration
    {
        private readonly ComponentFactoryDescriptor _descriptor;

        internal TestLongComponentRegistration(ComponentFactoryDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            _descriptor = descriptor;
        }

        public Type ComponentType => typeof(long);

        public ComponentFactoryDescriptor Descriptor => _descriptor;

        public ComponentLayout Layout => _descriptor.Layout;

        public ContentSchemaReference SchemaReference => _descriptor.Schema;

        public ComponentInitialization CaptureColumnRow(object column, int row)
        {
            var value = ((long[])column)[row];
            return ComponentInitialization.Value(in value);
        }

        public void ClearColumn(object column, int row)
            => ((long[])column)[row] = default;

        public void CopyColumnRow(object sourceColumn, int sourceRow, object destinationColumn, int destinationRow)
            => ((long[])destinationColumn)[destinationRow] = ((long[])sourceColumn)[sourceRow];

        public object CreateColumn(int capacity) => new long[capacity];

        public ComponentInitialization CreateInitializedValue(ComponentInitialization initialization)
        {
            ArgumentNullException.ThrowIfNull(initialization);
            return initialization.IsDefault
                ? ComponentInitialization.Value(0L)
                : initialization;
        }

        public long GetColumnArrayBytes(int capacity)
            => checked(64L + ((checked((long)capacity * sizeof(long)) + 7L) & ~7L));

        public void InitializeColumn(object column, int row, ComponentInitialization initialization)
        {
            if (initialization.IsDefault)
            {
                ((long[])column)[row] = default;
            }
            else if (initialization.TryGetValue<long>(out var value))
            {
                ((long[])column)[row] = value;
            }
            else
            {
                throw new InvalidOperationException("The test long initialization has the wrong type.");
            }
        }

        public bool IsEntityReferencePath(ComponentFieldPath path) => false;

        public void MoveColumnRow(object column, int sourceRow, int destinationRow)
        {
            var values = (long[])column;
            values[destinationRow] = values[sourceRow];
        }

        public ComponentInitialization SetEntityReference(
            ComponentInitialization current,
            ComponentFieldPath path,
            Entity? target)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> ValidateColumnRow(object column, int row, CancellationToken cancellationToken)
            => [];

        public void ValidateEntityReferencesColumnRow(
            object column,
            int row,
            World world,
            Entity? excludedEntity,
            string? parameterName)
        {
        }

        public void ValidateEntityReferencesInitialization(
            ComponentInitialization initialization,
            World world,
            Entity? excludedEntity,
            string? parameterName)
        {
        }

        public void ValidateEntityReferencesValue<TValue>(
            in TValue value,
            World world,
            Entity? excludedEntity,
            string? parameterName)
            where TValue : unmanaged
        {
        }

        public ImmutableArray<Diagnostic> ValidateInitialization(
            ComponentInitialization initialization,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(initialization);
            cancellationToken.ThrowIfCancellationRequested();

            if (!initialization.TryGetValue<long>(out _))
            {
                throw new InvalidOperationException("The test long initialization has no value.");
            }

            return [];
        }

        public ImmutableArray<Diagnostic> ValidateValue<TValue>(in TValue value, CancellationToken cancellationToken)
            where TValue : unmanaged
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (typeof(TValue) != typeof(long))
            {
                throw new InvalidOperationException("The selected validation value has the wrong type.");
            }

            return [];
        }
    }
}