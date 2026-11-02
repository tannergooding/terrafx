// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal readonly struct GraphResourceValidationResult
{
    internal GraphResourceValidationResult(
        bool importsValid,
        bool contentsValid,
        bool dependenciesValid,
        ImmutableArray<RenderPassId> orderedPasses)
    {
        if (orderedPasses.IsDefault)
        {
            throw new ArgumentException("The ordered pass list must be initialized.", nameof(orderedPasses));
        }

        if (!(importsValid && contentsValid && dependenciesValid) && !orderedPasses.IsEmpty)
        {
            throw new ArgumentException("An invalid graph cannot expose an ordered pass plan.", nameof(orderedPasses));
        }

        ContentsValid = contentsValid;
        DependenciesValid = dependenciesValid;
        ImportsValid = importsValid;
        OrderedPasses = [.. orderedPasses];
    }

    internal bool ContentsValid { get; }

    internal bool DependenciesValid { get; }

    internal bool ImportsValid { get; }

    internal bool IsValid => ImportsValid && ContentsValid && DependenciesValid;

    internal ImmutableArray<RenderPassId> OrderedPasses { get; }
}
