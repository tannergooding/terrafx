// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Assets;

public sealed partial class AssetStore
{
    /// <summary>Acquires a package lease over the exact backing currently selected by this store.</summary>
    /// <returns>A lease that keeps the selected package backing alive across replacement and store disposal.</returns>
    /// <exception cref="InvalidOperationException">The store has no selected package or its live-lease budget is exhausted.</exception>
    /// <exception cref="ObjectDisposedException">This store has been disposed.</exception>
    public AssetPackageLease AcquirePackageLease()
    {
        AssetPackageBacking backing;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            backing = _currentBacking
                ?? throw new InvalidOperationException("The asset store has no selected package.");

            if (_liveAssetLeases >= backing.Options.MaximumLiveAssetLeases)
            {
                throw new InvalidOperationException("The live asset lease budget is exhausted.");
            }

            ObjectDisposedException.ThrowIf(!backing.TryAcquireReference(), backing);
            _liveAssetLeases++;
        }

        try
        {
            return new AssetPackageLease(this, backing);
        }
        catch
        {
            ReleaseLeaseReference(0, releaseDecodedBytes: false);
            backing.ReleaseReference();
            throw;
        }
    }
}
