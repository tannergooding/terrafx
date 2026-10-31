// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Scenes;

internal sealed class SceneHierarchy
{
    internal SceneHierarchy(
        ImmutableArray<SceneHierarchyNode> nodes,
        ImmutableArray<SceneHierarchyEntry> roots)
    {
        if (nodes.IsDefault)
        {
            throw new ArgumentException("The hierarchy node array must be initialized.", nameof(nodes));
        }

        if (roots.IsDefault)
        {
            throw new ArgumentException("The hierarchy root array must be initialized.", nameof(roots));
        }

        Nodes = nodes;
        Roots = roots;
    }

    internal ImmutableArray<SceneHierarchyNode> Nodes { get; }

    internal ImmutableArray<SceneHierarchyEntry> Roots { get; }
}
