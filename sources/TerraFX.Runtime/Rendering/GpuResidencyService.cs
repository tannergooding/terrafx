// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

/// <summary>Uploads package-backed Runtime textures to one selected graphics device.</summary>
public sealed class GpuResidencyService
{
    private readonly HashSet<Task> _activeRequests = [];
    private TaskCompletionSource? _drained;
    private readonly Lock _gate = new();
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Dictionary<SpriteResidencyKey, SpriteResidencyCacheEntry> _spritePageResidencies = [];
    private bool _stopping;

    internal GpuResidencyService(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        _graphicsDevice = graphicsDevice;
    }

    /// <summary>Uploads one immutable Runtime Texture asset and returns its package/device ownership lease.</summary>
    /// <typeparam name="T">The typed asset lease value.</typeparam>
    /// <param name="asset">The exact package-backed Texture asset lease.</param>
    /// <param name="options">The admitted sampling and optional copy-source uses.</param>
    /// <param name="cancellationToken">The token that cancels upload admission or observation.</param>
    /// <returns>A lease after every mip upload has completed on the selected device generation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="asset"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="NotSupportedException">The asset is not the Runtime Texture type or its format is unsupported.</exception>
    /// <exception cref="InvalidDataException">A mip does not match the immutable Texture asset's declared extent.</exception>
    /// <exception cref="InvalidOperationException">The device is not ready or an upload did not complete on its selected epoch.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before the operation completed.</exception>
    /// <exception cref="ObjectDisposedException">The service or asset lease has been disposed.</exception>
    public Task<GpuResidencyLease> EnsureGpuResidencyAsync<T>(
        AssetLease<T> asset,
        ResidencyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(options);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<GpuResidencyLease>(cancellationToken);
        }

        TaskCompletionSource<GpuResidencyLease> completion;

        lock (_gate)
        {
            if (_stopping)
            {
                return Task.FromException<GpuResidencyLease>(new ObjectDisposedException(nameof(GpuResidencyService)));
            }

            completion = new TaskCompletionSource<GpuResidencyLease>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = _activeRequests.Add(completion.Task);
        }

