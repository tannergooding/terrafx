// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Hosting;

/// <summary>Identifies one runtime module and its artifact digest.</summary>
public readonly record struct WorldPersistenceModuleArtifact
{
    /// <summary>Initializes one runtime module artifact.</summary>
    /// <param name="moduleId">The non-empty stable module identifier.</param>
    /// <param name="version">The positive module version.</param>
    /// <param name="artifactSha256">The 32-byte artifact SHA-256 digest.</param>
    /// <exception cref="ArgumentException"><paramref name="moduleId"/> is empty or the digest is not 32 bytes.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is zero.</exception>
    public WorldPersistenceModuleArtifact(Guid moduleId, uint version, ReadOnlySpan<byte> artifactSha256)
    {
        if (moduleId == Guid.Empty)
        {
            throw new ArgumentException("The module ID must not be empty.", nameof(moduleId));
        }

        ArgumentOutOfRangeException.ThrowIfZero(version);

        if (artifactSha256.Length != 32)
        {
            throw new ArgumentException("A SHA-256 digest must contain exactly 32 bytes.", nameof(artifactSha256));
        }

        ModuleId = moduleId;
        Version = version;
        ArtifactSha256 = [.. artifactSha256];
    }

    /// <summary>Gets the stable module identifier.</summary>
    public Guid ModuleId { get; }

    /// <summary>Gets the positive module version.</summary>
    public uint Version { get; }

    /// <summary>Gets the immutable 32-byte artifact digest.</summary>
    public ImmutableArray<byte> ArtifactSha256 { get; }
}
