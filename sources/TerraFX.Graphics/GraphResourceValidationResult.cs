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
        ImmutableArray<RenderPassId> orderedPasses)
    {
        if (orderedPasses.IsDefault)
        {
            throw new ArgumentException("The ordered pass list must be initialized.", nameof(orderedPasses));
        }

        if (!(accessesValid && importsValid && contentsValid && dependenciesValid) && !orderedPasses.IsEmpty)
        {
            throw new ArgumentException("An invalid graph cannot expose an ordered pass plan.", nameof(orderedPasses));
        }

        AccessesValid = accessesValid;
        ContentsValid = contentsValid;
        DependenciesValid = dependenciesValid;
        ImportsValid = importsValid;
        OrderedPasses = [.. orderedPasses];
    }

    internal bool AccessesValid { get; }

    internal bool ContentsValid { get; }

    internal bool DependenciesValid { get; }

    internal bool ImportsValid { get; }

    internal bool IsValid => AccessesValid && ImportsValid && ContentsValid && DependenciesValid;

    internal ImmutableArray<RenderPassId> OrderedPasses { get; }
}
