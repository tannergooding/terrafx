// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal sealed class WorldPersistenceRegistrationContents
{
    internal WorldPersistenceRegistrationContents(int componentCount, int tagCount, int systemCount, int resourceCount)
    {
        ComponentCount = componentCount;
        TagCount = tagCount;
        SystemCount = systemCount;
        ResourceCount = resourceCount;
    }

    internal int ComponentCount { get; }

    internal int ResourceCount { get; }

    internal int SystemCount { get; }

    internal int TagCount { get; }
}
