// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Scenes;

internal static class SceneHierarchyValidator
{
    private const int MaximumHierarchyDepth = 256;

    private static readonly IComparer<Guid> s_entityComparer = new GuidIdentityComparer();
    private static readonly IComparer<SceneHierarchyPlacement> s_placementComparer = new PlacementIdentityComparer();

    internal static SceneHierarchyValidationResult Validate(
        SceneHierarchy hierarchy,
        ReadOnlySpan<Guid> entityIds,
        ReadOnlySpan<SceneHierarchyPlacement> placements,
        SceneHierarchyValidationWorkspace workspace,
        int maximumHierarchyDepth)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumHierarchyDepth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumHierarchyDepth, MaximumHierarchyDepth);

        if ((workspace.EntityCount != entityIds.Length)
            || (workspace.PlacementCount != placements.Length))
        {
            return CreateFailure(SceneHierarchyValidationError.InvalidWorkspace);
        }

        if (hierarchy.Nodes.Length != entityIds.Length)
        {
            return CreateFailure(SceneHierarchyValidationError.EntityCountMismatch);
        }

        workspace.Reset();
        entityIds.CopyTo(workspace.EntityIds);
        placements.CopyTo(workspace.Placements);
        workspace.EntityIds.Sort(s_entityComparer);
        workspace.Placements.Sort(s_placementComparer);

        var entityIdsByRfc = workspace.EntityIds;
        var placementsByRfc = workspace.Placements;

        for (var index = 0; index < entityIdsByRfc.Length; index++)
        {
            var entityId = entityIdsByRfc[index];

            if (entityId == Guid.Empty)
            {
                return CreateFailure(SceneHierarchyValidationError.EmptyEntityId, entityId);
            }

            if ((index != 0) && (entityIdsByRfc[index - 1] == entityId))
            {
                return CreateFailure(SceneHierarchyValidationError.DuplicateEntityId, entityId);
            }

            var node = hierarchy.Nodes[index];

            if ((node.EntityId != entityId) || node.Children.IsDefault)
            {
                return CreateFailure(SceneHierarchyValidationError.EntityNodeMismatch, node.EntityId);
            }

            if (node.ParentEntityId is { } parentEntityId)
            {
                var parentIndex = FindEntityIndex(entityIdsByRfc, parentEntityId);

                if ((parentEntityId == Guid.Empty) || (parentIndex < 0))
                {
                    return CreateFailure(SceneHierarchyValidationError.InvalidParent, parentEntityId);
                }

                workspace.ParentIndices[index] = parentIndex;
            }
        }

        for (var index = 0; index < placementsByRfc.Length; index++)
        {
            var placement = placementsByRfc[index];

            if (placement.PlacementId == Guid.Empty)
            {
                return CreateFailure(SceneHierarchyValidationError.EmptyPlacementId, placement.PlacementId);
            }

            if ((index != 0) && (placementsByRfc[index - 1].PlacementId == placement.PlacementId))
            {
                return CreateFailure(SceneHierarchyValidationError.DuplicatePlacementId, placement.PlacementId);
            }

            if (placement.ParentEntityId is { } parentEntityId
                && ((parentEntityId == Guid.Empty) || (FindEntityIndex(entityIdsByRfc, parentEntityId) < 0)))
            {
                return CreateFailure(SceneHierarchyValidationError.InvalidParent, parentEntityId);
            }
        }

        for (var nodeIndex = 0; nodeIndex < hierarchy.Nodes.Length; nodeIndex++)
        {
            var node = hierarchy.Nodes[nodeIndex];

            foreach (var entry in node.Children)
            {
                var result = ValidateChildEntry(entry, node, nodeIndex, entityIdsByRfc, placementsByRfc, workspace);

                if (!result.IsValid)
                {
                    return result;
                }
            }
        }

        foreach (var root in hierarchy.Roots)
        {
            var result = ValidateRootEntry(root, entityIdsByRfc, placementsByRfc, workspace);

            if (!result.IsValid)
            {
                return result;
            }
        }

        for (var index = 0; index < workspace.EntityMembershipCounts.Length; index++)
        {
            if (workspace.EntityMembershipCounts[index] != 1)
            {
                return CreateFailure(SceneHierarchyValidationError.MissingMembership, entityIdsByRfc[index]);
            }
        }

        for (var index = 0; index < workspace.PlacementMembershipCounts.Length; index++)
        {
            if (workspace.PlacementMembershipCounts[index] != 1)
            {
                return CreateFailure(SceneHierarchyValidationError.MissingMembership, placementsByRfc[index].PlacementId);
            }
        }

        return ValidateCyclesAndDepth(entityIdsByRfc, workspace, maximumHierarchyDepth);
    }

    private static SceneHierarchyValidationResult CreateFailure(
        SceneHierarchyValidationError error,
        Guid subjectId = default)
        => new(error, subjectId);

    private static int FindEntityIndex(ReadOnlySpan<Guid> entityIdsByRfc, Guid entityId)
    {
        var low = 0;
        var high = entityIdsByRfc.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = FoundationCatalogue.CompareRfc(entityIdsByRfc[middle], entityId);

            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    private static int FindPlacementIndex(
        ReadOnlySpan<SceneHierarchyPlacement> placementsByRfc,
        Guid placementId)
    {
        var low = 0;
        var high = placementsByRfc.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = FoundationCatalogue.CompareRfc(placementsByRfc[middle].PlacementId, placementId);

            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    private static SceneHierarchyValidationResult ValidateChildEntry(
        SceneHierarchyEntry entry,
        SceneHierarchyNode parentNode,
        int parentIndex,
        ReadOnlySpan<Guid> entityIdsByRfc,
        ReadOnlySpan<SceneHierarchyPlacement> placementsByRfc,
        SceneHierarchyValidationWorkspace workspace)
    {
        if (entry.Id == Guid.Empty)
        {
            return CreateFailure(SceneHierarchyValidationError.InvalidEntry, entry.Id);
        }

        if (entry.Kind == SceneHierarchyEntryKind.Entity)
        {
            var entityIndex = FindEntityIndex(entityIdsByRfc, entry.Id);

            if (entityIndex < 0)
            {
                return CreateFailure(SceneHierarchyValidationError.MissingEntity, entry.Id);
            }

            if (workspace.ParentIndices[entityIndex] != parentIndex)
            {
                return CreateFailure(SceneHierarchyValidationError.ParentMismatch, entry.Id);
            }

            if (workspace.EntityMembershipCounts[entityIndex] != 0)
            {
                return CreateFailure(SceneHierarchyValidationError.DuplicateMembership, entry.Id);
            }

            workspace.EntityMembershipCounts[entityIndex] = 1;
            return new SceneHierarchyValidationResult(SceneHierarchyValidationError.None, Guid.Empty);
        }

        if (entry.Kind == SceneHierarchyEntryKind.PrefabPlacement)
        {
            var placementIndex = FindPlacementIndex(placementsByRfc, entry.Id);

            if (placementIndex < 0)
            {
                return CreateFailure(SceneHierarchyValidationError.MissingPlacement, entry.Id);
            }

            if (placementsByRfc[placementIndex].ParentEntityId != parentNode.EntityId)
            {
                return CreateFailure(SceneHierarchyValidationError.ParentMismatch, entry.Id);
            }

            if (workspace.PlacementMembershipCounts[placementIndex] != 0)
            {
                return CreateFailure(SceneHierarchyValidationError.DuplicateMembership, entry.Id);
            }

            workspace.PlacementMembershipCounts[placementIndex] = 1;
            return new SceneHierarchyValidationResult(SceneHierarchyValidationError.None, Guid.Empty);
        }

        return CreateFailure(SceneHierarchyValidationError.InvalidEntry, entry.Id);
    }

    private static SceneHierarchyValidationResult ValidateCyclesAndDepth(
        ReadOnlySpan<Guid> entityIdsByRfc,
        SceneHierarchyValidationWorkspace workspace,
        int maximumHierarchyDepth)
    {
        var parentIndices = workspace.ParentIndices;
        var traversalStates = workspace.TraversalStates;
        var depths = workspace.Depths;
        var traversalPath = workspace.TraversalPath;

        for (var startIndex = 0; startIndex < entityIdsByRfc.Length; startIndex++)
        {
            if (traversalStates[startIndex] == 2)
            {
                continue;
            }

            var pathCount = 0;
            var currentIndex = startIndex;

            while ((currentIndex >= 0) && (traversalStates[currentIndex] == 0))
            {
                traversalStates[currentIndex] = 1;
                traversalPath[pathCount++] = currentIndex;
                currentIndex = parentIndices[currentIndex];
            }

            if ((currentIndex >= 0) && (traversalStates[currentIndex] == 1))
            {
                return CreateFailure(SceneHierarchyValidationError.Cycle, entityIdsByRfc[currentIndex]);
            }

            var depth = (currentIndex >= 0) ? depths[currentIndex] : 0;

            for (var pathIndex = pathCount - 1; pathIndex >= 0; pathIndex--)
            {
                var entityIndex = traversalPath[pathIndex];
                depth++;

                if (depth > maximumHierarchyDepth)
                {
                    return CreateFailure(SceneHierarchyValidationError.DepthExceeded, entityIdsByRfc[entityIndex]);
                }

                depths[entityIndex] = checked((ushort)depth);
                traversalStates[entityIndex] = 2;
            }
        }

        return new SceneHierarchyValidationResult(SceneHierarchyValidationError.None, Guid.Empty);
    }

    private static SceneHierarchyValidationResult ValidateRootEntry(
        SceneHierarchyEntry entry,
        ReadOnlySpan<Guid> entityIdsByRfc,
        ReadOnlySpan<SceneHierarchyPlacement> placementsByRfc,
        SceneHierarchyValidationWorkspace workspace)
    {
        if (entry.Id == Guid.Empty)
        {
            return CreateFailure(SceneHierarchyValidationError.InvalidEntry, entry.Id);
        }

        if (entry.Kind == SceneHierarchyEntryKind.Entity)
        {
            var entityIndex = FindEntityIndex(entityIdsByRfc, entry.Id);

            if (entityIndex < 0)
            {
                return CreateFailure(SceneHierarchyValidationError.MissingEntity, entry.Id);
            }

            if (workspace.ParentIndices[entityIndex] != -1)
            {
                return CreateFailure(SceneHierarchyValidationError.ParentMismatch, entry.Id);
            }

            if (workspace.EntityMembershipCounts[entityIndex] != 0)
            {
                return CreateFailure(SceneHierarchyValidationError.DuplicateMembership, entry.Id);
            }

            workspace.EntityMembershipCounts[entityIndex] = 1;
            return new SceneHierarchyValidationResult(SceneHierarchyValidationError.None, Guid.Empty);
        }

        if (entry.Kind == SceneHierarchyEntryKind.PrefabPlacement)
        {
            var placementIndex = FindPlacementIndex(placementsByRfc, entry.Id);

            if (placementIndex < 0)
            {
                return CreateFailure(SceneHierarchyValidationError.MissingPlacement, entry.Id);
            }

            if (placementsByRfc[placementIndex].ParentEntityId.HasValue)
            {
                return CreateFailure(SceneHierarchyValidationError.ParentMismatch, entry.Id);
            }

            if (workspace.PlacementMembershipCounts[placementIndex] != 0)
            {
                return CreateFailure(SceneHierarchyValidationError.DuplicateMembership, entry.Id);
            }

            workspace.PlacementMembershipCounts[placementIndex] = 1;
            return new SceneHierarchyValidationResult(SceneHierarchyValidationError.None, Guid.Empty);
        }

        return CreateFailure(SceneHierarchyValidationError.InvalidEntry, entry.Id);
    }

    private sealed class GuidIdentityComparer : IComparer<Guid>
    {
        public int Compare(Guid left, Guid right)
            => FoundationCatalogue.CompareRfc(left, right);
    }

    private sealed class PlacementIdentityComparer : IComparer<SceneHierarchyPlacement>
    {
        public int Compare(SceneHierarchyPlacement left, SceneHierarchyPlacement right)
            => FoundationCatalogue.CompareRfc(left.PlacementId, right.PlacementId);
    }
}
