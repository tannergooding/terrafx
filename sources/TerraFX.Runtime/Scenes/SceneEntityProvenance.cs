// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Identifies the authored source and World identity of one live Scene Entity.</summary>
public sealed class SceneEntityProvenance
{
    internal SceneEntityProvenance(
        Guid sceneInstanceId,
        Guid occurrenceId,
        AssetId sourceAssetId,
        Guid authoredEntityId,
        Guid expandedEntityKey,
        ImmutableArray<Guid> placementPath,
        WorldEntityIdentity worldIdentity)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(occurrenceId, Guid.Empty);

        if (!sourceAssetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAssetId));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(authoredEntityId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(expandedEntityKey, Guid.Empty);

        if (placementPath.IsDefault || (placementPath.Length > 32))
        {
            throw new ArgumentException("The provenance placement path must be initialized and bounded.", nameof(placementPath));
        }

        if (!worldIdentity.IsValid)
        {
            throw new ArgumentException("The provenance requires an exact qualified World identity.", nameof(worldIdentity));
        }

        SceneInstanceId = sceneInstanceId;
        OccurrenceId = occurrenceId;
        SourceAssetId = sourceAssetId;
        AuthoredEntityId = authoredEntityId;
        ExpandedEntityKey = expandedEntityKey;
        PlacementPath = [.. placementPath];
        WorldIdentity = worldIdentity;
    }

    /// <summary>Gets the authored Entity identity.</summary>
    public Guid AuthoredEntityId { get; }

    /// <summary>Gets the expanded Scene key derived from its occurrence and authored identity.</summary>
    public Guid ExpandedEntityKey { get; }

    /// <summary>Gets the exact occurrence identity within the Scene instance.</summary>
    public Guid OccurrenceId { get; }

    /// <summary>Gets the copied root-relative placement path.</summary>
    public ImmutableArray<Guid> PlacementPath { get; }

    /// <summary>Gets the stable identity of the containing Scene instance.</summary>
    public Guid SceneInstanceId { get; }

    /// <summary>Gets the selected source Scene or Prefab asset identity.</summary>
    public AssetId SourceAssetId { get; }

    /// <summary>Gets the World-, lineage-, and branch-qualified live Entity identity.</summary>
    public WorldEntityIdentity WorldIdentity { get; }
}
