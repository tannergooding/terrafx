// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class RenderSnapshotAssetPins : IDisposable
{
    private int _isDisposed;
    private readonly SharedState _sharedState;

    internal RenderSnapshotAssetPins(ImmutableArray<SceneAssetPins> scenePins)
    {
        if (scenePins.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Snapshot asset pins require at least one Scene bundle.", nameof(scenePins));
        }

        for (var index = 0; index < scenePins.Length; index++)
        {
            var scenePin = scenePins[index]
                ?? throw new ArgumentException("Snapshot asset pins cannot contain null Scene bundles.", nameof(scenePins));

            if ((index > 0)
                && (FoundationCatalogue.CompareRfc(scenePins[index - 1].SceneInstanceId, scenePin.SceneInstanceId) >= 0))
            {
                throw new ArgumentException("Snapshot Scene asset pins must be uniquely RFC-sorted.", nameof(scenePins));
            }
        }

        _sharedState = new SharedState(scenePins);
    }

    private RenderSnapshotAssetPins(SharedState sharedState)
    {
        _sharedState = sharedState;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _sharedState.Release();
        }
    }

    internal SceneAssetPins GetSceneAssets(Guid sceneInstanceId)
    {
        ThrowIfDisposed();
        return _sharedState.GetSceneAssets(sceneInstanceId);
    }

    internal RenderSnapshotAssetPins Retain()
    {
        ThrowIfDisposed();
        _sharedState.Retain();

        try
        {
            return new RenderSnapshotAssetPins(_sharedState);
        }
        catch
        {
            _sharedState.Release();
            throw;
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

    internal sealed class SceneAssetPins : IDisposable
    {
        private readonly ImmutableArray<AtlasPin> _atlasPages;
        private int _isDisposed;
        private readonly ImmutableArray<MaterialPin> _materials;

        internal SceneAssetPins(
            Guid sceneInstanceId,
            ImmutableArray<AtlasPin> atlasPages,
            ImmutableArray<MaterialPin> materials)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);

            if (atlasPages.IsDefaultOrEmpty || materials.IsDefault)
            {
                throw new ArgumentException("Scene Sprite snapshot asset arrays must be initialized.");
            }

            SceneInstanceId = sceneInstanceId;
            _atlasPages = atlasPages;
            _materials = materials;
        }

        internal Guid SceneInstanceId { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            foreach (var atlasPage in _atlasPages)
            {
                atlasPage.PageSet.Dispose();
            }

            foreach (var material in _materials)
            {
                material.Lease.Dispose();
            }
        }

        internal RenderAtlasPageSet GetAtlasPageSet(int assetId)
        {
            ThrowIfDisposed();
            var low = 0;
            var high = _atlasPages.Length - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = _atlasPages[middle].AssetId.CompareTo(assetId);

                if (comparison == 0)
                {
                    return _atlasPages[middle].PageSet;
                }

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            throw new ArgumentException("The Atlas is not retained by this Sprite snapshot.", nameof(assetId));
        }

        internal RenderMaterialAsset GetMaterial(int assetId)
        {
            ThrowIfDisposed();
            var low = 0;
            var high = _materials.Length - 1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = _materials[middle].AssetId.CompareTo(assetId);

                if (comparison == 0)
                {
                    return _materials[middle].Lease.Value;
                }

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            throw new ArgumentException("The Material is not retained by this Sprite snapshot.", nameof(assetId));
        }

        private void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        internal readonly record struct AtlasPin(int AssetId, RenderAtlasPageSet PageSet);

        internal readonly record struct MaterialPin(int AssetId, AssetLease<RenderMaterialAsset> Lease);
    }

    private sealed class SharedState
    {
        private bool _isDisposed;
        private int _referenceCount;
        private readonly ImmutableArray<SceneAssetPins> _scenePins;
        private readonly Lock _sync;

        internal SharedState(ImmutableArray<SceneAssetPins> scenePins)
        {
            _scenePins = scenePins;
            _referenceCount = 1;
            _sync = new();
        }

        internal SceneAssetPins GetSceneAssets(Guid sceneInstanceId)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var low = 0;
                var high = _scenePins.Length - 1;

                while (low <= high)
                {
                    var middle = low + ((high - low) / 2);
                    var comparison = FoundationCatalogue.CompareRfc(_scenePins[middle].SceneInstanceId, sceneInstanceId);

                    if (comparison == 0)
                    {
                        return _scenePins[middle];
                    }

                    if (comparison < 0)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle - 1;
                    }
                }

                throw new ArgumentException("The Scene instance is not retained by this render snapshot.", nameof(sceneInstanceId));
            }
        }

        internal void Release()
        {
            ImmutableArray<SceneAssetPins> scenePins = default;

            lock (_sync)
            {
                if (_referenceCount <= 0)
                {
                    throw new InvalidOperationException("Snapshot asset-pin ownership was released more than once.");
                }

                _referenceCount--;

                if (_referenceCount == 0)
                {
                    _isDisposed = true;
                    scenePins = _scenePins;
                }
            }

            if (!scenePins.IsDefault)
            {
                foreach (var scenePin in scenePins)
                {
                    scenePin.Dispose();
                }
            }
        }

        internal void Retain()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _referenceCount = checked(_referenceCount + 1);
            }
        }

        private void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
