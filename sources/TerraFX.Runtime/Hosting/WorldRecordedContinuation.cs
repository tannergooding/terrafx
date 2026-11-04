// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Hosting;

/// <summary>Owns one engine-prepared finite replay interval and its retained source data.</summary>
public sealed class WorldRecordedContinuation : IDisposable
{
    private readonly byte[] _replayDigest;
    private readonly byte[] _sourceCheckpointDigest;
    private IDisposable? _sourceOwnership;
    private readonly byte[] _sourcePayloadDigest;

    internal WorldRecordedContinuation(
        Guid sourceWorldGuid,
        Guid replayLineageId,
        Guid identityBranchId,
        long firstTick,
        long endTickExclusive,
        ReadOnlySpan<byte> sourceCheckpointDigest,
        ReadOnlySpan<byte> sourcePayloadDigest,
        ReadOnlySpan<byte> replayDigest,
        IDisposable sourceOwnership)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sourceWorldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(replayLineageId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(identityBranchId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstTick, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endTickExclusive, firstTick);
        ArgumentNullException.ThrowIfNull(sourceOwnership);
        ValidateDigest(sourceCheckpointDigest, nameof(sourceCheckpointDigest));
        ValidateDigest(sourcePayloadDigest, nameof(sourcePayloadDigest));
        ValidateDigest(replayDigest, nameof(replayDigest));

        SourceWorldGuid = sourceWorldGuid;
        ReplayLineageId = replayLineageId;
        IdentityBranchId = identityBranchId;
        FirstTick = firstTick;
        EndTickExclusive = endTickExclusive;
        _sourceCheckpointDigest = sourceCheckpointDigest.ToArray();
        _sourcePayloadDigest = sourcePayloadDigest.ToArray();
        _replayDigest = replayDigest.ToArray();
        _sourceOwnership = sourceOwnership;
    }

    /// <summary>Gets the exclusive end tick of the finite continuation interval.</summary>
    public long EndTickExclusive { get; }

    /// <summary>Gets the first tick in the finite continuation interval.</summary>
    public long FirstTick { get; }

    /// <summary>Gets the identity branch admitted for the continuation.</summary>
    public Guid IdentityBranchId { get; }

    /// <summary>Gets the immutable digest of the selected replay interval.</summary>
    public ReadOnlySpan<byte> ReplayDigest => _replayDigest;

    /// <summary>Gets the replay lineage admitted for the continuation.</summary>
    public Guid ReplayLineageId { get; }

    /// <summary>Gets the immutable digest of the complete source checkpoint.</summary>
    public ReadOnlySpan<byte> SourceCheckpointDigest => _sourceCheckpointDigest;

    /// <summary>Gets the immutable digest of the source World payload.</summary>
    public ReadOnlySpan<byte> SourcePayloadDigest => _sourcePayloadDigest;

    /// <summary>Gets the identity of the checkpoint World.</summary>
    public Guid SourceWorldGuid { get; }

    private static void ValidateDigest(ReadOnlySpan<byte> digest, string parameterName)
    {
        if (digest.Length != 32)
        {
            throw new ArgumentException("A continuation digest must contain exactly 32 bytes.", parameterName);
        }
    }

    /// <summary>Releases the continuation's retained source data and package pins.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _sourceOwnership, null)?.Dispose();

    internal void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _sourceOwnership) is null, this);
}
