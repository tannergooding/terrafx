// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Assets;

internal sealed class AssetLeaseLifetime
{
    private readonly Lock _gate = new();
    private AssetPackageBacking? _backing;
    private readonly long _decodedBytes;
    private int _referenceCount = 1;
    private bool _backingReferenceReleased;
    private Exception? _releaseFailure;
    private int _releaseState;
    private AssetStore? _store;

    internal AssetLeaseLifetime(AssetStore store, AssetPackageBacking backing, long decodedBytes)
    {
        _store = store;
        _backing = backing;
        _decodedBytes = decodedBytes;
    }

    internal AssetPackageBackingToken BackingToken
    {
        get
        {
            var (_, backing) = AcquirePackageBackingReference();

            try
            {
                return backing.BackingToken;
            }
            finally
            {
                backing.ReleaseReference();
            }
        }
    }

    internal SpatialProfileId ProfileId
    {
        get
        {
            var (_, backing) = AcquirePackageBackingReference();

            try
            {
                return backing.ProfileId;
            }
            finally
            {
                backing.ReleaseReference();
            }
        }
    }

    internal SpatialSettings SpatialSettings
    {
        get
        {
            var (_, backing) = AcquirePackageBackingReference();

            try
            {
                return backing.SpatialSettings;
            }
            finally
            {
                backing.ReleaseReference();
            }
        }
    }

    internal (AssetStore Store, AssetPackageBacking Backing) AcquirePackageBackingReference()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                (_releaseState != 0)
                    || (_referenceCount <= 0)
                    || (_store is null)
                    || (_backing is null),
                this);
            var store = _store;
            var backing = _backing;
            ObjectDisposedException.ThrowIf(!backing.TryAcquireReference(), this);
            return (store, backing);
        }
    }

    internal void AddReference()
    {
        AssetStore store;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                (_releaseState != 0)
                    || (_referenceCount <= 0)
                    || (_store is null),
                this);
            store = _store;

            if (_referenceCount == int.MaxValue)
            {
                throw new InvalidOperationException("The asset lease has too many retained handles.");
            }
        }

        store.ReserveRetainedLease();
        var referenceAcquired = false;

        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(
                    (_releaseState != 0) || (_referenceCount <= 0),
                    this);

                if (_referenceCount == int.MaxValue)
                {
                    throw new InvalidOperationException("The asset lease has too many retained handles.");
                }

                _referenceCount++;
                referenceAcquired = true;
            }
        }
        finally
        {
            if (!referenceAcquired)
            {
                store.ReleaseLeaseReference(0, releaseDecodedBytes: false);
            }
        }
    }

    internal void ReleaseReference()
    {
        AssetStore store;
        AssetPackageBacking backing;

        lock (_gate)
        {
            if (_releaseState is 3 or 4)
            {
                RethrowReleaseFailure();
            }

            if (_releaseState is not 0)
            {
                throw new InvalidOperationException("The asset lease reference release is already in progress or complete.");
            }

            if (_referenceCount <= 0)
            {
                throw new InvalidOperationException("The asset lease reference count became negative.");
            }

            store = _store
                ?? throw new InvalidOperationException("The asset lease lifetime lost its owning store.");

            if (_referenceCount > 1)
            {
                store.ReleaseLeaseReference(0, releaseDecodedBytes: false);
                _referenceCount--;
                return;
            }

            backing = _backing
                ?? throw new InvalidOperationException("The final asset lease reference lost its package backing.");
            _releaseState = 1;
        }

        try
        {
            backing.ReleaseReference();
            lock (_gate)
            {
                _backingReferenceReleased = true;
            }

            store.ReleaseLeaseReference(_decodedBytes, releaseDecodedBytes: true);
        }
        catch (Exception failure)
        {
            lock (_gate)
            {
                _releaseFailure = failure;
                _releaseState = _backingReferenceReleased ? 4 : 3;
            }

            throw;
        }

        lock (_gate)
        {
            if ((_releaseState is not 1)
                || (_referenceCount != 1)
                || !ReferenceEquals(_store, store)
                || !ReferenceEquals(_backing, backing))
            {
                throw new InvalidOperationException("The asset lease lifetime changed during final reference release.");
            }

            _store = null;
            _backing = null;
            _referenceCount = 0;
            _releaseState = 2;
        }
    }

    internal bool TryDescribeOwnedRead<T>(
        AssetId assetId,
        object profileOrigin,
        out AssetOwnedReadDescriptor descriptor)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf((_releaseState != 0) || (_referenceCount <= 0), this);
            var store = _store
                ?? throw new InvalidOperationException("The asset lease lifetime lost its owning store.");
            var backing = _backing
                ?? throw new InvalidOperationException("The asset lease lifetime lost its package backing.");

            if (!backing.TryGetEntry(assetId, out var entry)
                || !backing.Registration.TryGetProfiledCodecReceipt<T>(profileOrigin, out var codecReceipt))
            {
                descriptor = default;
                return false;
            }

            descriptor = new AssetOwnedReadDescriptor(store, backing, entry, codecReceipt);
            return true;
        }
    }

    private void RethrowReleaseFailure()
    {
        var failure = _releaseFailure
            ?? throw new InvalidOperationException("The asset lease lifetime has no recorded release failure.");
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
