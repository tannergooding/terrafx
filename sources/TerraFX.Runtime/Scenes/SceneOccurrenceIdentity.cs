// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal static class SceneOccurrenceIdentity
{
    private static readonly Guid s_rootNamespace = new("2ed8b750-e1a0-4b46-9000-000000000001");

    internal static Guid CreateExpandedEntityKey(Guid occurrence, Guid authoredEntityId)
    {
        if (occurrence == Guid.Empty)
        {
            throw new ArgumentException("A nonempty occurrence identity is required.", nameof(occurrence));
        }

        if (authoredEntityId == Guid.Empty)
        {
            throw new ArgumentException("A nonempty authored Entity identity is required.", nameof(authoredEntityId));
        }

        return FoundationNameUuid.Create(occurrence, "scene/entity/v1/" + authoredEntityId.ToString("N"));
    }

    internal static Guid CreatePrefabOccurrence(Guid parentOccurrence, Guid placementId, AssetId targetAssetId)
    {
        if (parentOccurrence == Guid.Empty)
        {
            throw new ArgumentException("A nonempty parent occurrence is required.", nameof(parentOccurrence));
        }

        if (placementId == Guid.Empty)
        {
            throw new ArgumentException("A nonempty placement identity is required.", nameof(placementId));
        }

        if (!targetAssetId.IsValid)
        {
            throw new ArgumentException("A valid prefab AssetId is required.", nameof(targetAssetId));
        }

        var name = string.Concat(
            "scene/occurrence/prefab/v1/",
            placementId.ToString("N"),
            "/",
            targetAssetId);
        return FoundationNameUuid.Create(parentOccurrence, name);
    }

    internal static Guid CreateRootOccurrence(AssetId rootAssetId)
    {
        if (!rootAssetId.IsValid)
        {
            throw new ArgumentException("A valid root Scene AssetId is required.", nameof(rootAssetId));
        }

        return FoundationNameUuid.Create(s_rootNamespace, "scene/occurrence/root/v1/" + rootAssetId);
    }
}
