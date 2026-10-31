// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Defines one root Scene external-slot target.</summary>
public sealed class SceneExternalBinding
{
    /// <summary>Initializes an explicit binding for one nonempty root Scene slot.</summary>
    /// <param name="slotId">The declared root Scene ExternalSlot identity.</param>
    /// <param name="target">The target Entity, or null for an explicit null binding.</param>
    /// <exception cref="ArgumentException"><paramref name="slotId"/> is empty or <paramref name="target"/> is a default Entity.</exception>
    public SceneExternalBinding(Guid slotId, Entity? target)
    {
        if (slotId == Guid.Empty)
        {
            throw new ArgumentException("An external binding requires a nonempty SlotId.", nameof(slotId));
        }

        if (target is Entity entity && !entity.IsValid)
        {
            throw new ArgumentException("An external binding target must be a nondefault Entity.", nameof(target));
        }

        SlotId = slotId;
        Target = target;
    }

    /// <summary>Gets the root Scene ExternalSlot identity.</summary>
    public Guid SlotId { get; }

    /// <summary>Gets the target Entity, or null for an explicit null binding.</summary>
    public Entity? Target { get; }
}
