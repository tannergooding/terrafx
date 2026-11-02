// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal readonly struct GraphResourceValidationResult
{
    internal GraphResourceValidationResult(
        bool accessesValid,
        bool importsValid,
        bool contentsValid,
        bool dependenciesValid,
        ImmutableArray<RenderPassId> orderedPasses,
        ImmutableArray<GraphResourceStateTransition> stateTransitions)
    {
        if (orderedPasses.IsDefault)
        {
            throw new ArgumentException("The ordered pass list must be initialized.", nameof(orderedPasses));
        }

        if (stateTransitions.IsDefault)
        {
            throw new ArgumentException("The graph resource state transition list must be initialized.", nameof(stateTransitions));
        }

        if (!(accessesValid && importsValid && contentsValid && dependenciesValid)
            && (!orderedPasses.IsEmpty || !stateTransitions.IsEmpty))
        {
            throw new ArgumentException("An invalid graph cannot expose ordered passes or state transitions.");
        }

        AccessesValid = accessesValid;
        ContentsValid = contentsValid;
        DependenciesValid = dependenciesValid;
        ImportsValid = importsValid;
        OrderedPasses = [.. orderedPasses];
        StateTransitions = [.. stateTransitions];
    }

    internal bool AccessesValid { get; }

    internal bool ContentsValid { get; }

    internal bool DependenciesValid { get; }

    internal bool ImportsValid { get; }

    internal bool IsValid => AccessesValid && ImportsValid && ContentsValid && DependenciesValid;

    internal ImmutableArray<RenderPassId> OrderedPasses { get; }

    internal ImmutableArray<GraphResourceStateTransition> StateTransitions { get; }
}
