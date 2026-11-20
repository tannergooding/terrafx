// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Scenes;

internal sealed class SceneExternalSlot
{
    internal SceneExternalSlot(
        Guid slotId,
        bool required,
        bool allowNull,
        ImmutableArray<ContentSchemaReference> requiredComponents)
        : this(slotId, required, allowNull, requiredComponents, copyRequiredComponents: true)
    {
    }

    private SceneExternalSlot(
        Guid slotId,
        bool required,
        bool allowNull,
        ImmutableArray<ContentSchemaReference> requiredComponents,
        bool copyRequiredComponents)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(slotId, Guid.Empty);

        if (required && allowNull)
        {
            throw new ArgumentException("A required external slot cannot allow a null binding.", nameof(allowNull));
        }

        if (requiredComponents.IsDefault)
        {
            throw new ArgumentException("The external-slot RequiredComponents array must be initialized.", nameof(requiredComponents));
        }

        SlotId = slotId;
        Required = required;
        AllowNull = allowNull;
        RequiredComponents = copyRequiredComponents ? [.. requiredComponents] : requiredComponents;
    }

    internal bool AllowNull { get; }

    internal bool Required { get; }

    internal ImmutableArray<ContentSchemaReference> RequiredComponents { get; }

    internal Guid SlotId { get; }

    internal static SceneExternalSlot CreateOwned(
        Guid slotId,
        bool required,
        bool allowNull,
        ImmutableArray<ContentSchemaReference> requiredComponents)
        => new(slotId, required, allowNull, requiredComponents, copyRequiredComponents: false);
}
