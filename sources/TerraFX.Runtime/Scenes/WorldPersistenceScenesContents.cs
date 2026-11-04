// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Scenes;

internal sealed class WorldPersistenceScenesContents
{
    internal WorldPersistenceScenesContents(int instanceCount, int hierarchyNodeCount, int rootCount)
    {
        InstanceCount = instanceCount;
        HierarchyNodeCount = hierarchyNodeCount;
        RootCount = rootCount;
    }

    internal int HierarchyNodeCount { get; }

    internal int InstanceCount { get; }

    internal int RootCount { get; }
}
