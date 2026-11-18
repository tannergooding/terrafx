// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

public sealed partial class AssetStore
{
    internal async Task<AssetLease<T>> LoadDescribedAsync<T>(
        AssetOwnedReadDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!descriptor.AssetId.IsValid)
        {
            throw new ArgumentException("The read descriptor is uninitialized.", nameof(descriptor));
        }

        cancellationToken.ThrowIfCancellationRequested();
        AcquireLoad<T>(
            descriptor.AssetId,
            packageBacking: null,
            backingReferenceAlreadyHeld: false,
            expectedDescriptor: descriptor,
            out var backing,
            out var entry,
            out var codec,
            out var requiredPackageCodec,
            out var decodedBytes);
        return await LoadAssetAsync(
            descriptor.AssetId,
            backing,
            entry,
            codec,
            requiredPackageCodec,
            decodedBytes,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<AssetLease<T>> LoadDescribedFromPackageAsync<T>(
        AssetOwnedReadDescriptor descriptor,
        AssetPackageBacking backing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backing);
        AssetPackageEntry entry;
        AssetStoreRegistration.IAssetCodec<T> codec;
        AssetPackagePayloadCodec? requiredPackageCodec;
        long decodedBytes;

        try
        {
            if (!descriptor.AssetId.IsValid)
            {
                throw new ArgumentException("The read descriptor is uninitialized.", nameof(descriptor));
            }

            cancellationToken.ThrowIfCancellationRequested();
            AcquireLoad<T>(
                descriptor.AssetId,
                backing,
                backingReferenceAlreadyHeld: true,
                expectedDescriptor: descriptor,
                out var loadBacking,
                out entry,
                out codec,
                out requiredPackageCodec,
                out decodedBytes);
            backing = loadBacking;
        }
        catch
        {
            backing.ReleaseReference();
            throw;
        }

        return await LoadAssetAsync(
            descriptor.AssetId,
            backing,
            entry,
            codec,
            requiredPackageCodec,
            decodedBytes,
            cancellationToken).ConfigureAwait(false);
    }

    internal bool TryDescribeOwnedRead<T>(
        AssetId assetId,
        object profileOrigin,
        out AssetOwnedReadDescriptor descriptor)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        ArgumentNullException.ThrowIfNull(profileOrigin);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var backing = _currentBacking
                ?? throw new InvalidOperationException("The asset store has no selected package.");

            if (!backing.TryGetEntry(assetId, out var entry)
                || !backing.Registration.TryGetProfiledCodecReceipt<T>(profileOrigin, out var codecReceipt)
                || (codecReceipt.RequiredPackageCodec is AssetPackagePayloadCodec requiredCodec
                    && (entry.Codec != (byte)requiredCodec)))
            {
                descriptor = default;
                return false;
            }

            descriptor = new AssetOwnedReadDescriptor(this, backing, entry, codecReceipt);
            return true;
        }
    }
}