        _ = CompleteEnsureAsync(completion, asset, options, cancellationToken);
        return completion.Task;
    }

    internal async Task<GpuResidencyLease> EnsureSpritePageResidencyAsync(
        AssetLease<RenderTextureAsset> asset,
        ResidencyOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (_graphicsDevice.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting Sprite page residency requests.");
        }

        var backingToken = asset.BackingToken;
        var key = new SpriteResidencyKey(backingToken, asset.AssetId.Value, _graphicsDevice.DeviceEpoch);
        TaskCompletionSource<GpuResidencyLease> completion;

        lock (_gate)
        {
            if (_stopping)
            {
                ObjectDisposedException.ThrowIf(_stopping, this);
            }

            if (_spritePageResidencies.TryGetValue(key, out var cachedEntry)
                && (cachedEntry.GetResource(options.Usage) is { } cachedResource))
            {
                return CreateSpritePageLease(key, cachedEntry, cachedResource, asset.AssetId);
            }

            completion = new TaskCompletionSource<GpuResidencyLease>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = _activeRequests.Add(completion.Task);
        }

        _ = CompleteEnsureSpritePageAsync(completion, asset, options, key, cancellationToken);
        return await completion.Task.ConfigureAwait(false);
    }

    internal Task StopAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            _spritePageResidencies.Clear();

            if (_activeRequests.Count == 0)
            {
                return Task.CompletedTask;
            }

            _drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _drained.Task;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The service transfers every operation failure to its caller-owned task before completing host shutdown.")]
    private async Task CompleteEnsureAsync<T>(
        TaskCompletionSource<GpuResidencyLease> completion,
        AssetLease<T> asset,
        ResidencyOptions options,
        CancellationToken cancellationToken)
    {
        GpuResidencyLease? residencyLease = null;

        try
        {
            residencyLease = await CreateGpuResidencyLeaseAsync(
                asset,
                options,
                cancellationToken).ConfigureAwait(false);

            if (completion.TrySetResult(residencyLease))
            {
                residencyLease = null;
            }
            else
            {
                throw new InvalidOperationException("The GPU residency request was already terminal.");
            }
        }
        catch (OperationCanceledException exception)
        {
            _ = completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            _ = completion.TrySetException(exception);
        }
        finally
        {
            try
            {
                residencyLease?.Dispose();
            }
            finally
            {
                FinishRequest(completion.Task);
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The service transfers every shared Sprite residency failure to its caller-owned task before completing host shutdown.")]
    private async Task CompleteEnsureSpritePageAsync(
        TaskCompletionSource<GpuResidencyLease> completion,
        AssetLease<RenderTextureAsset> asset,
        ResidencyOptions options,
        SpriteResidencyKey key,
        CancellationToken cancellationToken)
    {
        GpuResidencyResource? candidateResource = null;
        GpuResidencyLease? uploadedLease = null;
        GpuResidencyLease? residencyLease = null;

        try
        {
            uploadedLease = await CreateGpuResidencyLeaseAsync(
                asset,
                options,
                cancellationToken).ConfigureAwait(false);

            if ((_graphicsDevice.Status != GraphicsDeviceStatus.Ready)
                || (_graphicsDevice.DeviceEpoch != uploadedLease.DeviceEpoch))
            {
                throw new InvalidOperationException("The GraphicsDevice generation changed before Sprite residency publication.");
            }

            key = key with { DeviceEpoch = uploadedLease.DeviceEpoch };
            candidateResource = uploadedLease.TransferResource();
            uploadedLease = null;

            lock (_gate)
            {
                if (_stopping)
                {
                    ObjectDisposedException.ThrowIf(_stopping, this);
                }

                var isNewEntry = !_spritePageResidencies.TryGetValue(key, out var entry);
                entry ??= new SpriteResidencyCacheEntry();
                var selectedResource = entry.GetResource(options.Usage);
                var ownsCandidate = selectedResource is null;
                selectedResource ??= candidateResource
                    ?? throw new InvalidOperationException("The Sprite residency upload omitted its texture resource.");
                residencyLease = CreateSpritePageLease(key, entry, selectedResource, asset.AssetId);

                if (ownsCandidate)
                {
                    entry.SetResource(options.Usage, selectedResource);
                }

                if (isNewEntry)
                {
                    _spritePageResidencies.Add(key, entry);
                }

                if (ownsCandidate)
                {
                    candidateResource = null;
                }
            }

            if (completion.TrySetResult(residencyLease))
            {
                residencyLease = null;
            }
            else
            {
                throw new InvalidOperationException("The Sprite residency request was already terminal.");
            }
        }
        catch (OperationCanceledException exception)
        {
            _ = completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            _ = completion.TrySetException(exception);
        }
        finally
        {
            try
            {
                residencyLease?.Dispose();
            }
            finally
            {
                try
                {
                    uploadedLease?.Dispose();
                    candidateResource?.Dispose();
                }
                finally
                {
                    FinishRequest(completion.Task);
                }
            }
        }
    }

    private async Task<GpuResidencyLease> CreateGpuResidencyLeaseAsync<T>(
        AssetLease<T> asset,
        ResidencyOptions options,
        CancellationToken cancellationToken)
    {
        AssetLease<RenderTextureAsset>? assetLease = null;
        GpuTexture? texture = null;

        try
        {
            if (asset is not AssetLease<RenderTextureAsset> textureAssetLease)
            {
                throw new NotSupportedException("GPU residency currently admits only the Runtime Texture asset type.");
            }

            assetLease = textureAssetLease.Retain();
            var textureAsset = assetLease.Value;
            var deviceEpoch = _graphicsDevice.DeviceEpoch;

            if (_graphicsDevice.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The GraphicsDevice is not accepting texture residency requests.");
            }

            var format = textureAsset.Format switch {
                0 => textureAsset.ColorSpace switch {
                    RenderTextureColorSpace.Linear => TextureFormat.Rgba8Unorm,
                    RenderTextureColorSpace.Srgb => TextureFormat.Rgba8Srgb,
                    _ => throw new NotSupportedException("The Runtime Texture color space is unsupported."),
                },
                _ => throw new NotSupportedException("The Runtime Texture format is unsupported."),
            };
            var usage = options.Usage | TextureUsage.CopyDestination;
            texture = await _graphicsDevice.CreateTextureAsync(
                new TextureDescription(
                    TextureDimension.Dimension2D,
                    textureAsset.Width,
                    textureAsset.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: textureAsset.Mips.Length,
                    format,
                    usage,
                    $"runtime-texture-{asset.AssetId.Value}"),
                cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < textureAsset.Mips.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mip = textureAsset.Mips[index];
                var width = Math.Max(1, textureAsset.Width >> index);
                var height = Math.Max(1, textureAsset.Height >> index);
                var expectedPixelLength = checked(width * height * 4);

                if ((mip.Level != index)
                    || (mip.Width != width)
                    || (mip.Height != height)
                    || (mip.Pixels.Length != expectedPixelLength))
                {
                    throw new InvalidDataException("A Runtime Texture mip does not match its declared level, dimensions, or RGBA8 payload.");
                }

                using var storage = UnmanagedBuffer<byte>.Allocate(
                    expectedPixelLength,
                    new BufferAllocationOptions(expectedPixelLength, alignment: 1));
                storage.CopyFrom(mip.Pixels.AsSpan(), destinationOffset: 0);
                using var ownedStorage = storage.TransferToOwnedBuffer();
                using var source = ownedStorage.AcquireReadLease(0, expectedPixelLength);
                var rowPitch = checked((nint)width * 4);
                var upload = await _graphicsDevice.UploadTextureAsync(
                    texture,
                    new TextureSubresource(index, arrayLayer: 0, TextureAspect.Color),
                    source,
                    sourceOffset: 0,
                    new TextureDataLayout(rowPitch, checked(rowPitch * height)),
                    cancellationToken).ConfigureAwait(false);

                if ((upload.Status != UploadStatus.Ready) || (upload.DeviceEpoch != deviceEpoch))
                {
                    throw new InvalidOperationException("A Runtime Texture mip upload did not complete on the selected device epoch.");
                }
            }

            if ((_graphicsDevice.Status != GraphicsDeviceStatus.Ready)
                || (_graphicsDevice.DeviceEpoch != deviceEpoch)
                || (texture.DeviceEpoch != deviceEpoch))
            {
                throw new InvalidOperationException("The GraphicsDevice generation changed before Runtime Texture residency completed.");
            }

            var resource = new GpuResidencyResource(texture, assetLease);
            var residencyLease = new GpuResidencyLease(asset.AssetId, deviceEpoch, resource, resource);
            texture = null;
            assetLease = null;
            return residencyLease;
        }
        finally
        {
            try
            {
                texture?.Dispose();
            }
            finally
            {
                assetLease?.Dispose();
            }
        }
    }

    private GpuResidencyLease CreateSpritePageLease(
        SpriteResidencyKey key,
        SpriteResidencyCacheEntry entry,
        GpuResidencyResource resource,
        AssetId assetId)
    {
        if (entry.LeaseCount == int.MaxValue)
        {
            throw new InvalidOperationException("The Sprite residency has too many live leases.");
        }

        var resourceLease = resource.Retain();

        try
        {
            var spriteLease = new SpriteResidencyLease(this, key, entry, resourceLease);
            var residencyLease = new GpuResidencyLease(
                assetId,
                key.DeviceEpoch,
                resource,
                spriteLease);
            entry.LeaseCount++;
            return residencyLease;
        }
        catch
        {
            resourceLease.Dispose();
            throw;
        }
    }

    private void FinishRequest(Task request)
    {
        lock (_gate)
        {
            if (!_activeRequests.Remove(request))
            {
                throw new InvalidOperationException("The completed GPU residency request was not admitted by this service.");
            }

            if (_stopping && (_activeRequests.Count == 0))
            {
                _ = _drained?.TrySetResult();
            }
        }
    }

    private void ReleaseSpritePageLease(SpriteResidencyKey key, SpriteResidencyCacheEntry entry)
    {
        GpuResidencyResource? canonicalResource;
        GpuResidencyResource? copySourceResource;

        lock (_gate)
        {
            if (entry.LeaseCount <= 0)
            {
                throw new InvalidOperationException("The Sprite residency lease count became negative.");
            }

            entry.LeaseCount--;

            if (entry.LeaseCount != 0)
            {
                return;
            }

            if (_spritePageResidencies.TryGetValue(key, out var cachedEntry)
                && ReferenceEquals(cachedEntry, entry))
            {
                _ = _spritePageResidencies.Remove(key);
            }

            (canonicalResource, copySourceResource) = entry.DetachResources();
        }

        try
        {
            canonicalResource?.Dispose();
        }
        finally
        {
            if (!ReferenceEquals(canonicalResource, copySourceResource))
            {
                copySourceResource?.Dispose();
            }
        }
    }

    private sealed class SpriteResidencyCacheEntry
    {
        private GpuResidencyResource? _canonicalResource;
        private GpuResidencyResource? _copySourceResource;

        internal int LeaseCount { get; set; }

        internal (GpuResidencyResource? CanonicalResource, GpuResidencyResource? CopySourceResource) DetachResources()
        {
            var canonicalResource = _canonicalResource;
            var copySourceResource = _copySourceResource;
            _canonicalResource = null;
            _copySourceResource = null;
            return (canonicalResource, copySourceResource);
        }

        internal GpuResidencyResource? GetResource(TextureUsage usage)
            => ((usage & TextureUsage.CopySource) != 0)
                ? _copySourceResource
                : _canonicalResource;

        internal void SetResource(TextureUsage usage, GpuResidencyResource resource)
        {
            ArgumentNullException.ThrowIfNull(resource);

            if ((usage & TextureUsage.CopySource) != 0)
            {
                if (_copySourceResource is not null)
                {
                    throw new InvalidOperationException("The Sprite residency already owns a CopySource texture variant.");
                }

                _copySourceResource = resource;
            }

            _canonicalResource ??= resource;
        }
    }

    private readonly record struct SpriteResidencyKey(
        AssetPackageBackingToken BackingToken,
        int AssetId,
        uint DeviceEpoch);

    private sealed class SpriteResidencyLease : IDisposable
    {
        private readonly SpriteResidencyCacheEntry _entry;
        private readonly SpriteResidencyKey _key;
        private GpuResidencyResourceLease? _resourceLease;
        private readonly GpuResidencyService _service;

        internal SpriteResidencyLease(
            GpuResidencyService service,
            SpriteResidencyKey key,
            SpriteResidencyCacheEntry entry,
            GpuResidencyResourceLease resourceLease)
        {
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(resourceLease);
            _service = service;
            _key = key;
            _entry = entry;
            _resourceLease = resourceLease;
        }

        public void Dispose()
        {
            var resourceLease = Interlocked.Exchange(ref _resourceLease, null);

            if (resourceLease is null)
            {
                return;
            }

            try
            {
                resourceLease.Dispose();
            }
            finally
            {
                _service.ReleaseSpritePageLease(_key, _entry);
            }
        }
    }
}
