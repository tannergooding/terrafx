// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

internal sealed class AssetPackageBacking
{
    private readonly AssetPackageBackingToken _backingToken = new();
    private readonly Lock _gate = new();
    private bool _ownerReleased;
    private readonly EbinReadLimits _readLimits;
    private Exception? _releaseFailure;
    private int _releaseState;
    private FileStream? _stream;
    private int _referenceCount = 1;

    internal AssetPackageBacking(
        FileStream stream,
        ImmutableArray<AssetPackageEntry> entries,
        SpatialProfileId profileId,
        SpatialSettings spatialSettings,
        AssetStoreOptions options,
        long packageByteLength)
    {
        _stream = stream;
        Entries = entries;
        PackageByteLength = packageByteLength;
        ProfileId = profileId;
        Options = options;
        _readLimits = CreateReadLimits(options.MaximumDecodedAssetBytes);
        SpatialSettings = spatialSettings;
    }

    internal AssetPackageBackingToken BackingToken => _backingToken;

    internal ImmutableArray<AssetPackageEntry> Entries { get; }

    internal AssetStoreOptions Options { get; }

    internal long PackageByteLength { get; }

    internal SpatialProfileId ProfileId { get; }

    internal EbinReadLimits ReadLimits => _readLimits;

    internal AssetStoreRegistration Registration => Options.Registration;

    internal SpatialSettings SpatialSettings { get; }

    private static EbinReadLimits CreateReadLimits(int maximumDecodedAssetBytes)
    {
        var maximumRecordBytes = Math.Max(64, maximumDecodedAssetBytes);
        var maximumUtf8Bytes = Math.Max(0, maximumDecodedAssetBytes);
        return new EbinReadLimits(
            maximumRecordBytes,
            64,
            64,
            1_048_576,
            maximumUtf8Bytes,
            1_048_576,
            1_048_576,
            1_048_576);
    }

    internal AssetOwnedReadDescriptor DescribeOwnedRead<T>(
        AssetStore store,
        AssetId assetId,
        object profileOrigin)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profileOrigin);

        if (!TryGetEntry(assetId, out var entry))
        {
            throw new KeyNotFoundException($"Asset {assetId} is absent from the selected content set.");
        }

        if (!Registration.TryGetCodec<T>(out _))
        {
            throw new NotSupportedException($"No asset codec is registered for {typeof(T)}.");
        }

        if (!Registration.TryGetProfiledCodecReceipt<T>(profileOrigin, out var codecReceipt))
        {
            throw new NotSupportedException($"No profiled asset codec is registered for {typeof(T)}.");
        }

        if (codecReceipt.RequiredPackageCodec is AssetPackagePayloadCodec requiredCodec
            && (entry.Codec != (byte)requiredCodec))
        {
            throw new NotSupportedException("The selected asset uses a package codec outside its read profile.");
        }

        return new AssetOwnedReadDescriptor(store, this, entry, codecReceipt);
    }

    internal ValueTask ReadExactlyAtAsync(ulong offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var stream = Volatile.Read(ref _stream);
        ObjectDisposedException.ThrowIf(stream is null, this);
        return AssetPackageReader.ReadExactlyAtAsync(stream, offset, destination, cancellationToken);
    }

    internal void ReleaseOwner()
        => ReleaseReference(releaseOwner: true);

    internal void ReleaseReference()
        => ReleaseReference(releaseOwner: false);

    internal bool TryAcquireReference()
    {
        lock (_gate)
        {
            if ((_releaseState is not 0) || (_referenceCount == 0))
            {
                return false;
            }

            if (_referenceCount == int.MaxValue)
            {
                throw new InvalidOperationException("The package backing has too many live references.");
            }

            _referenceCount++;
            return true;
        }
    }

    internal bool TryGetEntry(AssetId assetId, out AssetPackageEntry entry)
    {
        var low = 0;
        var high = Entries.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = Entries[middle];
            var comparison = candidate.AssetId.Value.CompareTo(assetId.Value);

            if (comparison == 0)
            {
                entry = candidate;
                return true;
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

        entry = default;
        return false;
    }

    private void ReleaseReference(bool releaseOwner)
    {
        FileStream streamToClose;

        lock (_gate)
        {
            if (releaseOwner)
            {
                if (_ownerReleased)
                {
                    if (_releaseState is 1)
                    {
                        throw new InvalidOperationException("The package backing owner release is still in progress.");
                    }

                    if (_releaseState is 3)
                    {
                        RethrowReleaseFailure();
                    }

                    return;
                }

                _ownerReleased = true;
            }

            if (_releaseState is 3)
            {
                RethrowReleaseFailure();
            }

            if (_releaseState is not 0)
            {
                throw new InvalidOperationException("The package backing release is already in progress or complete.");
            }

            if (_referenceCount <= 0)
            {
                throw new InvalidOperationException("The package backing reference count became negative.");
            }

            if (_referenceCount > 1)
            {
                _referenceCount--;
                return;
            }

            streamToClose = _stream
                ?? throw new InvalidOperationException("The final package backing reference has no retained stream.");
            _releaseState = 1;
        }

        try
        {
            streamToClose.Dispose();
        }
        catch (Exception failure)
        {
            lock (_gate)
            {
                _releaseFailure = failure;
                _releaseState = 3;
            }

            throw;
        }

        lock (_gate)
        {
            if ((_releaseState is not 1)
                || (_referenceCount != 1)
                || !ReferenceEquals(_stream, streamToClose))
            {
                throw new InvalidOperationException("The package backing changed during final stream release.");
            }

            _stream = null;
            _referenceCount = 0;
            _releaseState = 2;
        }
    }

    private void RethrowReleaseFailure()
    {
        var failure = _releaseFailure
            ?? throw new InvalidOperationException("The package backing has no recorded release failure.");
        ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
