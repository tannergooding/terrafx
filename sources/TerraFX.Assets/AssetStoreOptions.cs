// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;

namespace TerraFX.Assets;

/// <summary>Defines one trusted package selection and bounded asset-store budgets.</summary>
public sealed class AssetStoreOptions
{
    /// <summary>Initializes package admission inputs.</summary>
    /// <param name="absolutePackagePath">The trusted absolute EPKG2 package path.</param>
    /// <param name="expectedPackageSha256">The expected whole-package SHA-256 digest.</param>
    /// <param name="registration">The statically compiled schema and codec registration.</param>
    /// <exception cref="ArgumentException">The package path or digest length is invalid.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
    public AssetStoreOptions(
        string absolutePackagePath,
        scoped ReadOnlySpan<byte> expectedPackageSha256,
        AssetStoreRegistration registration)
    {
        ArgumentException.ThrowIfNullOrEmpty(absolutePackagePath);

        if (!Path.IsPathFullyQualified(absolutePackagePath))
        {
            throw new ArgumentException("The package path must be absolute.", nameof(absolutePackagePath));
        }

        if (expectedPackageSha256.Length != 32)
        {
            throw new ArgumentException("The whole-package SHA-256 digest must contain 32 bytes.", nameof(expectedPackageSha256));
        }

        ArgumentNullException.ThrowIfNull(registration);
        AbsolutePackagePath = absolutePackagePath;
        ExpectedPackageSha256 = [.. expectedPackageSha256];
        Registration = registration;
    }

    /// <summary>Gets the trusted absolute package path.</summary>
    public string AbsolutePackagePath { get; }

    /// <summary>Gets the expected whole-package SHA-256 digest.</summary>
    public ImmutableArray<byte> ExpectedPackageSha256 { get; }

    /// <summary>Gets the immutable compiled schema and codec registration.</summary>
    public AssetStoreRegistration Registration { get; }

    /// <summary>Gets the maximum retained CPU asset backing budget.</summary>
    public long CpuAssetBudgetBytes { get; init; } = 536_870_912;

    /// <summary>Gets the maximum decoded size of one asset.</summary>
    public int MaximumDecodedAssetBytes { get; init; } = 268_435_456;

    /// <summary>Gets the maximum concurrent asset decode budget.</summary>
    public int MaximumConcurrentDecodeBytes { get; init; } = 268_435_456;

    /// <summary>Gets the maximum number of live asset leases.</summary>
    public int MaximumLiveAssetLeases { get; init; } = 4096;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(CpuAssetBudgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumDecodedAssetBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumConcurrentDecodeBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumLiveAssetLeases);
    }
}
