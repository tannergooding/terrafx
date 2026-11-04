// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Hosting;

/// <summary>Provides the explicit active runtime, artifact, module, and numerics identity used by persistence.</summary>
public sealed class WorldPersistenceRuntimeProfile
{
    private const int KnownInstructionSetMask = 15;
    private const int MaximumModuleCount = 256;
    private readonly ImmutableArray<WorldPersistenceModuleArtifact> _modules;

    /// <summary>Initializes a runtime profile with caller-supplied SHA-256 artifact identities.</summary>
    /// <param name="engineArtifactSha256">The 32-byte engine artifact digest.</param>
    /// <param name="runtimeArtifactSha256">The 32-byte runtime artifact digest.</param>
    /// <param name="compilerArtifactSha256">The 32-byte compiler artifact digest.</param>
    /// <param name="operatingSystem">The exact supported operating-system value.</param>
    /// <param name="codeGeneration">The active code-generation mode.</param>
    /// <param name="instructionSets">The active x64 instruction-set mask.</param>
    /// <param name="numericsKernelSha256">The 32-byte numerical-kernel digest.</param>
    /// <param name="fmaPolicy">Whether fused multiply-add is permitted by the active profile.</param>
    /// <param name="maximumSimdBits">The active maximum SIMD width.</param>
    /// <param name="modules">The runtime modules in strictly increasing RFC-GUID order.</param>
    /// <exception cref="ArgumentException">A digest is not 32 bytes, a module list is not canonical, or it contains duplicate IDs.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A selector, flag, module version, or SIMD width is invalid.</exception>
    public WorldPersistenceRuntimeProfile(
        ReadOnlySpan<byte> engineArtifactSha256,
        ReadOnlySpan<byte> runtimeArtifactSha256,
        ReadOnlySpan<byte> compilerArtifactSha256,
        WorldPersistenceOperatingSystem operatingSystem,
        WorldPersistenceCodeGeneration codeGeneration,
        WorldPersistenceInstructionSet instructionSets,
        ReadOnlySpan<byte> numericsKernelSha256,
        WorldPersistenceFmaPolicy fmaPolicy,
        ushort maximumSimdBits,
        ReadOnlySpan<WorldPersistenceModuleArtifact> modules)
    {
        ValidateDigest(engineArtifactSha256, nameof(engineArtifactSha256));
        ValidateDigest(runtimeArtifactSha256, nameof(runtimeArtifactSha256));
        ValidateDigest(compilerArtifactSha256, nameof(compilerArtifactSha256));
        ValidateDigest(numericsKernelSha256, nameof(numericsKernelSha256));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(modules.Length, MaximumModuleCount);

        if (!Enum.IsDefined(operatingSystem))
        {
            throw new ArgumentOutOfRangeException(nameof(operatingSystem));
        }

        if (!Enum.IsDefined(codeGeneration))
        {
            throw new ArgumentOutOfRangeException(nameof(codeGeneration));
        }

        if (!Enum.IsDefined(fmaPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(fmaPolicy));
        }

        if (((int)instructionSets & ~KnownInstructionSetMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instructionSets));
        }

        if (maximumSimdBits is not (0 or 128 or 256 or 512))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSimdBits));
        }

        for (var index = 0; index < modules.Length; index++)
        {
            var module = modules[index];
            ValidateDigest(module.ArtifactSha256.AsSpan(), nameof(modules));

            if ((index > 0) && (CompareRfc(modules[index - 1].ModuleId, module.ModuleId) >= 0))
            {
                throw new ArgumentException("Runtime modules must be unique and ordered by RFC GUID bytes.", nameof(modules));
            }
        }

        EngineArtifactSha256 = [.. engineArtifactSha256];
        RuntimeArtifactSha256 = [.. runtimeArtifactSha256];
        CompilerArtifactSha256 = [.. compilerArtifactSha256];
        OperatingSystem = operatingSystem;
        CodeGeneration = codeGeneration;
        InstructionSets = instructionSets;
        NumericsKernelSha256 = [.. numericsKernelSha256];
        FmaPolicy = fmaPolicy;
        MaximumSimdBits = maximumSimdBits;
        _modules = [.. modules];
    }

    /// <summary>Gets the active code-generation mode.</summary>
    public WorldPersistenceCodeGeneration CodeGeneration { get; }

    /// <summary>Gets the compiler artifact SHA-256 digest.</summary>
    public ImmutableArray<byte> CompilerArtifactSha256 { get; }

    /// <summary>Gets the engine artifact SHA-256 digest.</summary>
    public ImmutableArray<byte> EngineArtifactSha256 { get; }

    /// <summary>Gets the active fused multiply-add policy.</summary>
    public WorldPersistenceFmaPolicy FmaPolicy { get; }

    /// <summary>Gets the active x64 instruction-set flags.</summary>
    public WorldPersistenceInstructionSet InstructionSets { get; }

    /// <summary>Gets the active maximum SIMD width.</summary>
    public ushort MaximumSimdBits { get; }

    /// <summary>Gets the immutable, RFC-GUID-ordered runtime modules.</summary>
    public ImmutableArray<WorldPersistenceModuleArtifact> Modules => _modules;

    /// <summary>Gets the numerical-kernel SHA-256 digest.</summary>
    public ImmutableArray<byte> NumericsKernelSha256 { get; }

    /// <summary>Gets the active operating system.</summary>
    public WorldPersistenceOperatingSystem OperatingSystem { get; }

    /// <summary>Gets the runtime artifact SHA-256 digest.</summary>
    public ImmutableArray<byte> RuntimeArtifactSha256 { get; }

    private static int CompareRfc(Guid left, Guid right)
    {
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        _ = left.TryWriteBytes(leftBytes, bigEndian: true, out _);
        _ = right.TryWriteBytes(rightBytes, bigEndian: true, out _);
        return leftBytes.SequenceCompareTo(rightBytes);
    }

    private static void ValidateDigest(ReadOnlySpan<byte> digest, string parameterName)
    {
        if (digest.Length != 32)
        {
            throw new ArgumentException("A SHA-256 digest must contain exactly 32 bytes.", parameterName);
        }
    }
}
