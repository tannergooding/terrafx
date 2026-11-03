// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal struct SceneWorldHierarchyCaptureRow
{
    internal int ChildCount;

    internal int ChildOffset;

    internal Guid EntityId;

    internal uint AssignmentOrdinal;

    internal Guid? ParentEntityId;

    internal SceneWorldHierarchyCaptureRow(Guid entityId, uint assignmentOrdinal, Guid? parentEntityId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(assignmentOrdinal);
        EntityId = entityId;
        AssignmentOrdinal = assignmentOrdinal;
        ParentEntityId = parentEntityId;
        ChildCount = 0;
        ChildOffset = 0;
    }
}
