// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using NUnit.Framework;
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
