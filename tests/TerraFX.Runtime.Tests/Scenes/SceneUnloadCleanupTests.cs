// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed class SceneUnloadCleanupTests
{
    private static readonly Guid s_childEntityId = new("10000000-0000-4000-8000-000000000002");
    private static readonly Guid s_placementId = new("40000000-0000-4000-8000-000000000002");

    private static void AssertRetained(CleanupFixture fixture, OwnedScene scene, Exception failure)
    {
        if (scene.Instance.SceneInstanceId != Guid.Empty)
        {
            Assert.That(fixture.Registry.TryGetInstance(scene.Instance.SceneInstanceId, out _), Is.False);
        }

        Assert.Multiple(() => {
            Assert.That(scene.Instance.CleanupFailure?.SourceException, Is.SameAs(failure));
            Assert.That(scene.Plan.CleanupFailure?.SourceException, Is.SameAs(failure));
            Assert.That(scene.Receiver.RetainedOwner, Is.SameAs(scene.Plan));
            Assert.That(scene.Receiver.IsStorage, Is.True);
            Assert.That(scene.Receiver.Bytes, Is.EqualTo(scene.Plan.RequiredByteReservation));
            Assert.That(fixture.Domain.GetSceneReservationBytes(scene.Parent).StorageBytes,
                Is.GreaterThanOrEqualTo(scene.Receiver.Bytes));
            Assert.That(GetField<SceneInstance?[]>(fixture.Registry, "_ownedInstances"), Does.Contain(scene.Instance));
            Assert.That(fixture.Registry.ContainsInstance(scene.Instance), Is.False);
            Assert.That(fixture.Registry.HasRetainedCleanup, Is.True);
        });
    }

    private static byte[] CreateEmptyScene()
    {
        var hierarchy = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/hierarchy").SchemaId,
            2,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2));
        return SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("native/scene").SchemaId,
            2,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateUInt32Field(1, 3),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(2),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordField(3, hierarchy),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(4),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(6),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(7),
            SceneHierarchyCodecTests.FixtureBuilder.CreateRecordArrayField(8));
    }

    private static T GetField<T>(object owner, string name)
        => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner)
            ?? throw new InvalidOperationException($"The owner field {name} is unavailable."));

    [TestCase(false)]
    [TestCase(true)]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The real Scene plan adopts the cleanup probe and retains it after deliberate failure; test teardown must not retry it.")]
    public void CancellationOnlyCancelsBeforeStructuralInstallation(bool cancelBeforeCommit)
    {
        using var fixture = new CleanupFixture();
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException("The cleanup canceled after commit.", cancellation.Token);
        var cleanup = new CleanupProbe(failure, () => cancellation.CancelAsync().GetAwaiter().GetResult());
        var scene = fixture.Activate(cleanup);
        var revision = fixture.World.CanonicalMutationRevision;
        var controlCount = GetField<int>(fixture.World, "_controlRecordCount");
        var unload = scene.Instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree, cancellation.Token);

        if (cancelBeforeCommit)
        {
            cancellation.CancelAsync().GetAwaiter().GetResult();
        }

        fixture.World.PumpControlBoundary();

        if (cancelBeforeCommit)
        {
            Assert.Multiple(() => {
                Assert.That(unload.IsCanceled, Is.True);
                Assert.That(cleanup.DisposeCalls, Is.Zero);
                Assert.That(fixture.Registry.ContainsInstance(scene.Instance), Is.True);
                Assert.That(fixture.World.CanonicalMutationRevision, Is.EqualTo(revision));
                Assert.That(GetField<int>(fixture.World, "_controlRecordCount"), Is.EqualTo(controlCount));
            });
        }
        else
        {
            Assert.That(Assert.Throws<OperationCanceledException>(() => unload.GetAwaiter().GetResult()), Is.SameAs(failure));
            Assert.Multiple(() => {
                Assert.That(unload.IsFaulted, Is.True);
                Assert.That(unload.IsCanceled, Is.False);
                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That(cleanup.DisposeCalls, Is.EqualTo(1));
                Assert.That(fixture.World.CanonicalMutationRevision, Is.EqualTo(revision + 1));
                Assert.That(GetField<int>(fixture.World, "_controlRecordCount"), Is.EqualTo(controlCount + 1));
            });
            AssertRetained(fixture, scene, failure);
        }

        Assert.That(Assert.Throws<OperationCanceledException>(fixture.World.Dispose), Is.SameAs(failure));
        Assert.That(cleanup.DisposeCalls, Is.EqualTo(1));
    }

    [TestCase(typeof(SceneInstance), 112)]
    [TestCase(typeof(SceneWorldRegistry), 144)]
    [TestCase(typeof(World.WorldSceneControlAppend), 88)]
    public void CleanupCarriersMatchTheSelectedRuntimeLayout(Type type, int expectedBytes)
    {
        const int SampleCount = 64;
        var samples = new object[SampleCount];

        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = RuntimeHelpers.GetUninitializedObject(type);
        }

        GC.KeepAlive(samples);
        var start = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = RuntimeHelpers.GetUninitializedObject(type);
        }

        var bytes = (GC.GetAllocatedBytesForCurrentThread() - start) / SampleCount;
        GC.KeepAlive(samples);
        TestContext.WriteLine($"{type.Name}: {bytes} bytes");
        Assert.That(bytes, Is.EqualTo(expectedBytes));
        Assert.That(Unsafe.SizeOf<World.WorldSceneStructuralCommitFacts>(), Is.EqualTo(24));

        if (type == typeof(SceneWorldRegistry))
        {
            var locks = new Lock[SampleCount];
            start = GC.GetAllocatedBytesForCurrentThread();

            for (var index = 0; index < locks.Length; index++)
            {
                locks[index] = new Lock();
            }

            var lockBytes = (GC.GetAllocatedBytesForCurrentThread() - start) / SampleCount;
            GC.KeepAlive(locks);
            TestContext.WriteLine($"Registry and both Locks: {bytes + (2 * lockBytes)} bytes");
            Assert.That(bytes + (2 * lockBytes), Is.LessThanOrEqualTo(512));
        }
    }

    [TestCase(HierarchyDestroyMode.DetachChildrenKeepWorld)]
    [TestCase(HierarchyDestroyMode.DestroySubtree)]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The actual Scene owns the failing cleanup probe; no independent test disposal may retry that transferred owner.")]
    public void CleanupFailureDoesNotUndoTheSelectedOutsideInstanceHierarchyDestruction(HierarchyDestroyMode mode)
    {
        using var fixture = new CleanupFixture();
        var failure = new InvalidOperationException("The structurally retired Scene failed cleanup.");
        var scene = fixture.Activate(new CleanupProbe(failure));
        var outside = fixture.Activate();
        var outsideId = fixture.World.GetEntityId(outside.Entities[0]);

        using (var transaction = outside.Instance.BeginTransaction())
        {
            transaction.Reparent(outside.Entities[0], scene.Entities[0], ReparentMode.KeepWorld);
            var commit = transaction.CommitAsync();
            fixture.World.PumpControlBoundary();
            Assert.That(commit.GetAwaiter().GetResult().Status, Is.EqualTo(SceneTransactionStatus.Committed));
        }

        var unload = scene.Instance.UnloadAsync(mode);
        fixture.World.PumpControlBoundary();
        Assert.That(Assert.Throws<InvalidOperationException>(() => unload.GetAwaiter().GetResult()), Is.SameAs(failure));
        Assert.That(fixture.World.TryGetEntity(outsideId, out var survivingRoot),
            Is.EqualTo(mode == HierarchyDestroyMode.DetachChildrenKeepWorld));

        if (mode == HierarchyDestroyMode.DetachChildrenKeepWorld)
        {
            Assert.That(fixture.World.TryGetHierarchyParent(survivingRoot, out _), Is.False);
            Assert.That(outside.Instance.TryGetProvenance(survivingRoot, out _), Is.True);
        }

        Assert.That(fixture.Registry.ContainsInstance(outside.Instance), Is.True);
        Assert.That(fixture.Registry.ContainsInstance(scene.Instance), Is.False);
        Assert.That(Assert.Throws<InvalidOperationException>(fixture.World.Dispose), Is.SameAs(failure));
        Assert.That(outside.Receiver.IsDisposed, Is.True);
        Assert.That(scene.Receiver.IsStorage, Is.True);
    }

    [Test]
    public void ClosedRegistryCannotAdoptAStillCallerOwnedCandidate()
    {
        using var fixture = new CleanupFixture();
        var (plan, receiver) = fixture.Prepare(cleanup: null);
        Assert.That(fixture.Registry.TryReserveInstance(2, out var reservation), Is.True);
        using var admitted = reservation ?? throw new InvalidOperationException("The real candidate reservation was refused.");
        var candidate = new SceneInstance(fixture.World, fixture.Registry, plan, []);
        fixture.Registry.Dispose();
        _ = Assert.Throws<ObjectDisposedException>(() => admitted.Adopt(candidate));
        Assert.That(GetField<SceneInstance?[]>(fixture.Registry, "_ownedInstances"), Does.Not.Contain(candidate));
        Assert.That(receiver.RetainedOwner, Is.SameAs(plan));
        admitted.Dispose();
        plan.Dispose();
        Assert.That(receiver.IsDisposed, Is.True);
    }

    [Test]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The empty Scene adopts the probe and deliberately quarantines it after cleanup failure.")]
    public void EmptyFailedSceneStillOwnsOneSlotAndItsRootOccurrence()
    {
        using var fixture = new CleanupFixture(maximumInstances: 1, maximumOccurrences: 1, empty: true);
        var failure = new InvalidOperationException("The empty Scene resource failed.");
        var cleanup = new CleanupProbe(failure);
        var scene = fixture.Activate(cleanup);
        var unload = scene.Instance.UnloadAsync(HierarchyDestroyMode.DetachChildrenKeepWorld);
        fixture.World.PumpControlBoundary();
        Assert.That(Assert.Throws<InvalidOperationException>(() => unload.GetAwaiter().GetResult()), Is.SameAs(failure));
        AssertRetained(fixture, scene, failure);
        Assert.Multiple(() => {
            Assert.That(scene.Instance.EntityCount, Is.Zero);
            Assert.That(fixture.Registry.GetSceneAssetIdCount(), Is.Zero);
            Assert.That(GetField<int>(fixture.Registry, "_cleanupPendingOccurrenceCount"), Is.EqualTo(1));
            Assert.That(fixture.Registry.TryReserveInstance(1, out _), Is.False);
        });
        Assert.That(Assert.Throws<InvalidOperationException>(fixture.World.Dispose), Is.SameAs(failure));
        Assert.That(cleanup.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Scene, attachment and system owners adopt the probes and perform their sole physical cleanup attempts.")]
    public void LowerPostcommitAttachmentFailureRemainsPrimaryWhilePlanCleanupStillRuns()
    {
        var systemCleanup = new CleanupProbe();
        using var fixture = new CleanupFixture(systemCleanup: systemCleanup, attachments: true);
        var planFailure = new InvalidOperationException("The Scene resource failed independently.");
        var planCleanup = new CleanupProbe(planFailure);
        var scene = fixture.Activate(planCleanup);
        var attachmentFailure = new InvalidOperationException("The detached attachment failed.");
        var attachmentCleanup = new CleanupProbe(attachmentFailure);
        fixture.World.SetAttachment(scene.Entities[0], new CleanupAttachment(attachmentCleanup), AttachmentOwnership.Transferred);
        var healthyCleanup = new CleanupProbe();
        fixture.World.SetAttachment(new CleanupAttachment(healthyCleanup), AttachmentOwnership.Transferred);
        var revision = fixture.World.CanonicalMutationRevision;
        var controlCount = GetField<int>(fixture.World, "_controlRecordCount");
        var unload = scene.Instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
        fixture.World.PumpControlBoundary();
        var observed = Assert.Throws<WorldAttachmentReleaseException>(() => unload.GetAwaiter().GetResult())
            ?? throw new InvalidOperationException("The committed attachment failure was not surfaced.");

        Assert.Multiple(() => {
            Assert.That(observed.PrimaryFailure.OriginalException, Is.SameAs(attachmentFailure));
            Assert.That(observed.StorageCommitted, Is.True);
            Assert.That(scene.Instance.CleanupFailure?.SourceException, Is.SameAs(observed));
            Assert.That(scene.Plan.CleanupFailure?.SourceException, Is.SameAs(planFailure));
            Assert.That(planCleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(attachmentCleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Registry.ContainsInstance(scene.Instance), Is.False);
            Assert.That(fixture.World.CanonicalMutationRevision, Is.EqualTo(revision + 1));
            Assert.That(GetField<int>(fixture.World, "_controlRecordCount"), Is.EqualTo(controlCount + 1));
            Assert.That(scene.Receiver.RetainedOwner, Is.SameAs(scene.Plan));
        });

        Assert.That(Assert.Throws<WorldAttachmentReleaseException>(fixture.World.Dispose), Is.SameAs(observed));
        Assert.Multiple(() => {
            Assert.That(healthyCleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(systemCleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.World.LastSystemCleanupReport?.FailureCount, Is.Zero);
            Assert.That(fixture.Registry.HasRetainedCleanup, Is.True);
            Assert.That(scene.Receiver.IsStorage, Is.True);
        });
        fixture.World.Dispose();
        Assert.That(planCleanup.DisposeCalls, Is.EqualTo(1));
        Assert.That(attachmentCleanup.DisposeCalls, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OnlyConfirmedCleanupReleasesInstanceAndOccurrenceCapacity(bool empty)
    {
        using var fixture = new CleanupFixture(maximumInstances: 1, maximumOccurrences: empty ? 1 : 2, empty: empty);
        var scene = fixture.Activate();
        var controlCount = GetField<int>(fixture.World, "_controlRecordCount");
        var unload = scene.Instance.UnloadAsync(HierarchyDestroyMode.DetachChildrenKeepWorld);
        fixture.World.PumpControlBoundary();
        Assert.That(unload.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
        Assert.Multiple(() => {
            Assert.That(scene.Instance.IsCleanupComplete, Is.True);
            Assert.That(scene.Plan.IsCleanupComplete, Is.True);
            Assert.That(scene.Receiver.IsDisposed, Is.True);
            Assert.That(scene.Instance.IsUnloadInvocationActive, Is.False);
            Assert.That(fixture.Registry.GetSceneAssetIdCount(), Is.Zero);
            Assert.That(fixture.Registry.HasRetainedCleanup, Is.False);
            Assert.That(GetField<int>(fixture.Registry, "_cleanupPendingInstanceCount"), Is.Zero);
            Assert.That(GetField<int>(fixture.Registry, "_cleanupPendingOccurrenceCount"), Is.Zero);
            Assert.That(GetField<SceneInstance?[]>(fixture.Registry, "_ownedInstances"), Does.Not.Contain(scene.Instance));
            Assert.That(GetField<ImmutableArray<SceneExpandedEntity>>(scene.Plan, "_entities"), Is.Empty);
            Assert.That(GetField<ImmutableArray<SceneExpandedOccurrence>>(scene.Plan, "_occurrences"), Is.Empty);
            Assert.That(fixture.Domain.GetSceneReservationBytes(scene.Parent), Is.EqualTo((0L, 0L)));
        });

        var repeated = scene.Instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
        fixture.World.PumpControlBoundary();
        Assert.That(repeated.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.AlreadyUnloaded));
        Assert.That(GetField<int>(fixture.World, "_controlRecordCount"), Is.EqualTo(controlCount + 1));
        Assert.That(fixture.Registry.TryReserveInstance(empty ? 1 : 2, out var reservation), Is.True);
        (reservation ?? throw new InvalidOperationException("The returned slot was not reusable.")).Dispose();
        Assert.That(fixture.Registry.TryReserveInstance(empty ? 1 : 2, out reservation), Is.True);
        (reservation ?? throw new InvalidOperationException("An unadopted reservation failed to return.")).Dispose();
        _ = fixture.World.Step();
        using var capture = fixture.World.AcquirePersistenceCaptureLease();
        Assert.That(fixture.Registry.GetPersistenceInstanceCount(capture), Is.Zero);
        fixture.Registry.CopyPersistenceInstances(capture, []);
    }

    [Test]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The active Scene retains the transferred resource across refusal and releases it during real World disposal.")]
    public void PrecommitControlCapacityRefusalPreservesActiveSceneAndCleanupOwnership()
    {
        using var fixture = new CleanupFixture(maximumRecordedControls: 1);
        var cleanup = new CleanupProbe();
        var scene = fixture.Activate(cleanup);
        fixture.World.SetTimeScale(1);
        var revision = fixture.World.CanonicalMutationRevision;
        var assignments = fixture.World.IdentityLedger.AssignmentCount;
        var unload = scene.Instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
        fixture.World.PumpControlBoundary();
        var receipt = unload.GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(receipt.Status, Is.EqualTo(SceneUnloadStatus.Rejected));
            Assert.That(receipt.StructuralRevision, Is.Null);
            Assert.That(receipt.Diagnostics, Is.Not.Empty);
            Assert.That(fixture.Registry.ContainsInstance(scene.Instance), Is.True);
            Assert.That(scene.Instance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out _), Is.True);
            Assert.That(fixture.World.CanonicalMutationRevision, Is.EqualTo(revision));
            Assert.That(fixture.World.IdentityLedger.AssignmentCount, Is.EqualTo(assignments));
            Assert.That(cleanup.DisposeCalls, Is.Zero);
            Assert.That(scene.Receiver.RetainedOwner, Is.SameAs(scene.Plan));
        });
        fixture.World.Dispose();
        Assert.That(cleanup.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The concurrent observer records the exact terminal cleanup failure returned by the elected drain.")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Each actual Scene adopts its probe; failed probes remain with their fixed-slot owners without test-side retry.")]
    public void RegistryDrainCancelsQueuedRequestsAndAttemptsEverySiblingWithoutInlineReentrancy()
    {
        using var fixture = new CleanupFixture(maximumInstances: 3, maximumOccurrences: 6);
        using var entered = new ManualResetEventSlim();
        using var observerStarted = new ManualResetEventSlim();
        using var observerReturned = new ManualResetEventSlim();
        var firstFailure = new InvalidOperationException("The first Scene failed.");
        var lastFailure = new InvalidOperationException("The last Scene failed.");
        var order = new int[3];
        var orderCount = 0;
        var observer = Task.Run(() => {
            if (!entered.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException("The real cleanup callback was not entered.");
            }

            observerStarted.Set();

            try
            {
                fixture.Registry.Dispose();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
            finally
            {
                observerReturned.Set();
            }
        });
        var firstCleanup = new CleanupProbe(firstFailure, () => {
            order[orderCount++] = 0;
            Assert.That(fixture.Registry.GetSceneAssetIdCount(), Is.Zero);
            Assert.That(GetField<int>(fixture.Registry, "_activeInstanceCount"), Is.Zero);
            _ = Assert.Throws<InvalidOperationException>(fixture.Registry.Dispose);
            _ = Assert.Throws<InvalidOperationException>(fixture.World.Dispose);
            _ = Assert.Throws<InvalidOperationException>(() => fixture.World.CreateEntity(new EntityDescription()));
            _ = Assert.Throws<InvalidOperationException>(() => fixture.World.AcquirePersistenceCaptureLease());
            entered.Set();
            Assert.That(observerStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(observerReturned.Wait(TimeSpan.FromMilliseconds(100)), Is.False);
        });
        var secondCleanup = new CleanupProbe(callback: () => order[orderCount++] = 1);
        var lastCleanup = new CleanupProbe(lastFailure, () => order[orderCount++] = 2);
        var first = fixture.Activate(firstCleanup);
        var second = fixture.Activate(secondCleanup);
        var last = fixture.Activate(lastCleanup);
        var firstQueued = first.Instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
        var lastQueued = last.Instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);

        Assert.That(Assert.Throws<InvalidOperationException>(fixture.Registry.Dispose), Is.SameAs(firstFailure));
        Assert.That(observer.GetAwaiter().GetResult(), Is.SameAs(firstFailure));
        Assert.Multiple(() => {
            Assert.That(orderCount, Is.EqualTo(3));
            Assert.That(order, Is.EqualTo((int[])[0, 1, 2]));
            Assert.That(firstCleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(secondCleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(lastCleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(second.Instance.IsCleanupComplete, Is.True);
            Assert.That(second.Receiver.IsDisposed, Is.True);
            Assert.That(GetField<SceneInstance?[]>(fixture.Registry, "_ownedInstances"), Does.Not.Contain(second.Instance));
            Assert.That(fixture.Registry.AdditionalCleanupFailureCount, Is.EqualTo(1));
            Assert.That(GetField<int>(fixture.Registry, "_cleanupPendingInstanceCount"), Is.EqualTo(2));
        });
        _ = Assert.Throws<ObjectDisposedException>(() => firstQueued.GetAwaiter().GetResult());
        _ = Assert.Throws<ObjectDisposedException>(() => lastQueued.GetAwaiter().GetResult());
        AssertRetained(fixture, first, firstFailure);
        AssertRetained(fixture, last, lastFailure);
        Assert.That(Assert.Throws<InvalidOperationException>(fixture.Registry.Dispose), Is.SameAs(firstFailure));
        Assert.That(Assert.Throws<InvalidOperationException>(fixture.World.Dispose), Is.SameAs(firstFailure));
        Assert.That(orderCount, Is.EqualTo(3));
    }

    [Test]
    public void SceneControlPreparationChargesItsNewReturnTailBeforeConstructionAndConfirmsRefund()
    {
        using var fixture = new CleanupFixture(maximumRecordedControls: 1);
        var scene = fixture.Activate();
        var limits = WorldPersistenceEbinLimits.Create(fixture.World.Options);
        Assert.That(ScenePersistenceControlRecordWriter.MeasureUnload(
            fixture.World,
            scene.Instance.SceneInstanceId,
            HierarchyDestroyMode.DestroySubtree,
            limits,
            out var workspace,
            out _), Is.EqualTo(EbinWriteStatus.Complete));
        var expectedTail = (sizeof(long) + IntPtr.Size + sizeof(int) + 7L) & ~7L;
        var payloadBytes = 64 + ((workspace.RecordBytes + 7L) & ~7L);
        var controlArrayBytes = (long)(typeof(World).GetMethod(
            "GetRestoreControlStorageByteReservation", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, [1])
            ?? throw new InvalidOperationException("The compiled control-array charge is unavailable."));
        var expectedBytes = controlArrayBytes
            + 128 + workspace.WorkspaceBytes + workspace.RecordBytes + payloadBytes + expectedTail;
        var baseline = fixture.World.GetWorldOwnedStateBytes();
        var parentBytes = scene.Parent.Bytes;
        using var append = fixture.World.TryPrepareSceneControlAppend(workspace.RecordBytes, workspace.WorkspaceBytes)
            ?? throw new InvalidOperationException("The real Scene control preparation was refused.");
        Assert.That(append.ReturnDebtBytes, Is.EqualTo(expectedBytes));
        Assert.That(scene.Parent.Bytes, Is.EqualTo(parentBytes + expectedBytes));
        Assert.That(World.WorldSceneControlAppend.ReturnTailByteReservation, Is.EqualTo(expectedTail));
        Assert.That(fixture.World.GetWorldOwnedStateBytes(), Is.EqualTo(baseline));
        append.Dispose();
        Assert.That(append.IsCleanupComplete, Is.True);
        Assert.That(append.ReturnDebtBytes, Is.Zero);
        Assert.That(scene.Parent.Bytes, Is.EqualTo(parentBytes));
        Assert.That(append.CandidateStorage, Is.Empty);

        using var emptyWorld = World.Create(new WorldOptions(fixture.World.Registration) {
            MaximumActiveSceneInstances = 4,
            MaximumSceneOccurrences = 8,
            MaximumPendingOwnerRequests = 4,
            MaximumRecordedControls = 1,
        });
        var emptyBaseline = emptyWorld.GetWorldOwnedStateBytes();
        var defaults = WorldSchedulingLimits.Default;
        using var refusedWorld = World.Create(new WorldOptions(fixture.World.Registration) {
            MaximumActiveSceneInstances = 4,
            MaximumSceneOccurrences = 8,
            MaximumPendingOwnerRequests = 4,
            MaximumRecordedControls = 1,
            SchedulingLimits = new WorldSchedulingLimits(
                defaults.MaximumRegistrationBytes, defaults.MaximumGraphBytes,
                defaults.MaximumWorkPlanBytes, defaults.MaximumCommandStagingBytes,
                emptyBaseline + expectedBytes - expectedTail, defaults.MaximumDiagnosticBytes,
                defaults.MaximumConflictComparisons),
        });
        Assert.That(refusedWorld.TryPrepareSceneControlAppend(workspace.RecordBytes, workspace.WorkspaceBytes), Is.Null);
        Assert.That(refusedWorld.GetWorldOwnedStateBytes(), Is.EqualTo(emptyBaseline));
    }

    [Test]
    public void UnloadContinuationCannotDisposeRegistryBeforeInvocationReturns()
    {
        using var fixture = new CleanupFixture();
        var scheduler = new InlineContinuationScheduler();
        var scene = fixture.Activate();
        var unload = scene.Instance.UnloadAsync(HierarchyDestroyMode.DestroySubtree);
        var unloadInvocationWasActive = false;
        InvalidOperationException? worldDisposeFailure = null;
        InvalidOperationException? registryDisposeFailure = null;
        var worldWasDisposed = false;
        var continuationThreadId = 0;
        var ownerThreadId = GetField<int>(fixture.World, "_ownerThreadId");
        var continuation = unload.ContinueWith(
            _ => {
                continuationThreadId = Environment.CurrentManagedThreadId;
                unloadInvocationWasActive = scene.Instance.IsUnloadInvocationActive;

                try
                {
                    fixture.World.Dispose();
                }
                catch (InvalidOperationException exception)
                {
                    worldDisposeFailure = exception;
                }

                try
                {
                    fixture.Registry.Dispose();
                }
                catch (InvalidOperationException exception)
                {
                    registryDisposeFailure = exception;
                }

                worldWasDisposed = GetField<int>(fixture.World, "_disposed") != 0;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            scheduler);

        fixture.World.PumpControlBoundary();
        continuation.GetAwaiter().GetResult();

        Assert.Multiple(() => {
            Assert.That(unloadInvocationWasActive, Is.True);
            Assert.That(continuationThreadId, Is.EqualTo(ownerThreadId));
            Assert.That(
                worldDisposeFailure?.Message,
                Is.EqualTo("World disposal is unavailable until the Scene unload invocation returns."));
            Assert.That(
                registryDisposeFailure?.Message,
                Is.EqualTo("The Scene registry cannot dispose before its unload invocation returns."));
            Assert.That(worldWasDisposed, Is.False);
            Assert.That(scene.Instance.IsUnloadInvocationActive, Is.False);
            Assert.That(scene.Instance.IsCleanupComplete, Is.True);
            Assert.That(unload.GetAwaiter().GetResult().Status, Is.EqualTo(SceneUnloadStatus.Unloaded));
        });

        fixture.World.Dispose();
    }

    [TestCase(HierarchyDestroyMode.DetachChildrenKeepWorld)]
    [TestCase(HierarchyDestroyMode.DestroySubtree)]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The Scene plan owns the deliberate failing probe, which remains retained without destructive retry.")]
    public void UnloadFailureRetainsInactiveOwnerAndReplaysWithoutAnotherDestruction(HierarchyDestroyMode mode)
    {
        using var fixture = new CleanupFixture(maximumInstances: 1, maximumOccurrences: 2);
        var failure = new InvalidOperationException("The Scene resource could not close.");
        var cleanup = new CleanupProbe(failure);
        var scene = fixture.Activate(cleanup);
        var firstId = fixture.World.GetEntityId(scene.Entities[0]);
        var secondId = fixture.World.GetEntityId(scene.Entities[1]);
        var assignmentCount = fixture.World.IdentityLedger.AssignmentCount;
        var controlCount = GetField<int>(fixture.World, "_controlRecordCount");
        var revision = fixture.World.CanonicalMutationRevision;
        var unload = scene.Instance.UnloadAsync(mode);
        fixture.World.PumpControlBoundary();

        Assert.That(Assert.Throws<InvalidOperationException>(() => unload.GetAwaiter().GetResult()), Is.SameAs(failure));
        AssertRetained(fixture, scene, failure);
        Assert.Multiple(() => {
            Assert.That(unload.IsFaulted, Is.True);
            Assert.That(fixture.World.CanonicalMutationRevision, Is.EqualTo(revision + 1));
            Assert.That(fixture.World.TryGetEntity(firstId, out _), Is.False);
            Assert.That(fixture.World.TryGetEntity(secondId, out _), Is.False);
            Assert.That(fixture.World.IdentityLedger.TryGetLiveLocator(
                scene.Instance.SceneInstanceId, IdentityKind.SceneInstance, out _), Is.False);
            Assert.That(scene.Instance.EntityCount, Is.EqualTo(2));
            Assert.That(scene.Instance.TryGetEntity(SceneAssetReaderTests.Fixtures.RootEntityId, out _), Is.False);
            Assert.That(scene.Instance.TryGetProvenance(scene.Entities[0], out _), Is.False);
            Assert.That(fixture.Registry.TryGetInstanceForEntity(fixture.World, scene.Entities[0], out _), Is.False);
            Assert.That(fixture.Registry.GetSceneAssetIdCount(), Is.Zero);
            Assert.That(GetField<int>(fixture.Registry, "_activeInstanceCount"), Is.Zero);
            Assert.That(GetField<int>(fixture.Registry, "_activeOrderedInstanceCount"), Is.Zero);
            Assert.That(GetField<int>(fixture.Registry, "_cleanupPendingInstanceCount"), Is.EqualTo(1));
            Assert.That(GetField<int>(fixture.Registry, "_cleanupPendingOccurrenceCount"), Is.EqualTo(2));
            Assert.That(GetField<int>(fixture.World, "_controlRecordCount"), Is.EqualTo(controlCount + 1));
            Assert.That(fixture.Registry.TryReserveInstance(1, out _), Is.False);
        });
        fixture.Registry.CopySceneAssetIds([]);
        _ = Assert.Throws<InvalidOperationException>(() => scene.Instance.BeginTransaction());
        _ = Assert.Throws<InvalidOperationException>(() => scene.Instance.GetPersistenceAssetIdCount());
        _ = Assert.Throws<ObjectDisposedException>(() => _ = scene.Plan.Entities);
        _ = Assert.Throws<InvalidOperationException>(() => fixture.World.AcquirePersistenceCaptureLease());

        revision = fixture.World.CanonicalMutationRevision;
        var repeated = scene.Instance.UnloadAsync(mode);
        fixture.World.PumpControlBoundary();
        Assert.That(Assert.Throws<InvalidOperationException>(() => repeated.GetAwaiter().GetResult()), Is.SameAs(failure));
        Assert.Multiple(() => {
            Assert.That(cleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.World.CanonicalMutationRevision, Is.EqualTo(revision));
            Assert.That(fixture.World.IdentityLedger.AssignmentCount, Is.EqualTo(assignmentCount));
            Assert.That(GetField<int>(fixture.World, "_controlRecordCount"), Is.EqualTo(controlCount + 1));
            Assert.That(scene.Instance.IsUnloadInvocationActive, Is.False);
        });

        _ = fixture.World.Step();
        _ = Assert.Throws<InvalidOperationException>(() => fixture.World.AcquirePersistenceCaptureLease());
        Assert.That(Assert.Throws<InvalidOperationException>(fixture.Registry.Dispose), Is.SameAs(failure));
        Assert.That(Assert.Throws<InvalidOperationException>(fixture.Registry.Dispose), Is.SameAs(failure));
        Assert.That(Assert.Throws<InvalidOperationException>(fixture.World.Dispose), Is.SameAs(failure));
        Assert.That(cleanup.DisposeCalls, Is.EqualTo(1));
        fixture.Domain.Dispose();
        Assert.That(fixture.Domain.Retirement.IsCompleted, Is.False);
    }

    [Test]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The reserved candidate adopts the probe before publication and retains it in that slot after failure.")]
    public void UnpublishedCandidateKeepsItsAdoptedSlotAfterFailedReservationCleanup()
    {
        using var fixture = new CleanupFixture(maximumInstances: 1, maximumOccurrences: 2);
        var failure = new InvalidOperationException("The unpublished candidate resource failed.");
        var cleanup = new CleanupProbe(failure);
        var (plan, receiver) = fixture.Prepare(cleanup);
        var parent = receiver.Parent ?? throw new InvalidOperationException("The receiver has no original parent.");
        Assert.That(fixture.Registry.TryReserveInstance(2, out var reservation), Is.True);
        var admitted = reservation ?? throw new InvalidOperationException("The candidate slot was refused.");
        var candidate = new SceneInstance(fixture.World, fixture.Registry, plan, []);
        admitted.Adopt(candidate);
        Assert.That(Assert.Throws<InvalidOperationException>(admitted.Dispose), Is.SameAs(failure));
        admitted.Dispose();

        AssertRetained(fixture, new OwnedScene(candidate, plan, receiver, parent, []), failure);
        Assert.Multiple(() => {
            Assert.That(cleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.World.IdentityLedger.AssignmentCount, Is.Zero);
            Assert.That(fixture.World.CanonicalMutationRevision, Is.Zero);
            Assert.That(GetField<int>(fixture.Registry, "_reservedInstanceCount"), Is.Zero);
            Assert.That(GetField<int>(fixture.Registry, "_cleanupPendingInstanceCount"), Is.EqualTo(1));
            Assert.That(fixture.Registry.TryReserveInstance(1, out _), Is.False);
        });
        _ = Assert.Throws<InvalidOperationException>(() => candidate.Activate(Guid.NewGuid(), 1));
        Assert.That(Assert.Throws<InvalidOperationException>(fixture.World.Dispose), Is.SameAs(failure));
        Assert.That(cleanup.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The plan owner is retained by its Scene instance after the deliberate cleanup failure.")]
    public void WorldDisposePreservesFirstSceneFailureWhileAttemptingLaterCleanup()
    {
        var systemCleanup = new CleanupProbe();
        using var fixture = new CleanupFixture(systemCleanup: systemCleanup, attachments: true);
        var sceneFailure = new InvalidOperationException("The first Scene cleanup failed.");
        var attachmentCleanup = new CleanupProbe(new InvalidOperationException("The later attachment cleanup failed."));
        var scene = fixture.Activate(new CleanupProbe(sceneFailure));
        fixture.World.SetAttachment(
            new CleanupAttachment(attachmentCleanup),
            AttachmentOwnership.Transferred);

        var observed = Assert.Throws<InvalidOperationException>(fixture.World.Dispose);

        Assert.Multiple(() => {
            Assert.That(observed, Is.SameAs(sceneFailure));
            Assert.That(attachmentCleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(systemCleanup.DisposeCalls, Is.EqualTo(1));
            Assert.That(scene.Instance.CleanupFailure?.SourceException, Is.SameAs(sceneFailure));
            Assert.That(fixture.Registry.HasRetainedCleanup, Is.True);
            Assert.That(fixture.Domain.Retirement.IsCompleted, Is.False);
        });
    }

    private readonly struct AttachmentReader : IComponentValueReader<CleanupAttachment, long>
    {
        public long Get(in CleanupAttachment value) => 0;
    }

    private readonly struct AuthorReader : IComponentValueReader<long, long>
    {
        public long Get(in long value) => value;
    }

    private sealed class CleanupAttachment(CleanupProbe cleanup)
    {
        internal CleanupProbe Cleanup => cleanup;
    }

    private sealed class CleanupAttachmentProvider : IWorldAttachmentStateProvider<CleanupAttachment, long>
    {
        public nint Count(CleanupAttachment value, WorldAttachmentCaptureBinding binding, CancellationToken cancellationToken)
            => sizeof(long);

        public WorldAttachmentReadResult<long> Read(
            scoped ReadOnlySpan<byte> source, WorldAttachmentReadBinding<long> binding, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Release(CleanupAttachment value, WorldAttachmentReleaseBinding binding)
            => value.Cleanup.Dispose();

        public CleanupAttachment Restore(
            in long author, WorldAttachmentRestoreBinding binding, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ImmutableArray<Diagnostic> Validate(
            CleanupAttachment value, WorldAttachmentValidationBinding binding, CancellationToken cancellationToken)
            => [];

        public nint Write(
            CleanupAttachment value, scoped Span<byte> destination, WorldAttachmentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            BinaryPrimitives.WriteInt64LittleEndian(destination, 0);
            return sizeof(long);
        }
    }

    private sealed class CleanupFixture : IDisposable
    {
        private readonly AssetStore _assets;
        private readonly bool _empty;
        private readonly string _packagePath;

        internal CleanupFixture(
            int maximumInstances = 4,
            int maximumOccurrences = 8,
            bool empty = false,
            CleanupProbe? systemCleanup = null,
            bool attachments = false,
            int maximumRecordedControls = 4096)
        {
            _empty = empty;
            Domain = new WorldAdmissionDomain(256 * 1024 * 1024);
            var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
            var assignment = new ContentSchemaIdentityAssignment("application/attachment-state/history-retention/v1");
            var attachmentSchema = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);

            if (attachments)
            {
                schemas = new CompiledContentSchemaRegistry([
                    .. schemas.Schemas,
                    new DataSchema(
                        attachmentSchema, assignment, ContentSchemaRole.AttachmentState,
                        ContentSchemaOwner.Application, ContentSchemaStorage.NotComponent,
                        ContentTransientRestore.NotApplicable,
                        [new ContentFieldDescriptor(1, 8, 0, true, 1, 1, [], 0,
                            ContentUnit.None, ContentReferenceKind.None, ContentAuthoring.None, null)]),
                ]);
            }

            var builder = new WorldRegistrationBuilder(schemas);
            _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());

            if (systemCleanup is not null)
            {
                var binding = WorldSystemFactoryBinding.Stateless<CleanupSystem, CleanupSystemCreate, CleanupSystemRelease>(
                    Guid.NewGuid(), 1, new WorldSystemSourceStateDeclaration(typeof(CleanupSystem), []),
                    default, new CleanupSystemRelease(systemCleanup));
                _ = builder.RegisterSystem(new SystemDescription(
                    Guid.NewGuid(), 0, SimulationStage.Input, [], [], [], SystemThreadMode.OwnerThread),
                    new CleanupSystemFactory(binding));
            }

            if (attachments)
            {
                var field = WorldAttachmentFieldBinding<CleanupAttachment, long>.Value<long, AttachmentReader, AuthorReader>(
                    schemas, attachmentSchema, new ComponentFieldPath([ComponentPropertySegment.FieldTag(1)]),
                    default, default);
                var binding = WorldAttachmentFactoryBinding<CleanupAttachment, long>.Create(
                    Guid.NewGuid(), 1, schemas, attachmentSchema, [field], new CleanupAttachmentProvider());
                _ = builder.RegisterAttachment(AttachmentScope.World, binding);
                _ = builder.RegisterAttachment(AttachmentScope.Entity, binding);
            }

            World = World.Create(new WorldOptions(builder.Build()) {
                AdmissionDomain = Domain,
                MaximumActiveSceneInstances = maximumInstances,
                MaximumSceneOccurrences = maximumOccurrences,
                MaximumPendingOwnerRequests = 4,
                MaximumRecordedControls = maximumRecordedControls,
            });
            Registry = (SceneWorldRegistry)(World.Registration.SceneSupport
                ?? throw new InvalidOperationException("The real World omitted Scene support.")).GetParticipant(World);
            var scene = empty
                ? CreateEmptyScene()
                : SceneAssetReaderTests.Fixtures.CreateSceneWithChildPlacement(
                    new AssetId(3), SceneAssetReaderTests.Fixtures.RootEntityId, s_placementId,
                    SceneAssetReaderTests.Fixtures.CreateRootPose(
                        s_childEntityId, localPositionX: 2, includeRootPosition: false));
            var prefab = SceneAssetReaderTests.Fixtures.CreatePrefab(new AssetId(1), entityId: s_childEntityId);
            var package = SceneAssetStoreTests.Package.CreatePackage(
                new SceneAssetStoreTests.Package.Entry(1, SceneAssetStoreTests.Package.CreateEnvelope(1, prefab)),
                new SceneAssetStoreTests.Package.Entry(3, SceneAssetStoreTests.Package.CreateEnvelope(3, scene)));
            _packagePath = Path.GetFullPath(Path.Combine("artifacts", "tst", "scene-cleanup", $"{Guid.NewGuid():N}.epkg"));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(_packagePath)
                ?? throw new InvalidOperationException("The test package has no directory."));
            File.WriteAllBytes(_packagePath, package);
            _assets = AssetStore.CreateAsync(new AssetStoreOptions(
                _packagePath, SHA256.HashData(package),
                SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build()))
                .GetAwaiter().GetResult();
        }

        internal WorldAdmissionDomain Domain { get; }

        internal SceneWorldRegistry Registry { get; }

        internal World World { get; }

        public void Dispose()
        {
            try
            {
                World.Dispose();
            }
            finally
            {
                try
                {
                    _assets.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                finally
                {
                    Domain.Dispose();
                    File.Delete(_packagePath);
                }
            }
        }

        internal OwnedScene Activate(CleanupProbe? cleanup = null)
        {
            var (plan, receiver) = Prepare(cleanup);
            var parent = receiver.Parent ?? throw new InvalidOperationException("The retained receiver lost its parent.");
            Assert.That(Registry.TryReserveInstance(plan.Occurrences.Length, out var reservation), Is.True);
            using var admitted = reservation ?? throw new InvalidOperationException("The real instance slot was refused.");
            ImmutableArray<EntityDescription> descriptions = [];

            if (!_empty)
            {
                var rootPosition = new SceneComponentDefinitions.RootPositionComponent(default);
                var rootTransform = new SceneComponentDefinitions.LocalTransformComponent(Vector3.Zero, Quaternion.Identity, Vector3.One);
                var childTransform = new SceneComponentDefinitions.LocalTransformComponent(new Vector3(2, 0, 0), Quaternion.Identity, Vector3.One);
                descriptions = [
                    new EntityDescription(components: [
                        ComponentInitialization.Value(in rootPosition), ComponentInitialization.Value(in rootTransform)]),
                    new EntityDescription(components: [ComponentInitialization.Value(in childTransform)]),
                ];
            }

            var instance = new SceneInstance(World, Registry, plan, []);
            admitted.Adopt(instance);
            var commit = World.CreateSceneEntitiesAtomically(
                plan.Entities.Length, null, plan.EntityIndicesByAssignmentOrdinal, _ => descriptions,
                parentEntityIndices: plan.EntityParentIndices);
            instance.Activate(commit.SceneInstanceId, commit.FirstEntityIdentityOrdinal);
            admitted.Commit(instance);
            return new OwnedScene(instance, plan, receiver, parent, commit.Entities);
        }

        internal (SceneExpansionPlan Plan, WorldSceneStorageReservation Receiver) Prepare(CleanupProbe? cleanup)
        {
            WorldSceneStorageReservation? receiver = null;
            var defaults = SceneExpansionLimits.Default;
            var limits = new SceneExpansionLimits(
                defaults.MaximumExpandedEntities, defaults.MaximumPrefabEntities, defaults.MaximumExpandedOccurrences,
                defaults.MaximumPrefabDepth, defaults.MaximumHierarchyDepth, defaults.MaximumExpandedBytes,
                reserveStorage: bytes => receiver = World.TryReserveSceneStorage(Registry, bytes));
            var plan = ScenePrefabExpansionPlanner.PrepareAsync(_assets, new AssetId(3), limits).GetAwaiter().GetResult();

            if (cleanup is not null)
            {
                plan.AttachPreparedAssets(cleanup);
            }

            return (plan, receiver ?? throw new InvalidOperationException("The real plan has no retained receiver."));
        }
    }

    private sealed class CleanupProbe(Exception? failure = null, Action? callback = null) : IDisposable
    {
        internal int DisposeCalls { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
            callback?.Invoke();

            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    private sealed class CleanupSystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context)
        {
        }
    }

    private readonly struct CleanupSystemCreate : IWorldSystemCreate<CleanupSystem>
    {
        public CleanupSystem Invoke() => new();
    }

    private sealed class CleanupSystemFactory(WorldSystemFactoryBinding binding) : IWorldSystemFactory
    {
        public WorldSystemFactoryBinding Binding => binding;
    }

    private readonly struct CleanupSystemRelease(CleanupProbe cleanup) : IWorldSystemRelease<CleanupSystem>
    {
        public void Invoke(CleanupSystem system) => cleanup.Dispose();
    }

    private sealed class InlineContinuationScheduler : TaskScheduler
    {
        protected override IEnumerable<Task>? GetScheduledTasks() => [];

        protected override void QueueTask(Task task)
        {
            if (!TryExecuteTask(task))
            {
                throw new InvalidOperationException("The unload completion continuation could not be executed inline.");
            }
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
            => !taskWasPreviouslyQueued && TryExecuteTask(task);
    }

    private readonly record struct OwnedScene(
        SceneInstance Instance,
        SceneExpansionPlan Plan,
        WorldSceneStorageReservation Receiver,
        WorldDomainReservation Parent,
        ImmutableArray<Entity> Entities);
}
