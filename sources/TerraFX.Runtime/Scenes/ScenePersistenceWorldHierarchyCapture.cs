// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class ScenePersistenceWorldHierarchyCapture
{
    internal static void Capture(
        World world,
        WorldPersistenceCaptureLease lease,
        scoped Span<World.EntityStateCaptureRow> entityRows,
        scoped Span<SceneWorldHierarchyCaptureRow> hierarchyRows,
        out int rootCount,
        out int childCount)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease belongs to another World.", nameof(lease));
        }

        var entityCount = lease.GetEntityStateCount();

        if ((entityRows.Length != entityCount) || (hierarchyRows.Length != entityCount))
        {
            throw new ArgumentException("The World-hierarchy capture workspaces must match the exact live Entity count.");
        }

        var entityEnumerator = lease.GetCanonicalEntityStateEnumerator(entityRows);
        var hasSceneSupport = world.Registration.SceneSupport is not null;
        rootCount = 0;
        childCount = 0;
        var rowIndex = 0;

        while (entityEnumerator.MoveNext())
        {
            if (rowIndex >= hierarchyRows.Length)
            {
                throw new InvalidOperationException("The canonical Entity capture contains more rows than the live Entity count.");
            }

            var entityId = entityEnumerator.CurrentEntityId;
            Guid? parentEntityId = null;

            if (hasSceneSupport
                && world.TryGetHierarchyParent(entityEnumerator.CurrentEntity, out var parent))
            {
                parentEntityId = world.GetEntityId(parent);

                if (parentEntityId == entityId)
                {
                    throw new InvalidOperationException("The World hierarchy contains a self-parent link.");
                }

                childCount = checked(childCount + 1);
            }
            else
            {
                rootCount = checked(rootCount + 1);
            }

            hierarchyRows[rowIndex++] = new SceneWorldHierarchyCaptureRow(entityId, parentEntityId);
        }

        if (rowIndex != entityCount)
        {
            throw new InvalidOperationException("The canonical Entity capture does not contain every live Entity.");
        }

        for (var index = 1; index < hierarchyRows.Length; index++)
        {
            if (FoundationCatalogue.CompareRfc(hierarchyRows[index - 1].EntityId, hierarchyRows[index].EntityId) >= 0)
            {
                throw new InvalidOperationException("The canonical Entity capture contains unordered or duplicate stable identities.");
            }
        }

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            var row = hierarchyRows[index];

            if (row.ParentEntityId is Guid parentEntityId)
            {
                var parentIndex = FindNodeIndex(hierarchyRows, parentEntityId);

                if (parentIndex < 0)
                {
                    throw new InvalidOperationException("A live World hierarchy parent is absent from the canonical Entity capture.");
                }

                hierarchyRows[parentIndex].ChildCount = checked(hierarchyRows[parentIndex].ChildCount + 1);
            }
        }

        var childOffset = 0;

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            ref var row = ref hierarchyRows[index];
            row.ChildOffset = childOffset;
            row.NextChildOffset = childOffset;
            childOffset = checked(childOffset + row.ChildCount);
        }

        if ((childOffset != childCount) || (checked(rootCount + childCount) != entityCount))
        {
            throw new InvalidOperationException("The World hierarchy root and parent counts do not cover every live Entity.");
        }
    }

    internal static void CopyCanonicalLists(
        scoped Span<SceneWorldHierarchyCaptureRow> hierarchyRows,
        scoped Span<Guid> rootEntityIds,
        scoped Span<Guid> childEntityIds)
    {
        var rootCount = 0;
        var childCount = 0;

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            var row = hierarchyRows[index];
            rootCount = checked(rootCount + (row.ParentEntityId.HasValue ? 0 : 1));
            childCount = checked(childCount + row.ChildCount);
        }

        if ((rootEntityIds.Length != rootCount) || (childEntityIds.Length != childCount))
        {
            throw new ArgumentException("The World-hierarchy root and child destinations must match their exact counts.");
        }

        var rootIndex = 0;

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            ref var row = ref hierarchyRows[index];
            row.NextChildOffset = row.ChildOffset;

            if (!row.ParentEntityId.HasValue)
            {
                rootEntityIds[rootIndex++] = row.EntityId;
            }
        }

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            var child = hierarchyRows[index];

            if (child.ParentEntityId is not Guid parentEntityId)
            {
                continue;
            }

            var parentIndex = FindNodeIndex(hierarchyRows, parentEntityId);

            if (parentIndex < 0)
            {
                throw new InvalidOperationException("A captured World hierarchy parent is absent from the canonical Entity rows.");
            }

            ref var parent = ref hierarchyRows[parentIndex];
            var end = checked(parent.ChildOffset + parent.ChildCount);

            if ((parent.NextChildOffset < parent.ChildOffset) || (parent.NextChildOffset >= end))
            {
                throw new InvalidOperationException("The captured World hierarchy child count changed while filling its canonical list.");
            }

            childEntityIds[parent.NextChildOffset++] = child.EntityId;
        }

        for (var index = 0; index < hierarchyRows.Length; index++)
        {
            var row = hierarchyRows[index];
            var end = checked(row.ChildOffset + row.ChildCount);

            if (row.NextChildOffset != end)
            {
                throw new InvalidOperationException("The captured World hierarchy child list is incomplete.");
            }

            for (var childIndex = row.ChildOffset + 1; childIndex < end; childIndex++)
            {
                if (FoundationCatalogue.CompareRfc(childEntityIds[childIndex - 1], childEntityIds[childIndex]) >= 0)
                {
                    throw new InvalidOperationException("The captured World hierarchy child list is not in RFC EntityId order.");
                }
            }
        }
    }

    private static int FindNodeIndex(scoped ReadOnlySpan<SceneWorldHierarchyCaptureRow> rows, Guid entityId)
    {
        var lower = 0;
        var upper = rows.Length - 1;

        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var comparison = FoundationCatalogue.CompareRfc(rows[middle].EntityId, entityId);

            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        return -1;
    }
}
