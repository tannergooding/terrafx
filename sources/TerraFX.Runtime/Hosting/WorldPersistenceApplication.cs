// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Hosting;

/// <summary>Provides the application identity and its versioned persistence modules.</summary>
public sealed class WorldPersistenceApplication
{
    private const int MaximumModuleCount = 256;
    private readonly ImmutableArray<WorldPersistenceModuleVersion> _modules;

    /// <summary>Initializes an explicit application persistence identity.</summary>
    /// <param name="applicationId">The non-empty stable application identifier.</param>
    /// <param name="majorVersion">The positive application schema major version.</param>
    /// <param name="minorVersion">The application schema minor version.</param>
    /// <param name="modules">The application modules in strictly increasing RFC-GUID order.</param>
    /// <exception cref="ArgumentException">The application ID is empty, the module list is not canonical, or it contains duplicate IDs.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A version is invalid or too many modules were supplied.</exception>
    public WorldPersistenceApplication(
        Guid applicationId,
        uint majorVersion,
        uint minorVersion,
        ReadOnlySpan<WorldPersistenceModuleVersion> modules)
    {
        if (applicationId == Guid.Empty)
        {
            throw new ArgumentException("The application ID must not be empty.", nameof(applicationId));
        }

        ArgumentOutOfRangeException.ThrowIfZero(majorVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(modules.Length, MaximumModuleCount);

        for (var index = 0; index < modules.Length; index++)
        {
            var module = modules[index];

            if (module.ModuleId == Guid.Empty)
            {
                throw new ArgumentException("Application module IDs must not be empty.", nameof(modules));
            }

            if (module.Version == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(modules));
            }

            if ((index > 0) && (CompareRfc(modules[index - 1].ModuleId, module.ModuleId) >= 0))
            {
                throw new ArgumentException("Application modules must be unique and ordered by RFC GUID bytes.", nameof(modules));
            }
        }

        ApplicationId = applicationId;
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        _modules = [.. modules];
    }

    /// <summary>Gets the stable application identifier.</summary>
    public Guid ApplicationId { get; }

    /// <summary>Gets the application schema major version.</summary>
    public uint MajorVersion { get; }

    /// <summary>Gets the application schema minor version.</summary>
    public uint MinorVersion { get; }

    /// <summary>Gets the immutable, RFC-GUID-ordered application modules.</summary>
    public ImmutableArray<WorldPersistenceModuleVersion> Modules => _modules;

    private static int CompareRfc(Guid left, Guid right)
    {
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        _ = left.TryWriteBytes(leftBytes, bigEndian: true, out _);
        _ = right.TryWriteBytes(rightBytes, bigEndian: true, out _);
        return leftBytes.SequenceCompareTo(rightBytes);
    }
}
