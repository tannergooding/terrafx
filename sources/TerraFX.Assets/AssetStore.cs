// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

/// <summary>Loads immutable typed assets from one selected content-set package.</summary>
public sealed partial class AssetStore : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private long _activeDecodeBytes;
    private AssetPackageBacking? _currentBacking;
    private bool _disposed;
    private long _liveAssetBytes;
    private int _liveAssetLeases;

    private AssetStore(AssetPackageBacking backing)
    {
        _currentBacking = backing;
    }

    /// <summary>Opens and validates one immutable EPKG2 content-set package.</summary>
    /// <param name="options">The trusted package selection and budgets.</param>
    /// <param name="cancellationToken">The token that cancels package admission.</param>
    /// <returns>The initialized asset store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidDataException">The package fails digest, profile, or EPKG2 validation.</exception>
    public static async Task<AssetStore> CreateAsync(
        AssetStoreOptions options,
        CancellationToken cancellationToken = default)
    {
        var backing = await AssetPackageReader.OpenAsync(options, cancellationToken).ConfigureAwait(false);

        try
        {
            return new AssetStore(backing);
        }
        catch
        {
            backing.ReleaseOwner();
            throw;
        }
    }

    private static AssetLease<T> CreateLease<T>(
        AssetStore store,
        AssetId assetId,
        T value,
        AssetPackageBacking backing,
        long decodedBytes,
        out bool ownershipTransferred)
    {
        ownershipTransferred = false;
        var lifetime = new AssetLeaseLifetime(store, backing, decodedBytes);

        try
        {
            var lease = new AssetLease<T>(assetId, value, lifetime);
            ownershipTransferred = true;
            return lease;
        }
        catch
        {
            try
            {
                lifetime.ReleaseReference();
            }
            finally
            {
                ownershipTransferred = true;
            }

            throw;
        }
    }

    /// <summary>Disposes the store's package ownership while active leases retain their backing.</summary>
    public ValueTask DisposeAsync()
    {
        AssetPackageBacking? backing;

        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            backing = _currentBacking;
            _currentBacking = null;
        }

        backing?.ReleaseOwner();
        return ValueTask.CompletedTask;
    }

    /// <summary>Loads one typed asset from the currently selected content set.</summary>
    /// <typeparam name="T">The registered immutable asset value type.</typeparam>
    /// <param name="assetId">The positive content-set asset identity.</param>
    /// <param name="cancellationToken">The token that cancels the admitted read/decode operation.</param>
    /// <returns>A lease that retains the decoded value and package backing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="assetId"/> is invalid.</exception>
    /// <exception cref="KeyNotFoundException"><paramref name="assetId"/> is absent from the selected content set.</exception>
    /// <exception cref="InvalidDataException">The indexed envelope or typed record is malformed.</exception>
    /// <exception cref="InvalidOperationException">A live-lease or decode budget is exhausted.</exception>
    /// <exception cref="NotSupportedException">The CLR type or payload schema has no exact compiled codec.</exception>
    /// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
    public async Task<AssetLease<T>> LoadAsync<T>(
        AssetId assetId,
        CancellationToken cancellationToken = default)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        AcquireLoad<T>(
            assetId,
            packageBacking: null,
            backingReferenceAlreadyHeld: false,
            out var backing,
            out var entry,
            out var codec,
            out var decodedBytes);
        return await LoadAssetAsync(assetId, backing, entry, codec, decodedBytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates and atomically selects a replacement content-set package.</summary>
    /// <param name="options">The trusted replacement package selection and budgets.</param>
    /// <param name="cancellationToken">The token that cancels admission before publication.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidDataException">The candidate package fails admission.</exception>
    /// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
    public async Task ReplacePackageAsync(
        AssetStoreOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var replacement = await AssetPackageReader.OpenAsync(options, cancellationToken).ConfigureAwait(false);
        AssetPackageBacking? previous;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                previous = _currentBacking
                    ?? throw new InvalidOperationException("The asset store has no selected package.");
                _currentBacking = replacement;
            }
        }
        catch
        {
            replacement.ReleaseOwner();
            throw;
        }

        previous.ReleaseOwner();
    }

    internal void ReleaseLeaseReference(long decodedBytes, bool releaseDecodedBytes)
    {
        lock (_gate)
        {
            if ((_liveAssetLeases <= 0)
                || (releaseDecodedBytes && (decodedBytes > _liveAssetBytes)))
            {
                throw new InvalidOperationException("The asset-store lease accounting is inconsistent.");
            }

            _liveAssetLeases--;

            if (releaseDecodedBytes)
            {
                _liveAssetBytes -= decodedBytes;
            }
        }
    }

    internal void ReserveRetainedLease()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var backing = _currentBacking
                ?? throw new InvalidOperationException("The asset store has no selected package.");

            if (_liveAssetLeases >= backing.Options.MaximumLiveAssetLeases)
            {
                throw new InvalidOperationException("The live asset lease budget is exhausted.");
            }

            _liveAssetLeases++;
        }
    }

    private void AcquireLoad<T>(
        AssetId assetId,
        AssetPackageBacking? packageBacking,
        bool backingReferenceAlreadyHeld,
        out AssetPackageBacking backing,
        out AssetPackageEntry entry,
        out AssetStoreRegistration.IAssetCodec<T> codec,
        out long decodedBytes)
    {
        if (backingReferenceAlreadyHeld && (packageBacking is null))
        {
            throw new ArgumentNullException(nameof(packageBacking));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            backing = packageBacking ?? _currentBacking
                ?? throw new InvalidOperationException("The asset store has no selected package.");

            if (!backing.TryGetEntry(assetId, out entry))
            {
                throw new KeyNotFoundException($"Asset {assetId} is absent from the selected content set.");
            }

            if (!backing.Registration.TryGetCodec<T>(out var registeredCodec))
            {
                throw new NotSupportedException($"No asset codec is registered for {typeof(T)}.");
            }

            codec = registeredCodec;
            decodedBytes = checked((long)entry.DecodedLength);
            var options = backing.Options;

            if ((decodedBytes > options.CpuAssetBudgetBytes)
                || (_liveAssetBytes > options.CpuAssetBudgetBytes - decodedBytes)
                || (_liveAssetLeases >= options.MaximumLiveAssetLeases))
            {
                throw new InvalidOperationException("The live asset lease budget is exhausted.");
            }

            if ((decodedBytes > options.MaximumConcurrentDecodeBytes)
                || (_activeDecodeBytes > options.MaximumConcurrentDecodeBytes - decodedBytes))
            {
                throw new InvalidOperationException("The concurrent asset decode budget is exhausted.");
            }

            if (!backingReferenceAlreadyHeld)
            {
                ObjectDisposedException.ThrowIf(!backing.TryAcquireReference(), backing);
            }

            _liveAssetBytes += decodedBytes;
            _liveAssetLeases++;
            _activeDecodeBytes += decodedBytes;
        }
    }

    private void CompleteLoad(long decodedBytes, bool leaseCreated)
    {
        lock (_gate)
        {
            if (!leaseCreated)
            {
                if ((_liveAssetLeases <= 0) || (decodedBytes > _liveAssetBytes))
                {
                    throw new InvalidOperationException("The asset-store lease accounting is inconsistent.");
                }

                _liveAssetLeases--;
                _liveAssetBytes -= decodedBytes;
            }
        }
    }

    private void ReleaseDecodeBudget(long decodedBytes)
    {
        lock (_gate)
        {
            if (decodedBytes > _activeDecodeBytes)
            {
                throw new InvalidOperationException("The asset-store decode accounting is inconsistent.");
            }

            _activeDecodeBytes -= decodedBytes;
        }
    }

    private void ReserveDecodeBudget(AssetPackageBacking backing, long decodedBytes)
    {
        lock (_gate)
        {
            var maximumDecodeBytes = (_currentBacking ?? backing).Options.MaximumConcurrentDecodeBytes;

            if ((decodedBytes > maximumDecodeBytes)
                || (_activeDecodeBytes > maximumDecodeBytes - decodedBytes))
            {
                throw new InvalidOperationException("The concurrent asset decode budget is exhausted.");
            }

            _activeDecodeBytes += decodedBytes;
        }
    }

    private async Task ValidateAssetReferencesAsync(
        AssetPackageBacking backing,
        ImmutableArray<AssetReference> assetReferences,
        CancellationToken cancellationToken)
    {
        foreach (var reference in assetReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!backing.TryGetEntry(reference.AssetId, out var targetEntry))
            {
                throw new InvalidDataException(
                    $"Asset {reference.AssetId} referenced by the loaded record is absent from the content set.");
            }

            var targetDecodedBytes = checked((long)targetEntry.DecodedLength);
            ReserveDecodeBudget(backing, targetDecodedBytes);

            try
            {
                var targetEnvelope = await AssetPayloadReader.ReadAsync(
                    backing,
                    targetEntry,
                    cancellationToken).ConfigureAwait(false);
                var status = AssetEnvelopeDecoder.Inspect(
                    targetEnvelope,
                    reference.AssetId,
                    backing.Registration.Schemas,
                    backing.ReadLimits,
                    out var actualSchema,
                    out var diagnostic);
                cancellationToken.ThrowIfCancellationRequested();

                if (status is BinaryRecordStatus.UnsupportedSchema)
                {
                    throw new NotSupportedException(
                        $"Referenced asset {reference.AssetId} has no supported schema: {diagnostic ?? status.ToString()}.");
                }

                if (status is not BinaryRecordStatus.Complete)
                {
                    throw new InvalidDataException(
                        $"Referenced asset {reference.AssetId} was not admitted: {diagnostic ?? status.ToString()}.");
                }

                if ((actualSchema.SchemaId != reference.SchemaId)
                    || (actualSchema.Major != reference.SchemaMajor))
                {
                    throw new InvalidDataException(
                        $"Referenced asset {reference.AssetId} does not match its declared schema identity and major version.");
                }

                if (!backing.Registration.TryGetCodec(actualSchema, out _))
                {
                    throw new NotSupportedException(
                        $"Referenced asset {reference.AssetId} has no codec for its exact schema tuple.");
                }
            }
            finally
            {
                ReleaseDecodeBudget(targetDecodedBytes);
            }
        }
    }
}
