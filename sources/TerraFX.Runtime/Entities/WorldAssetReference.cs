// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace TerraFX.Entities;

/// <summary>Copies an asset identity and selected schema major without retaining package ownership.</summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and comparison operators on value types",
    Justification = "WorldAssetReference is a typed selector; its contract does not define value equality.")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct WorldAssetReference
{
    private readonly int _assetId;
    private readonly int _schemaMajor;

    internal WorldAssetReference(int assetId, int schemaMajor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(assetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaMajor);
        _assetId = assetId;
        _schemaMajor = schemaMajor;
    }

    internal int AssetId
    {
        get
        {
            ThrowIfInvalid();
            return _assetId;
        }
    }

    internal bool IsValid => (_assetId > 0) && (_schemaMajor > 0);

    internal int SchemaMajor
    {
        get
        {
            ThrowIfInvalid();
            return _schemaMajor;
        }
    }

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("The WorldAssetReference is uninitialized or invalid.");
        }
    }
}
