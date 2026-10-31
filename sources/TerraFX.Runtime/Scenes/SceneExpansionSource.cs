// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
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
}
