// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal sealed class ScenePrefabPlacement
{
    internal ScenePrefabPlacement(
        Guid placementId,
        AssetId prefabAssetId,
        Guid? parentEntityId,
        ImmutableArray<ScenePrefabRootPose> rootPoses,
        ImmutableArray<ScenePrefabOverride> overrides,
        ImmutableArray<ScenePlacementSlotBinding> slotBindings,
        ImmutableArray<byte> encodedRecord)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(placementId, Guid.Empty);

        if (!prefabAssetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(prefabAssetId));
        }

        if (parentEntityId == Guid.Empty)
        {
            throw new ArgumentException("A placement parent identity must be nonempty when present.", nameof(parentEntityId));
        }

        if (rootPoses.IsDefault)
        {
            throw new ArgumentException("The Prefab root-pose array must be initialized.", nameof(rootPoses));
        }

        if (overrides.IsDefault)
        {
            throw new ArgumentException("The Prefab override array must be initialized.", nameof(overrides));
        }

        if (slotBindings.IsDefault)
        {
            throw new ArgumentException("The Prefab slot-binding array must be initialized.", nameof(slotBindings));
        }

        if (encodedRecord.IsDefaultOrEmpty)
        {
            throw new ArgumentException("The placement record bytes must be initialized and nonempty.", nameof(encodedRecord));
        }

        PlacementId = placementId;
        PrefabAssetId = prefabAssetId;
        ParentEntityId = parentEntityId;
        RootPoses = rootPoses;
        Overrides = overrides;
        SlotBindings = slotBindings;
        EncodedRecord = encodedRecord;
    }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal ImmutableArray<ScenePrefabOverride> Overrides { get; }

    internal Guid? ParentEntityId { get; }

    internal Guid PlacementId { get; }

    internal AssetId PrefabAssetId { get; }

    internal ImmutableArray<ScenePrefabRootPose> RootPoses { get; }

    internal ImmutableArray<ScenePlacementSlotBinding> SlotBindings { get; }
}
