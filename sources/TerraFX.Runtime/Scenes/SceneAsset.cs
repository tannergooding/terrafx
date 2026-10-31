// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal sealed class SceneAsset
{
    internal SceneAsset(
        AssetId assetId,
        ImmutableArray<SceneEntity> entities,
        SceneHierarchy hierarchy,
        ImmutableArray<ScenePrefabPlacement> placements,
        ImmutableArray<SceneAuthoredRecord> references,
        ImmutableArray<SceneAuthoredRecord> overrides,
        ImmutableArray<SceneAuthoredRecord> externalSlots)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        if (entities.IsDefault)
        {
            throw new ArgumentException("The Scene entity array must be initialized.", nameof(entities));
        }

        ArgumentNullException.ThrowIfNull(hierarchy);

        if (placements.IsDefault)
        {
            throw new ArgumentException("The Scene placement array must be initialized.", nameof(placements));
        }

        if (references.IsDefault)
        {
            throw new ArgumentException("The Scene reference array must be initialized.", nameof(references));
        }

        if (overrides.IsDefault)
        {
            throw new ArgumentException("The Scene override array must be initialized.", nameof(overrides));
        }

        if (externalSlots.IsDefault)
        {
            throw new ArgumentException("The Scene external-slot array must be initialized.", nameof(externalSlots));
        }

        AssetId = assetId;
        Entities = entities;
        Hierarchy = hierarchy;
        Placements = placements;
        References = references;
        Overrides = overrides;
        ExternalSlots = externalSlots;
    }

    internal AssetId AssetId { get; }

    internal ImmutableArray<SceneEntity> Entities { get; }

    internal ImmutableArray<SceneAuthoredRecord> ExternalSlots { get; }

    internal SceneHierarchy Hierarchy { get; }

    internal ImmutableArray<SceneAuthoredRecord> Overrides { get; }

    internal ImmutableArray<ScenePrefabPlacement> Placements { get; }

    internal ImmutableArray<SceneAuthoredRecord> References { get; }
}
