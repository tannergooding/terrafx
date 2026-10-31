// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Assets;

/// <summary>Retains one decoded asset and the immutable package backing that produced it.</summary>
/// <typeparam name="T">The registered immutable asset value type.</typeparam>
public sealed class AssetLease<T> : IDisposable
{
    private readonly AssetId _assetId;
    private AssetLeaseLifetime? _leaseLifetime;
    private T _value;

    internal AssetLease(AssetId assetId, T value, AssetLeaseLifetime leaseLifetime)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        ArgumentNullException.ThrowIfNull(leaseLifetime);
        _assetId = assetId;
        _value = value;
        _leaseLifetime = leaseLifetime;
    }

    /// <summary>Gets the identity of the leased asset.</summary>
    public AssetId AssetId => _assetId;

    /// <summary>Gets the decoded asset value while this lease is active.</summary>
    /// <exception cref="ObjectDisposedException">This lease has been disposed.</exception>
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _leaseLifetime) is null, this);
            return _value;
        }
    }

    /// <summary>Releases the retained package backing and invalidates <see cref="Value"/>.</summary>
    public void Dispose()
    {
        var lifetime = Interlocked.Exchange(ref _leaseLifetime, null);

        if (lifetime is not null)
        {
            _value = default!;
            lifetime.Dispose();
        }
    }
}
