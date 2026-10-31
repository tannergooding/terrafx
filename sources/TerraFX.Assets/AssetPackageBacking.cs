// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

internal sealed class AssetPackageBacking
{
    private int _ownerReleased;
    private readonly EbinReadLimits _readLimits;
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

    internal ValueTask ReadExactlyAtAsync(ulong offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var stream = Volatile.Read(ref _stream);
        ObjectDisposedException.ThrowIf(stream is null, this);
        return AssetPackageReader.ReadExactlyAtAsync(stream, offset, destination, cancellationToken);
    }

    internal void ReleaseOwner()
    {
        if (Interlocked.Exchange(ref _ownerReleased, 1) == 0)
        {
            ReleaseReference();
        }
    }

    internal void ReleaseReference()
    {
        var referenceCount = Interlocked.Decrement(ref _referenceCount);

        if (referenceCount < 0)
        {
            throw new InvalidOperationException("The package backing reference count became negative.");
        }

        if (referenceCount == 0)
        {
            Interlocked.Exchange(ref _stream, null)?.Dispose();
        }
    }

    internal bool TryAcquireReference()
    {
        while (true)
        {
            var referenceCount = Volatile.Read(ref _referenceCount);

            if (referenceCount == 0)
            {
                return false;
            }

            if (referenceCount == int.MaxValue)
            {
                throw new InvalidOperationException("The package backing has too many live references.");
            }

            if (Interlocked.CompareExchange(ref _referenceCount, referenceCount + 1, referenceCount) == referenceCount)
            {
                return true;
            }
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
}
