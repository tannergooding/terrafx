// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal sealed class SceneHierarchyTransactionPlan
{
    private readonly long _baseStructuralRevision;
    private readonly ImmutableArray<WorldHierarchyComponentMutation> _componentMutations;
    private readonly ImmutableArray<WorldHierarchyParentMutation> _parentMutations;

    internal SceneHierarchyTransactionPlan(
        long baseStructuralRevision,
        ImmutableArray<WorldHierarchyComponentMutation> componentMutations,
        ImmutableArray<WorldHierarchyParentMutation> parentMutations)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseStructuralRevision);

        if (componentMutations.IsDefault || parentMutations.IsDefault)
        {
            throw new ArgumentException("The hierarchy transaction mutation arrays must be initialized.");
        }

        _baseStructuralRevision = baseStructuralRevision;
        _componentMutations = componentMutations;
        _parentMutations = parentMutations;
    }

    internal long BaseStructuralRevision => _baseStructuralRevision;

    internal ImmutableArray<WorldHierarchyComponentMutation> ComponentMutations => _componentMutations;

    internal ImmutableArray<WorldHierarchyParentMutation> ParentMutations => _parentMutations;
}
