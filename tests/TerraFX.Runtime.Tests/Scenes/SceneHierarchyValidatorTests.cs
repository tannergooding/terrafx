// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneHierarchyValidatorTests
{
    [Test]
    public void AcceptsCompleteEntityAndPlacementMembership()
    {
        var rootEntityId = new Guid("10000000-0000-4000-8000-000000000001");
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000002");
        var childPlacementId = new Guid("30000000-0000-4000-8000-000000000003");
        var rootPlacementId = new Guid("40000000-0000-4000-8000-000000000004");
        var hierarchy = new SceneHierarchy(
            [
                new SceneHierarchyNode(
                    rootEntityId,
                    null,
                    [
                        new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, childEntityId),
                        new SceneHierarchyEntry(SceneHierarchyEntryKind.PrefabPlacement, childPlacementId),
                    ]),
                new SceneHierarchyNode(childEntityId, rootEntityId, []),
            ],
            [
                new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, rootEntityId),
                new SceneHierarchyEntry(SceneHierarchyEntryKind.PrefabPlacement, rootPlacementId),
            ]);
        Guid[] entityIds = [childEntityId, rootEntityId];
        SceneHierarchyPlacement[] placements = [
            new(childPlacementId, rootEntityId),
            new(rootPlacementId, null),
        ];
        var workspace = new SceneHierarchyValidationWorkspace(entityIds.Length, placements.Length);

        var result = SceneHierarchyValidator.Validate(hierarchy, entityIds, placements, workspace, 256);

        Assert.That(result.Error, Is.EqualTo(SceneHierarchyValidationError.None));
        Assert.That(result.IsValid, Is.True);
        Assert.That(workspace.ByteReservation, Is.GreaterThan(0));
    }

    [Test]
    [TestCase(-1, 1)]
    [TestCase(0, 0)]
    public void ArrayBytesRejectInvalidDimensions(int count, int elementSize)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => ScenePreparationDataBounds.ArrayBytes(count, elementSize));
    }

    [Test]
    [TestCase(0, 4, 64)]
    [TestCase(1, 4, 80)]
    [TestCase(4, 4, 80)]
    [TestCase(5, 4, 96)]
    [TestCase(1, 24, 96)]
    public void ArrayBytesUseCheckedSixteenByteRounding(
        int count,
        int elementSize,
        long expectedBytes)
    {
        Assert.That(
            ScenePreparationDataBounds.ArrayBytes(count, elementSize),
            Is.EqualTo(expectedBytes));
    }

    [Test]
    public void PrefabLeaseCacheCarrierMatchesSelectedRuntimeLayout()
    {
        const int SampleCount = 64;
        const int ExpectedPrefabLeaseCacheCarrierBytes = 80;
        var cacheType = typeof(Dictionary<AssetId, AssetLease<ScenePrefabAsset>>);
        var warmInstances = new object[SampleCount];

        for (var index = 0; index < warmInstances.Length; index++)
        {
            warmInstances[index] = RuntimeHelpers.GetUninitializedObject(cacheType);
        }

        GC.KeepAlive(warmInstances);
        var measuredInstances = new object[SampleCount];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < measuredInstances.Length; index++)
        {
            measuredInstances[index] = RuntimeHelpers.GetUninitializedObject(cacheType);
        }

        var measuredObjectBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
        GC.KeepAlive(measuredInstances);

        Assert.That(measuredObjectBytes, Is.EqualTo(ExpectedPrefabLeaseCacheCarrierBytes));
    }

    [Test]
    public void PrefabLeaseCacheGrowthFitsPerEntryAllocationAllowance()
    {
        const int EntryCount = 65_536;
        const long PrefabLeaseCacheCarrierBytes = 80;
        const long PerPrefabEntryAllowanceBytes = 256;
        var assetIds = new AssetId[EntryCount];

        for (var index = 0; index < assetIds.Length; index++)
        {
            assetIds[index] = new AssetId(index + 1);
        }

        var warmCache = new Dictionary<AssetId, AssetLease<ScenePrefabAsset>?> {
            [assetIds[0]] = null,
        };
        GC.KeepAlive(warmCache);

        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var cache = new Dictionary<AssetId, AssetLease<ScenePrefabAsset>?>();
        long firstExceededEntryCount = -1;
        long observedBytesAtFirstExcess = 0;
        long allowanceAtFirstExcess = 0;

        for (var index = 0; index < assetIds.Length; index++)
        {
            cache.Add(assetIds[index], null);
            var observedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            var allowanceBytes = PrefabLeaseCacheCarrierBytes
                + (PerPrefabEntryAllowanceBytes * (index + 1L));

            if ((firstExceededEntryCount < 0) && (observedBytes > allowanceBytes))
            {
                firstExceededEntryCount = index + 1L;
                observedBytesAtFirstExcess = observedBytes;
                allowanceAtFirstExcess = allowanceBytes;
            }
        }

        GC.KeepAlive(cache);

        Assert.That(
            firstExceededEntryCount,
            Is.EqualTo(-1),
            $"The cache exceeded its allowance at {firstExceededEntryCount} entries: " +
            $"{observedBytesAtFirstExcess} allocated bytes versus {allowanceAtFirstExcess} allowed.");
    }

    [Test]
    public void PreparedSceneCarrierMatchesSelectedRuntimeLayout()
    {
        const int SampleCount = 64;
        const int ExpectedPreparedSceneCarrierBytes = 488;
        var preparationType = typeof(PreparedScene);
        var warmInstances = new object[SampleCount];

        for (var index = 0; index < warmInstances.Length; index++)
        {
            warmInstances[index] = RuntimeHelpers.GetUninitializedObject(preparationType);
        }

        GC.KeepAlive(warmInstances);
        var measuredInstances = new object[SampleCount];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < measuredInstances.Length; index++)
        {
            measuredInstances[index] = RuntimeHelpers.GetUninitializedObject(preparationType);
        }

        var measuredObjectBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
        GC.KeepAlive(measuredInstances);

        Assert.That(measuredObjectBytes, Is.EqualTo(ExpectedPreparedSceneCarrierBytes));
    }

    [Test]
    public void PreparedSceneCompletionSourceAndTaskAllocationMatchesSelectedRuntime()
    {
        const int SampleCount = 64;
        const int ExpectedCompletionSourceAndTaskBytes = 96;
        var completionSources = new TaskCompletionSource<PreparedScene>[SampleCount];
        var tasks = new Task<PreparedScene>[SampleCount];
        var warmSource = new TaskCompletionSource<PreparedScene>(TaskCreationOptions.RunContinuationsAsynchronously);
        var warmTask = warmSource.Task;
        GC.KeepAlive(warmTask);
        GC.KeepAlive(warmSource);
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < completionSources.Length; index++)
        {
            completionSources[index] = new TaskCompletionSource<PreparedScene>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            tasks[index] = completionSources[index].Task;
        }

        var measuredObjectBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
        GC.KeepAlive(completionSources);
        GC.KeepAlive(tasks);

        Assert.That(measuredObjectBytes, Is.EqualTo(ExpectedCompletionSourceAndTaskBytes));
    }

    [Test]
    public void RejectsCyclesAndHierarchyDepthBeyondTheConfiguredLimit()
    {
        var firstEntityId = new Guid("10000000-0000-4000-8000-000000000001");
        var secondEntityId = new Guid("20000000-0000-4000-8000-000000000002");
        var cycle = new SceneHierarchy(
            [
                new SceneHierarchyNode(
                    firstEntityId,
                    secondEntityId,
                    [new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, secondEntityId)]),
                new SceneHierarchyNode(
                    secondEntityId,
                    firstEntityId,
                    [new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, firstEntityId)]),
            ],
            []);
        Guid[] cycleEntityIds = [firstEntityId, secondEntityId];
        var cycleWorkspace = new SceneHierarchyValidationWorkspace(cycleEntityIds.Length, 0);

        var cycleResult = SceneHierarchyValidator.Validate(cycle, cycleEntityIds, [], cycleWorkspace, 256);

        Assert.That(cycleResult.Error, Is.EqualTo(SceneHierarchyValidationError.Cycle));

        var acceptedDepth = HierarchyFixture.CreateEntityChain(256);
        var acceptedWorkspace = new SceneHierarchyValidationWorkspace(acceptedDepth.Nodes.Length, 0);
        var acceptedResult = SceneHierarchyValidator.Validate(
            acceptedDepth,
            HierarchyFixture.GetEntityIds(acceptedDepth),
            [],
            acceptedWorkspace,
            256);

        Assert.That(acceptedResult.IsValid, Is.True);

        var rejectedDepth = HierarchyFixture.CreateEntityChain(257);
        var rejectedWorkspace = new SceneHierarchyValidationWorkspace(rejectedDepth.Nodes.Length, 0);
        var rejectedResult = SceneHierarchyValidator.Validate(
            rejectedDepth,
            HierarchyFixture.GetEntityIds(rejectedDepth),
            [],
            rejectedWorkspace,
            256);

        Assert.That(rejectedResult.Error, Is.EqualTo(SceneHierarchyValidationError.DepthExceeded));
    }

    [Test]
    public void RejectsParentMismatchAndDuplicateMembership()
    {
        var rootEntityId = new Guid("10000000-0000-4000-8000-000000000001");
        var childEntityId = new Guid("20000000-0000-4000-8000-000000000002");
        Guid[] entityIds = [rootEntityId, childEntityId];
        var parentMismatch = new SceneHierarchy(
            [
                new SceneHierarchyNode(
                    rootEntityId,
                    null,
                    [new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, childEntityId)]),
                new SceneHierarchyNode(childEntityId, null, []),
            ],
            [
                new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, rootEntityId),
                new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, childEntityId),
            ]);
        var workspace = new SceneHierarchyValidationWorkspace(entityIds.Length, 0);

        var parentResult = SceneHierarchyValidator.Validate(parentMismatch, entityIds, [], workspace, 256);

        Assert.That(parentResult.Error, Is.EqualTo(SceneHierarchyValidationError.ParentMismatch));

        var duplicateMembership = new SceneHierarchy(
            [new SceneHierarchyNode(rootEntityId, null, [])],
            [
                new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, rootEntityId),
                new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, rootEntityId),
            ]);
        var duplicateResult = SceneHierarchyValidator.Validate(
            duplicateMembership,
            [rootEntityId],
            [],
            new SceneHierarchyValidationWorkspace(1, 0),
            256);

        Assert.That(duplicateResult.Error, Is.EqualTo(SceneHierarchyValidationError.DuplicateMembership));
    }

    [Test]
    public void SceneExpansionBuilderCarrierMatchesSelectedRuntimeLayout()
    {
        const int SampleCount = 64;
        const int ExpectedBuilderCarrierBytes = 448;
        var builderType = typeof(ScenePrefabExpansionPlanner).GetNestedType(
            "SceneExpansionBuilder",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The Scene expansion builder type is unavailable.");
        var warmInstances = new object[SampleCount];

        for (var index = 0; index < warmInstances.Length; index++)
        {
            warmInstances[index] = RuntimeHelpers.GetUninitializedObject(builderType);
        }

        GC.KeepAlive(warmInstances);
        var measuredInstances = new object[SampleCount];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < measuredInstances.Length; index++)
        {
            measuredInstances[index] = RuntimeHelpers.GetUninitializedObject(builderType);
        }

        var measuredObjectBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
        GC.KeepAlive(measuredInstances);

        Assert.That(measuredObjectBytes, Is.EqualTo(ExpectedBuilderCarrierBytes));
    }

    [Test]
    public void SceneExpansionPlanCarrierMatchesSelectedRuntimeLayout()
    {
        const int SampleCount = 64;
        const int ExpectedSceneExpansionPlanCarrierBytes = 160;
        var planType = typeof(SceneExpansionPlan);
        var warmInstances = new object[SampleCount];

        for (var index = 0; index < warmInstances.Length; index++)
        {
            warmInstances[index] = RuntimeHelpers.GetUninitializedObject(planType);
        }

        GC.KeepAlive(warmInstances);
        var measuredInstances = new object[SampleCount];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < measuredInstances.Length; index++)
        {
            measuredInstances[index] = RuntimeHelpers.GetUninitializedObject(planType);
        }

        var measuredObjectBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
        GC.KeepAlive(measuredInstances);

        Assert.That(measuredObjectBytes, Is.EqualTo(ExpectedSceneExpansionPlanCarrierBytes));
    }

    [Test]
    public void WorkspaceByteReservationMatchesCompiledObjectAndArrayLayout()
    {
        const int SampleCount = 64;
        var workspaceType = typeof(SceneHierarchyValidationWorkspace);
        var warmInstances = new object[SampleCount];

        for (var index = 0; index < warmInstances.Length; index++)
        {
            warmInstances[index] = RuntimeHelpers.GetUninitializedObject(workspaceType);
        }

        GC.KeepAlive(warmInstances);
        var measuredInstances = new object[SampleCount];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < measuredInstances.Length; index++)
        {
            measuredInstances[index] = RuntimeHelpers.GetUninitializedObject(workspaceType);
        }

        var measuredObjectBytes = (GC.GetAllocatedBytesForCurrentThread() - allocationStart) / SampleCount;
        GC.KeepAlive(measuredInstances);
        var expectedObjectBytes = checked(
            ((2L + 8L) * IntPtr.Size)
            + (2L * sizeof(int))
            + sizeof(long));

        static long ArrayBytes<T>(int count)
        {
            var payloadBytes = checked((long)count * Unsafe.SizeOf<T>());
            return checked(64L + ((payloadBytes + 15L) & ~15L));
        }

        Assert.That(measuredObjectBytes, Is.EqualTo(expectedObjectBytes));
        Assert.That(
            SceneHierarchyValidationWorkspace.GetRequiredByteReservation(1, 1),
            Is.EqualTo(expectedObjectBytes
                + ArrayBytes<Guid>(1)
                + ArrayBytes<SceneHierarchyPlacement>(1)
                + ArrayBytes<byte>(1)
                + ArrayBytes<byte>(1)
                + ArrayBytes<int>(1)
                + ArrayBytes<byte>(1)
                + ArrayBytes<ushort>(1)
                + ArrayBytes<int>(1)));
    }

    private static class HierarchyFixture
    {
        public static SceneHierarchy CreateEntityChain(int depth)
        {
            var nodes = ImmutableArray.CreateBuilder<SceneHierarchyNode>(depth);

            for (var index = 0; index < depth; index++)
            {
                var entityId = CreateId(index + 1);
                Guid? parentId = (index == 0) ? null : CreateId(index);
                var children = (index + 1 == depth)
                    ? ImmutableArray<SceneHierarchyEntry>.Empty
                    : [new SceneHierarchyEntry(
                        SceneHierarchyEntryKind.Entity,
                        CreateId(index + 2))];
                nodes.Add(new SceneHierarchyNode(entityId, parentId, children));
            }

            return new SceneHierarchy(
                nodes.MoveToImmutable(),
                [new SceneHierarchyEntry(SceneHierarchyEntryKind.Entity, CreateId(1))]);
        }

        public static Guid CreateId(int value)
        {
            Span<byte> bytes = stackalloc byte[16];
            bytes.Clear();
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            bytes[6] = 0x40;
            bytes[8] = 0x80;
            bytes[15] = 1;
            return new Guid(bytes, bigEndian: true);
        }

        public static Guid[] GetEntityIds(SceneHierarchy hierarchy)
        {
            var result = new Guid[hierarchy.Nodes.Length];

            for (var index = 0; index < result.Length; index++)
            {
                result[index] = hierarchy.Nodes[index].EntityId;
            }

            return result;
        }
    }
}
