// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal readonly struct SceneExpansionSource
{
    private readonly ScenePrefabAsset? _prefab;
    private readonly SceneAsset? _scene;

    internal SceneExpansionSource(SceneAsset scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
        _prefab = null;
    }

    internal SceneExpansionSource(ScenePrefabAsset prefab)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        _scene = null;
        _prefab = prefab;
    }

    internal AssetId AssetId
    {
        get
        {
            if (_scene is SceneAsset scene)
            {
                return scene.AssetId;
            }

            return (_prefab ?? throw new InvalidOperationException("The Scene expansion source is uninitialized.")).AssetId;
        }
    }

    internal ImmutableArray<SceneEntity> Entities
    {
        get
        {
            if (_scene is SceneAsset scene)
            {
                return scene.Entities;
            }

            return (_prefab ?? throw new InvalidOperationException("The Scene expansion source is uninitialized.")).Entities;
        }
    }

    internal ImmutableArray<SceneExternalSlot> ExternalSlots
    {
        get
        {
            if (_scene is SceneAsset scene)
            {
                return scene.ExternalSlots;
            }

            return (_prefab ?? throw new InvalidOperationException("The Scene expansion source is uninitialized.")).ExternalSlots;
        }
    }

    internal SceneHierarchy Hierarchy
    {
        get
        {
            if (_scene is SceneAsset scene)
            {
                return scene.Hierarchy;
            }

            return (_prefab ?? throw new InvalidOperationException("The Scene expansion source is uninitialized.")).Hierarchy;
        }
    }

    internal bool IsPrefab => _prefab is not null;

    internal ImmutableArray<ScenePrefabPlacement> Placements
    {
        get
        {
            if (_scene is SceneAsset scene)
            {
                return scene.Placements;
            }

            return (_prefab ?? throw new InvalidOperationException("The Scene expansion source is uninitialized.")).Placements;
        }
    }

    internal EbinReadLimits ReadLimits
    {
        get
        {
            if (_scene is SceneAsset scene)
            {
                return scene.ReadLimits;
            }

            return (_prefab ?? throw new InvalidOperationException("The Scene expansion source is uninitialized.")).ReadLimits;
        }
    }

    internal ImmutableArray<SceneEntityReferenceBinding> References
    {
        get
        {
            if (_scene is SceneAsset scene)
            {
                return scene.References;
            }

            return (_prefab ?? throw new InvalidOperationException("The Scene expansion source is uninitialized.")).References;
        }
    }

    internal CompiledContentSchemaRegistry Schemas
    {
        get
        {
            if (_scene is SceneAsset scene)
            {
                return scene.Schemas;
            }

            return (_prefab ?? throw new InvalidOperationException("The Scene expansion source is uninitialized.")).Schemas;
        }
    }
}
