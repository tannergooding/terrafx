// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Owns one bounded Scene preparation for its exact target World and AssetStore.</summary>
public sealed class PreparedScene : IDisposable
{
    private bool _activationAttempted;
    private readonly AssetId _assetId;
    private readonly AssetStore _assets;
    private readonly Lock _gate = new();
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
    {
        SceneExpansionPlan? plan;

        lock (_gate)
        {
            plan = _plan;
            _plan = null;
        }

        plan?.Dispose();
    }

    internal PreparedAssetsLifetime.Lease? AcquirePreparedAssets()
    {
        lock (_gate)
        {
            if (_activationAttempted)
            {
                throw new InvalidOperationException("Prepared assets cannot be acquired after activation admission.");
            }

            var plan = _plan
                ?? throw new ObjectDisposedException(nameof(PreparedScene));
            return plan.AcquirePreparedAssets();
        }
    }

    internal SceneExpansionPlan DetachPlan(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!ReferenceEquals(_world, world))
        {
            throw new ArgumentException("The prepared Scene is bound to another exact World.", nameof(world));
        }

        lock (_gate)
        {
            if (_activationAttempted)
            {
                throw new InvalidOperationException("A prepared Scene can be admitted for activation only once.");
            }

            _activationAttempted = true;
            var plan = _plan
                ?? throw new ObjectDisposedException(nameof(PreparedScene));
            _plan = null;
            return plan;
        }
    }
}
