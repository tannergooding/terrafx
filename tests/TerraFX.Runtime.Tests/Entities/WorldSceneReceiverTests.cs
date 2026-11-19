// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldSceneReceiverTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<World> World, WorldSceneStorageReservation Receiver) CloseWorldWithReceiver(WorldAdmissionDomain domain)
    {
        var world = CreateWorld(domain);
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The real World has no Scene support.");
        var receiver = world.TryReserveSceneStorage(support.GetParticipant(world), 512)
            ?? throw new InvalidOperationException("The receiver was refused.");
        world.Dispose();
        return (new WeakReference<World>(world), receiver);
    }

    private static World CreateWorld(WorldAdmissionDomain domain, Guid? worldGuid = null)
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        return World.Create(new WorldOptions(builder.Build()) {
            AdmissionDomain = domain,
            WorldGuid = worldGuid,
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<WorldSceneStorageReservation> ReserveReceiver(World world)
    {
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The real World has no Scene support.");
        var reservation = world.TryReserveSceneStorage(support.GetParticipant(world), 512)
            ?? throw new InvalidOperationException("The receiver was unexpectedly refused.");
        return new WeakReference<WorldSceneStorageReservation>(reservation);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CloseDuringARealConstructionClaimReturnsOnlyThatOriginalClaim(bool useFunding)
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        var use = world.AcquireGuidUse(1024);
        var control = use.Control;
        var parent = control.DomainReservation;
        var baseline = parent.Bytes;

        if (!useFunding)
        {
            use.Dispose();
            use = default;
        }

        try
        {
            Assert.That(
                domain.TryClaimSceneReservationConstruction(parent, useFunding ? control : null, 584, useRestoredCapacity: false),
                Is.True);
            world.Dispose();
            domain.Dispose();
            Assert.That(parent.IsOwnedBy(domain), Is.True);
            Assert.That(domain.Retirement.IsCompleted, Is.False);
            Assert.That(parent.Bytes, Is.EqualTo(baseline + (useFunding ? 0 : 584)));

            domain.CancelSceneReservationConstruction(
                parent,
                reservation: null,
                useFunding ? control : null,
                584,
                restoredCapacity: false);

            Assert.That(domain.GetSceneReservationBytes(parent), Is.EqualTo((0L, 0L)));

            if (useFunding)
            {
                Assert.That(control.WorkspaceLimit, Is.EqualTo(1024));
                Assert.That(control.Anchor.ActiveWorkspaceBytes, Is.Zero);
                Assert.That(parent.IsOwnedBy(domain), Is.True);
            }
            else
            {
                Assert.That(parent.IsOwnedBy(domain), Is.False);
            }
        }
        finally
        {
            use.Dispose();
        }

        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void CloseRefusesWorkspaceInstallationButAllowsItsOriginalReturn()
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        using var use = world.AcquireGuidUse(1024);
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The real World has no Scene support.");
        var participant = support.GetParticipant(world);
        using var reservation = world.TryReserveSceneWorkspace(participant, use.Control, 256)
            ?? throw new InvalidOperationException("The workspace receiver was unexpectedly refused.");
        var parent = use.Control.DomainReservation;
        var baseline = parent.Bytes;

        world.Dispose();
        domain.Dispose();
        Assert.That(reservation.TryTransferWorkspaceToStorage(), Is.False);
        Assert.That(world.TryReserveSceneStorage(participant, 1), Is.Null);
        Assert.That(use.Control.Anchor.ActiveWorkspaceBytes, Is.EqualTo(328));
        Assert.That(parent.Bytes, Is.EqualTo(baseline));
        reservation.Dispose();
        Assert.That(use.Control.WorkspaceLimit, Is.EqualTo(1024));
        Assert.That(parent.IsOwnedBy(domain), Is.True);
        use.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void ClosedWorldCarrierIsRetainedUntilItsLastReceiver()
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        var (world, receiver) = CloseWorldWithReceiver(domain);
        using var retainedReceiver = receiver;
        domain.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(world.TryGetTarget(out _), Is.True);
        Assert.That(domain.Retirement.IsCompleted, Is.False);
        retainedReceiver.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClosedWorldKeepsAnUninstalledOriginalTransferUntilRollback(bool closeDomain)
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        var use = world.AcquireGuidUse(4096);
        var control = use.Control;
        var parent = control.DomainReservation;
        var baseline = parent.Bytes;

        try
        {
            Assert.That(control.TryTransferWorkspaceToWorldReservation(parent, 3072), Is.True);
            Assert.That(parent.IndependentUseBytes, Is.EqualTo(1280));
            Assert.That(domain.GetSceneReservationBytes(parent), Is.EqualTo((3072L, 0L)));
            world.Dispose();

            if (closeDomain)
            {
                domain.Dispose();
            }

            Assert.That(parent.IsOwnedBy(domain), Is.True);
            Assert.That(parent.Bytes, Is.EqualTo(baseline + 3072));
            Assert.That(control.TryReturnWorkspaceFromWorldReservation(parent, 3072), Is.True);
            Assert.That(parent.Bytes, Is.EqualTo(baseline));
            Assert.That(parent.IndependentUseBytes, Is.EqualTo(4352));
            Assert.That(control.WorkspaceLimit, Is.EqualTo(4096));
            Assert.That(control.TryReturnWorkspaceFromWorldReservation(parent, 1), Is.False);
            Assert.That(parent.IsOwnedBy(domain), Is.True);
        }
        finally
        {
            use.Dispose();
        }

        Assert.That(parent.IsOwnedBy(domain), Is.False);
        Assert.That(control.IsQuarantined, Is.False);
    }

    [TestCase(583, false)]
    [TestCase(583, true)]
    [TestCase(584, false)]
    [TestCase(584, true)]
    public void ControlIsPreclaimedExactlyOnceBeforeConstruction(long workspaceBytes, bool workspace)
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        using var use = world.AcquireGuidUse(workspaceBytes);
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The real World has no Scene support.");
        var participant = support.GetParticipant(world);
        var parent = use.Control.DomainReservation;
        var baseline = parent.Bytes;
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        using var reservation = workspace
            ? world.TryReserveSceneWorkspace(participant, use.Control, 512)
            : world.TryReserveSceneStorage(participant, 512, use.Control);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;

        if (workspaceBytes == 583)
        {
            Assert.That(reservation, Is.Null);
            Assert.That(allocatedBytes, Is.Zero);
            Assert.That(use.Control.WorkspaceLimit, Is.EqualTo(583));
            Assert.That(parent.Bytes, Is.EqualTo(baseline));
            return;
        }

        var admitted = reservation
            ?? throw new InvalidOperationException("The exact-fit receiver was unexpectedly refused.");
        Assert.That(allocatedBytes, Is.EqualTo(72));
        Assert.That(admitted.Bytes, Is.EqualTo(584));
        Assert.That(admitted.DataBytes, Is.EqualTo(512));

        if (workspace)
        {
            Assert.That(parent.Bytes, Is.EqualTo(baseline));
            Assert.That(use.Control.Anchor.ActiveWorkspaceBytes, Is.EqualTo(584));
            Assert.That(admitted.TryTransferWorkspaceToStorage(), Is.True);
        }

        Assert.That(parent.Bytes, Is.EqualTo(baseline + 584));
        Assert.That(parent.IndependentUseBytes, Is.EqualTo(256));
        Assert.That(domain.GetSceneReservationBytes(parent), Is.EqualTo((0L, 584L)));
        admitted.Dispose();
        Assert.That(parent.Bytes, Is.EqualTo(baseline));
        Assert.That(use.Control.WorkspaceLimit, Is.Zero);
        Assert.That(parent.IndependentUseBytes, Is.EqualTo(256));
    }

    [Test]
    public void DomainOwnsWorkspaceInstallElectionAndRejectsADuplicate()
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        using var use = world.AcquireGuidUse(1024);
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The real World has no Scene support.");
        using var receiver = world.TryReserveSceneWorkspace(support.GetParticipant(world), use.Control, 256)
            ?? throw new InvalidOperationException("The workspace receiver was refused.");
        var parent = use.Control.DomainReservation;
        var baseline = parent.Bytes;

        Assert.That(domain.TryInstallSceneWorkspace(parent, receiver), Is.True);
        Assert.That(domain.TryInstallSceneWorkspace(parent, receiver), Is.False);
        Assert.That(receiver.IsStorage, Is.True);
        Assert.That(use.Control.Anchor.ActiveWorkspaceBytes, Is.Zero);
        Assert.That(use.Control.Anchor.UninstalledActiveWorldWorkspaceBytes, Is.Zero);
        Assert.That(use.Control.WorkspaceLimit, Is.EqualTo(696));
        Assert.That(parent.IndependentUseBytes, Is.EqualTo(952));
        Assert.That(parent.Bytes, Is.EqualTo(baseline + 328));
        Assert.That(domain.GetSceneReservationBytes(parent), Is.EqualTo((0L, 328L)));
        world.Dispose();
        domain.Dispose();
        receiver.Dispose();
        Assert.That(parent.IsOwnedBy(domain), Is.True);
        use.Dispose();
        Assert.That(use.Control.IsReleased, Is.True);
        Assert.That(use.Control.IsQuarantined, Is.False);
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void DomainRootsReceiverAcrossWorldCloseUntilItsExactReturn()
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        var receiver = ReserveReceiver(world);
        world.Dispose();
        domain.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(receiver.TryGetTarget(out var retained), Is.True);
        Assert.That(domain.Retirement.IsCompleted, Is.False);
        retained?.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void ForeignReturnsCannotRetireAnotherWorldOrItsOriginalLineage()
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        using var otherWorld = CreateWorld(domain);
        using var use = world.AcquireGuidUse(0);
        using var otherUse = otherWorld.AcquireGuidUse(0);
        var parent = use.Control.DomainReservation;
        var otherParent = otherUse.Control.DomainReservation;
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The real World has no Scene support.");
        using var receiver = world.TryReserveSceneStorage(support.GetParticipant(world), 512)
            ?? throw new InvalidOperationException("The receiver was refused.");
        var lineageField = typeof(World).GetField("_lineageGrant", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The original World lineage is missing.");
        var lineage = (WorldLineageGrant)(lineageField.GetValue(world)
            ?? throw new InvalidOperationException("The original World lineage was not captured."));
        use.Dispose();
        otherUse.Dispose();

        Assert.That(domain.TryRollbackFreshLineage(lineage), Is.False);
        _ = Assert.Throws<InvalidOperationException>(() => domain.ReleaseSceneReservation(otherParent, receiver));
        Assert.That(receiver.Parent, Is.SameAs(parent));
        Assert.That(domain.GetSceneReservationBytes(otherParent), Is.EqualTo((0L, 0L)));
        world.Dispose();
        Assert.That(domain.TryRollbackFreshLineage(lineage), Is.False);
        Task.Run(receiver.Dispose).GetAwaiter().GetResult();
        Assert.That(parent.IsOwnedBy(domain), Is.False);
        Assert.That(otherParent.IsOwnedBy(domain), Is.True);
        var entity = otherWorld.CreateEntity(new EntityDescription());
        Assert.That(otherWorld.GetEntityId(entity), Is.Not.EqualTo(Guid.Empty));
        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.False);
        otherWorld.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void OutOfOrderReceiversAndUsesKeepTheEntireOriginalParent(bool releaseUseFirst, bool closeDomainFirst)
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        var use = world.AcquireGuidUse(4096);
        var control = use.Control;
        var parent = control.DomainReservation;
        var baseline = parent.Bytes;
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The real World has no Scene support.");
        var participant = support.GetParticipant(world);
        using var first = world.TryReserveSceneStorage(participant, 128, control)
            ?? throw new InvalidOperationException("The first receiver was refused.");
        using var middle = world.TryReserveSceneStorage(participant, 256, control)
            ?? throw new InvalidOperationException("The middle receiver was refused.");
        using var head = world.TryReserveSceneStorage(participant, 512, control)
            ?? throw new InvalidOperationException("The head receiver was refused.");

        try
        {
            if (releaseUseFirst)
            {
                use.Dispose();
                use = default;
            }

            if (closeDomainFirst)
            {
                domain.Dispose();
            }

            world.Dispose();
            Assert.That(parent.IsOwnedBy(domain), Is.True);
            Assert.That(parent.Bytes, Is.EqualTo(baseline + 1112));
            Assert.That(domain.GetSceneReservationBytes(parent), Is.EqualTo((0L, 1112L)));
            domain.Dispose();

            middle.Dispose();
            Assert.That(parent.Bytes, Is.EqualTo(baseline + 784));
            first.Dispose();
            first.Dispose();
            Assert.That(parent.Bytes, Is.EqualTo(baseline + 584));
            Assert.That(domain.Retirement.IsCompleted, Is.False);
            head.Dispose();

            if (!releaseUseFirst)
            {
                Assert.That(parent.IsOwnedBy(domain), Is.True);
                Assert.That(parent.Bytes, Is.EqualTo(baseline));
                Assert.That(parent.IndependentUseBytes, Is.EqualTo(3240));
            }
        }
        finally
        {
            use.Dispose();
        }

        Assert.That(parent.IsOwnedBy(domain), Is.False);
        Assert.That(control.IsQuarantined, Is.False);
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void PrechargedCellKeepsItsWholeCompiledStride()
    {
        var type = typeof(WorldAdmissionDomain).GetNestedType("InterestEntry", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The compiled interest cell is missing.");
        var sizeOf = typeof(Unsafe).GetMethod(
            nameof(Unsafe.SizeOf),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
            ?? throw new InvalidOperationException("The compiled size observation is unavailable.");
        (string Name, Type Type)[] expected = [
            ("Generation", typeof(uint)),
            ("State", typeof(int)),
            ("Bytes", typeof(long)),
            ("Kind", typeof(int)),
            ("LineageRootIndex", typeof(int)),
            ("LineageRootGeneration", typeof(uint)),
            ("UseControl", typeof(WorldGuidUseControl)),
            ("Reservation", typeof(WorldDomainReservation)),
            ("SceneConstructionBytes", typeof(long)),
            ("SceneStorageBytes", typeof(long)),
            ("PendingSceneConstructionBytes", typeof(long)),
            ("WorldCloseProgress", typeof(long)),
            ("SceneConstructorCount", typeof(int)),
            ("WorldReturnPartCount", typeof(int)),
            ("WorldLifetime", typeof(WorldLifetimeControl)),
            ("SceneStorageHead", typeof(WorldSceneStorageReservation)),
            ("WorldCloseCarrier", typeof(World)),
            ("Reserved0", typeof(long)),
            ("Reserved1", typeof(long)),
        ];

        Assert.That(
            type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(static field => (field.Name, field.FieldType)),
            Is.EquivalentTo(expected));
        Assert.That(sizeOf.MakeGenericMethod(type).Invoke(null, null), Is.EqualTo(128));
        var rootType = typeof(WorldAdmissionDomain).GetNestedType("LineageEntry", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The compiled lineage cell is missing.");
        (string Name, Type Type)[] expectedRoot = [
            ("Generation", typeof(uint)),
            ("State", typeof(int)),
            ("WorldGuid", typeof(Guid)),
            ("IdentityBranchId", typeof(Guid)),
            ("IssuanceNamespace", typeof(Guid)),
            ("OwnsCurrentLineageEdge", typeof(bool)),
            ("MembershipKeys", typeof(Guid[])),
            ("MembershipBytes", typeof(long)),
            ("PendingMembershipEdgeCount", typeof(int)),
            ("PendingMembershipKeyCount", typeof(int)),
            ("HistoryInterestCount", typeof(int)),
            ("OwnerRetired", typeof(bool)),
            ("WorldInterestIndexPlusOne", typeof(int)),
            ("WorldInterestGeneration", typeof(uint)),
        ];
        Assert.That(
            rootType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(static field => (field.Name, field.FieldType)),
            Is.EquivalentTo(expectedRoot));
        var rootFieldBytes = (3 * 16) + sizeof(long) + (7 * sizeof(int)) + (2 * sizeof(bool)) + IntPtr.Size;
        var rootStride = (rootFieldBytes + IntPtr.Size - 1) & ~(IntPtr.Size - 1);
        Assert.That(sizeOf.MakeGenericMethod(rootType).Invoke(null, null), Is.EqualTo(rootStride));
        Assert.That(rootStride, Is.LessThanOrEqualTo(256));
        var bindingType = typeof(WorldGuidRegistry).GetNestedType("BindingCell", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The compiled binding cell is missing.");
        (string Name, Type Type)[] expectedBinding = [
            ("_generation", typeof(uint)),
            ("_keyCellIndex", typeof(int)),
            ("_keyGeneration", typeof(uint)),
            ("_state", typeof(int)),
            ("_freeNextIndex", typeof(int)),
            ("_independentUseCount", typeof(int)),
            ("_worldOwnerBound", typeof(bool)),
            ("_terminalDomain", typeof(WorldAdmissionDomain)),
            ("_terminalFailure", typeof(Exception)),
            ("_terminalState", typeof(int)),
            ("_terminalReserved0", typeof(long)),
            ("_terminalReserved1", typeof(long)),
            ("_terminalReserved2", typeof(long)),
            ("_terminalReserved3", typeof(long)),
            ("_terminalReserved4", typeof(long)),
            ("_terminalReserved5", typeof(long)),
            ("_terminalReserved6", typeof(long)),
            ("_terminalReserved7", typeof(long)),
            ("_terminalReserved8", typeof(long)),
            ("_terminalReserved9", typeof(long)),
        ];
        Assert.That(
            bindingType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(static field => (field.Name, field.FieldType)),
            Is.EquivalentTo(expectedBinding));
        Assert.That(sizeOf.MakeGenericMethod(bindingType).Invoke(null, null), Is.EqualTo(128));
    }

    [Test]
    public void RawGrantCannotReleaseAWorldOwnerWhileReceiverRemains()
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        using var use = world.AcquireGuidUse(0);
        var control = use.Control;
        var grant = control.Grant;
        var permitField = typeof(WorldLifetimeControl).GetField("_retirementPermit", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The original retirement permit is missing.");
        var permit = (WorldGuidRetirementPermit)(permitField.GetValue(control.Lifetime)
            ?? throw new InvalidOperationException("The original retirement permit was not captured."));
        use.Dispose();
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The real World has no Scene support.");
        using var receiver = world.TryReserveSceneStorage(support.GetParticipant(world), 512)
            ?? throw new InvalidOperationException("The receiver was refused.");
        var registry = WorldGuidRegistry.GetSelected();

        Assert.That(registry.TryBindWorldOwner(grant), Is.False);
        Assert.That(registry.TryReleaseFresh(grant), Is.False);
        world.Dispose();
        Assert.That(registry.TryReleaseFresh(grant), Is.False);
        Assert.That(registry.TryReleaseFresh(grant, permit), Is.False);
        Assert.That(registry.TryRetire(permit, out _), Is.False);
        receiver.Dispose();
        using var successor = CreateWorld(domain, world.WorldGuid);
        Assert.That(successor.WorldGuid, Is.EqualTo(world.WorldGuid));
    }

    [Test]
    public void RevokedGuidCannotBeReusedWhileReceiverOrPersistenceReturnRemains()
    {
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        var guid = Guid.NewGuid();
        using var world = CreateWorld(domain, guid);
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The real World has no Scene support.");
        using var receiver = world.TryReserveSceneStorage(support.GetParticipant(world), 512)
            ?? throw new InvalidOperationException("The receiver was refused.");
        Assert.That(world.TryReservePersistenceAdmissionBytes(512, out var persistence), Is.True);
        using var retainedPersistence = persistence
            ?? throw new InvalidOperationException("The real persistence reservation was refused.");
        var registry = WorldGuidRegistry.GetSelected();

        world.Dispose();
        Assert.That(registry.TryReserveFresh(guid, out _, out var refusal), Is.False);
        Assert.That(refusal.Code, Is.EqualTo(WorldGuidRefusal.GuidAlreadyRetained));
        receiver.Dispose();
        Assert.That(registry.TryReserveFresh(guid, out _, out refusal), Is.False);
        Assert.That(refusal.Code, Is.EqualTo(WorldGuidRefusal.GuidAlreadyRetained));
        retainedPersistence.Dispose();
        using var successor = CreateWorld(domain, guid);
        Assert.That(successor.WorldGuid, Is.EqualTo(guid));
    }
}
