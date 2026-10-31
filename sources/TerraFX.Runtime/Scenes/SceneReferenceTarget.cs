// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal sealed class SceneReferenceTarget
{
    internal SceneReferenceTarget(
        SceneReferenceTargetKind kind,
        SceneEntitySelector? entity,
        Guid? externalSlotId)
    {
        switch (kind)
        {
            case SceneReferenceTargetKind.Internal when (entity is not null) && (externalSlotId is null):
            case SceneReferenceTargetKind.ExternalSlot when (entity is null) && (externalSlotId is not null):
            {
                break;
            }

            default:
            {
                throw new ArgumentException("The reference target kind and operand do not agree.", nameof(kind));
            }
        }

        if (externalSlotId == Guid.Empty)
        {
            throw new ArgumentException("An external-slot identity must be nonempty.", nameof(externalSlotId));
        }

        Kind = kind;
        Entity = entity;
        ExternalSlotId = externalSlotId;
    }

    internal SceneEntitySelector? Entity { get; }

    internal Guid? ExternalSlotId { get; }

    internal SceneReferenceTargetKind Kind { get; }
}
