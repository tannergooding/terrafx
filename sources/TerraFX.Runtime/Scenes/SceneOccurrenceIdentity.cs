// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal static class SceneOccurrenceIdentity
{
    private static readonly IComparer<Guid> s_identityComparer = new GuidIdentityComparer();
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

    internal static void SortExpandedEntityKeysForAssignment(Span<Guid> expandedEntityKeys)
        => SortUnique(expandedEntityKeys, nameof(expandedEntityKeys), "expanded Entity key");

    internal static void SortOccurrenceIdsAndValidateUnique(Span<Guid> occurrenceIds)
        => SortUnique(occurrenceIds, nameof(occurrenceIds), "Scene occurrence identity");

    private static void SortUnique(Span<Guid> identities, string parameterName, string identityDescription)
    {
        for (var index = 0; index < identities.Length; index++)
        {
            if (identities[index] == Guid.Empty)
            {
                throw new ArgumentException($"A {identityDescription} must be nonempty.", parameterName);
            }
        }

        identities.Sort(s_identityComparer);

        for (var index = 1; index < identities.Length; index++)
        {
            if (identities[index - 1] == identities[index])
            {
                throw new ArgumentException($"The {identityDescription} set contains a duplicate.", parameterName);
            }
        }
    }

    private sealed class GuidIdentityComparer : IComparer<Guid>
    {
        public int Compare(Guid left, Guid right)
            => FoundationCatalogue.CompareRfc(left, right);
    }
}
