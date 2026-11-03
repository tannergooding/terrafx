// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class ScenePersistenceWorldHierarchyCapture
{
    private static readonly IComparer<SceneWorldHierarchyCaptureRow> s_assignmentOrdinalComparer =
        new AssignmentOrdinalComparer();
    private static readonly IComparer<SceneWorldHierarchyCaptureRow> s_entityIdComparer =
        new EntityIdComparer();
    private static readonly IComparer<Guid> s_rfcGuidComparer = new GuidIdentityComparer();

    internal static bool AreRfcUnique(scoped ReadOnlySpan<Guid> values, scoped Span<Guid> scratch)
    {
        if (scratch.Length < values.Length)
        {
            throw new ArgumentException("The GUID uniqueness workspace is too small.", nameof(scratch));
        }

        var sorted = scratch[..values.Length];
        values.CopyTo(sorted);
        sorted.Sort(s_rfcGuidComparer);

        for (var index = 1; index < sorted.Length; index++)
        {
            if (FoundationCatalogue.CompareRfc(sorted[index - 1], sorted[index]) == 0)
            {
                return false;
            }
        }

        return true;
    }

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
            var assignmentOrdinal = entityEnumerator.CurrentAssignmentOrdinal;
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

            hierarchyRows[rowIndex++] = new SceneWorldHierarchyCaptureRow(entityId, assignmentOrdinal, parentEntityId);
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
            childOffset = checked(childOffset + row.ChildCount);
        }

        if ((childOffset != childCount) || (checked(rootCount + childCount) != entityCount))
        {
            throw new InvalidOperationException("The World hierarchy root and parent counts do not cover every live Entity.");
        }
    }

    internal static void CopyCanonicalLists(
        World world,
        WorldPersistenceCaptureLease lease,
        scoped ReadOnlySpan<World.EntityStateCaptureRow> entityRows,
        scoped Span<SceneWorldHierarchyCaptureRow> hierarchyRows,
        scoped Span<Guid> rootEntityIds,
        scoped Span<Guid> childEntityIds)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease belongs to another World.", nameof(lease));
        }

        if (entityRows.Length != hierarchyRows.Length)
        {
            throw new ArgumentException("The Entity and hierarchy rows must match their exact live count.");
        }

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
            if ((entityRows[index].EntityId != hierarchyRows[index].EntityId)
                || (entityRows[index].AssignmentOrdinal != hierarchyRows[index].AssignmentOrdinal))
            {
                throw new InvalidOperationException("The captured Entity and hierarchy rows no longer match.");
            }
        }

        hierarchyRows.Sort(s_assignmentOrdinalComparer);

        try
        {
            for (var index = 0; index < hierarchyRows.Length; index++)
            {
                var row = hierarchyRows[index];

                if (!row.ParentEntityId.HasValue)
                {
                    rootEntityIds[rootIndex++] = row.EntityId;
                }
            }
        }
        finally
        {
            hierarchyRows.Sort(s_entityIdComparer);
        }

        if (rootIndex != rootEntityIds.Length)
        {
            throw new InvalidOperationException("The World hierarchy root list is incomplete.");
        }

        for (var parentIndex = 0; parentIndex < hierarchyRows.Length; parentIndex++)
        {
            var parent = hierarchyRows[parentIndex];

            if (parent.ChildCount == 0)
            {
                continue;
            }

            if (!world.TryGetFirstHierarchyChild(entityRows[parentIndex].Entity, out var child))
            {
                throw new InvalidOperationException("The captured World hierarchy child list is incomplete.");
            }

            var childOffset = parent.ChildOffset;
            var childEnd = checked(childOffset + parent.ChildCount);

            while (true)
            {
                if (childOffset >= childEnd)
                {
                    throw new InvalidOperationException("The captured World hierarchy child list exceeds its measured count.");
                }

                var childEntityId = world.GetEntityId(child);
                var childIndex = FindNodeIndex(hierarchyRows, childEntityId);

                if ((childIndex < 0) || (hierarchyRows[childIndex].ParentEntityId != parent.EntityId))
                {
                    throw new InvalidOperationException("The captured World hierarchy sibling does not match its parent row.");
                }

                childEntityIds[childOffset++] = childEntityId;

                if (!world.TryGetNextHierarchySibling(child, out child))
                {
                    break;
                }
            }

            if (childOffset != childEnd)
            {
                throw new InvalidOperationException("The captured World hierarchy child list is incomplete.");
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

    private sealed class AssignmentOrdinalComparer : IComparer<SceneWorldHierarchyCaptureRow>
    {
        public int Compare(SceneWorldHierarchyCaptureRow left, SceneWorldHierarchyCaptureRow right)
            => left.AssignmentOrdinal.CompareTo(right.AssignmentOrdinal);
    }

    private sealed class EntityIdComparer : IComparer<SceneWorldHierarchyCaptureRow>
    {
        public int Compare(SceneWorldHierarchyCaptureRow left, SceneWorldHierarchyCaptureRow right)
            => FoundationCatalogue.CompareRfc(left.EntityId, right.EntityId);
    }

    private sealed class GuidIdentityComparer : IComparer<Guid>
    {
        public int Compare(Guid left, Guid right)
            => FoundationCatalogue.CompareRfc(left, right);
    }
}
