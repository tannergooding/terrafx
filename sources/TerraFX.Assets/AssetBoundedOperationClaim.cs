// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Assets;

internal struct AssetBoundedOperationClaim
{
    private AssetStore? _issuer;
    private readonly long _operationId;
    private readonly long _bytes;

    internal AssetBoundedOperationClaim(AssetStore issuer, long operationId, long bytes)
    {
        _issuer = issuer;
        _operationId = operationId;
        _bytes = bytes;
    }

    internal readonly long Bytes => _bytes;

    internal readonly bool IsActive => _issuer is not null;

    internal readonly int MaximumConcurrentDecodeBytes
        => _issuer?.BoundedMaximumConcurrentDecodeBytes
            ?? throw new InvalidOperationException("The bounded operation claim has no active original Store.");

    internal readonly int MaximumLiveAssetLeases
        => _issuer?.BoundedMaximumLiveAssetLeases
            ?? throw new InvalidOperationException("The bounded operation claim has no active original Store.");

    internal readonly EbinReadLimits ReadLimits
        => _issuer?.BoundedReadLimits
            ?? throw new InvalidOperationException("The bounded operation claim has no active original Store.");

    internal readonly AssetStoreRegistration Registration
        => _issuer?.BoundedRegistration
            ?? throw new InvalidOperationException("The bounded operation claim has no active original Store.");

    internal AssetBoundedOperationClaim Detach()
    {
        var issuer = _issuer
            ?? throw new InvalidOperationException("The bounded operation claim has already been returned or detached.");
        _issuer = null;
        return new AssetBoundedOperationClaim(issuer, _operationId, _bytes);
    }

    internal void Dispose()
    {
        var issuer = _issuer;

        if (issuer is null)
        {
            return;
        }

        issuer.ReturnBoundedOperationClaim(_operationId, _bytes);
        _issuer = null;
    }
}
