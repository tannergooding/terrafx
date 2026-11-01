// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

/// <summary>Identifies the exact offline shader compiler package used for a reflection record.</summary>
public readonly struct ShaderCompilerIdentity : IEquatable<ShaderCompilerIdentity>
{
    private readonly ImmutableArray<byte> _packageSha256;

    /// <summary>Initializes an offline compiler package identity.</summary>
    /// <param name="packageId">The nonempty package ID.</param>
    /// <param name="releaseVersion">The nonempty upstream source release version.</param>
    /// <param name="packageVersion">The nonempty package version.</param>
    /// <param name="packageSha256">The 32-byte SHA-256 digest of the package artifact.</param>
    /// <exception cref="ArgumentException">A string is empty or the digest is not exactly 32 initialized bytes.</exception>
    public ShaderCompilerIdentity(
        string packageId,
        string releaseVersion,
        string packageVersion,
        ImmutableArray<byte> packageSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);

        if (packageSha256.IsDefault || (packageSha256.Length != 32))
        {
            throw new ArgumentException("A compiler package SHA-256 digest must contain exactly 32 initialized bytes.", nameof(packageSha256));
        }

        PackageId = packageId;
        ReleaseVersion = releaseVersion;
        PackageVersion = packageVersion;
        _packageSha256 = [.. packageSha256.ToArray()];
    }

    /// <summary>Gets whether this value contains a complete compiler package identity.</summary>
    public bool IsValid
        => !string.IsNullOrWhiteSpace(PackageId)
        && !string.IsNullOrWhiteSpace(ReleaseVersion)
        && !string.IsNullOrWhiteSpace(PackageVersion)
        && !_packageSha256.IsDefault
        && (_packageSha256.Length == 32);

    /// <summary>Gets the package ID.</summary>
    public string PackageId { get; }

    /// <summary>Gets the copied package artifact SHA-256 digest.</summary>
    public ImmutableArray<byte> PackageSha256 => _packageSha256;

    /// <summary>Gets the package version.</summary>
    public string PackageVersion { get; }

    /// <summary>Gets the upstream source release version.</summary>
    public string ReleaseVersion { get; }

    /// <summary>Negates compiler identity equality.</summary>
    public static bool operator !=(ShaderCompilerIdentity left, ShaderCompilerIdentity right)
        => !left.Equals(right);

    /// <summary>Compares compiler package identity and artifact digest.</summary>
    public static bool operator ==(ShaderCompilerIdentity left, ShaderCompilerIdentity right)
        => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ShaderCompilerIdentity other)
        => StringComparer.Ordinal.Equals(PackageId, other.PackageId)
        && StringComparer.Ordinal.Equals(ReleaseVersion, other.ReleaseVersion)
        && StringComparer.Ordinal.Equals(PackageVersion, other.PackageVersion)
        && _packageSha256.AsSpan().SequenceEqual(other._packageSha256.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => (obj is ShaderCompilerIdentity other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PackageId, StringComparer.Ordinal);
        hash.Add(ReleaseVersion, StringComparer.Ordinal);
        hash.Add(PackageVersion, StringComparer.Ordinal);

        foreach (var value in _packageSha256)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
