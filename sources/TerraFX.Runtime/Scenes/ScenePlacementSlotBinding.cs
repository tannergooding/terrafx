// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal sealed class ScenePlacementSlotBinding
{
    internal ScenePlacementSlotBinding(Guid slotId, SceneReferenceTarget? target)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(slotId, Guid.Empty);
        SlotId = slotId;
        Target = target;
    }

    internal Guid SlotId { get; }

    internal SceneReferenceTarget? Target { get; }
}
