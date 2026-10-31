// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Owns one bounded Scene preparation for its exact target World and AssetStore.</summary>
public sealed class PreparedScene : IDisposable
{
    private readonly AssetId _assetId;
    private readonly AssetStore _assets;
    private readonly SceneLoadOptionsSnapshot _options;
    private SceneExpansionPlan? _plan;
    private readonly World _world;

    internal PreparedScene(
        World world,
        AssetStore assets,
        SceneLoadOptionsSnapshot options,
        SceneExpansionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(plan);
        _world = world;
        _assets = assets;
        _options = options;
        _plan = plan;
        _assetId = plan.SceneAssetId;
    }

    /// <summary>Gets the prepared root Scene asset identity.</summary>
    public AssetId AssetId => _assetId;

    internal AssetStore Assets => _assets;

    internal SceneLoadOptionsSnapshot Options => _options;

    /// <summary>Releases the prepared Scene's package-backed asset leases.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _plan, null)?.Dispose();

    internal SceneExpansionPlan DetachPlan(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!ReferenceEquals(_world, world))
        {
            throw new ArgumentException("The prepared Scene is bound to another exact World.", nameof(world));
        }

        return Interlocked.Exchange(ref _plan, null)
            ?? throw new ObjectDisposedException(nameof(PreparedScene));
    }
}
