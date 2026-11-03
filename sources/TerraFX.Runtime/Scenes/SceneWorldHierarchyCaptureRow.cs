// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal struct SceneWorldHierarchyCaptureRow
{
    internal int ChildCount;

    internal int ChildOffset;

    internal Guid EntityId;

    internal int NextChildOffset;

    internal Guid? ParentEntityId;

    internal SceneWorldHierarchyCaptureRow(Guid entityId, Guid? parentEntityId)
    {
        EntityId = entityId;
        ParentEntityId = parentEntityId;
        ChildCount = 0;
        ChildOffset = 0;
        NextChildOffset = 0;
    }
}
