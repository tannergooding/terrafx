// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Security.Cryptography;
using System.Threading;
using TerraFX.Entities;

namespace TerraFX.Hosting;

/// <summary>Owns one validated, target-bound World replacement plan.</summary>
public sealed class WorldLoadPlan : IDisposable
{
    private readonly Lock _gate = new();
    private bool _isDisposed;
    private bool _isInitialized;
    private byte[]? _sourceCheckpointDigest;
    private byte[]? _sourcePayloadDigest;
    private WorldLoadPreparedState? _state;
    private readonly World _targetWorld;

    internal WorldLoadPlan(
        World targetWorld,
        Guid targetWorldGuid,
        Guid sourceWorldGuid,
        Guid targetRecordWorldGuid,
        long snapshotTick)
    {
        ArgumentNullException.ThrowIfNull(targetWorld);
        ArgumentOutOfRangeException.ThrowIfNotEqual(targetWorld.WorldGuid, targetWorldGuid);
        ArgumentOutOfRangeException.ThrowIfEqual(targetWorldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceWorldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(targetRecordWorldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(snapshotTick);
        _targetWorld = targetWorld;
        TargetWorldGuid = targetWorldGuid;
        SourceWorldGuid = sourceWorldGuid;
        TargetRecordWorldGuid = targetRecordWorldGuid;
        SnapshotTick = snapshotTick;
    }

    /// <summary>Gets the explicit intent selected for this restore.</summary>
    public WorldRestoreIntent RestoreIntent
        => _isInitialized
            ? _state!.RestoreIntent
            : throw new InvalidOperationException("The World load plan has not been initialized.");

    /// <summary>Gets the source checkpoint's next tick.</summary>
    public long SnapshotTick { get; }

    /// <summary>Gets the digest of the complete source checkpoint.</summary>
    public ReadOnlySpan<byte> SourceCheckpointDigest
        => _sourceCheckpointDigest
            ?? throw new InvalidOperationException("The World load plan has not been initialized.");

    /// <summary>Gets the digest of the complete source World payload.</summary>
    public ReadOnlySpan<byte> SourcePayloadDigest
        => _sourcePayloadDigest
            ?? throw new InvalidOperationException("The World load plan has not been initialized.");

    /// <summary>Gets the semantic World identity stored in the source checkpoint.</summary>
    public Guid SourceWorldGuid { get; }

    /// <summary>Gets the World identity that the replacement records will use.</summary>
    public Guid TargetRecordWorldGuid { get; }

    /// <summary>Gets the exact operational World binding that was targeted by preparation.</summary>
    public Guid TargetWorldGuid { get; }

    /// <summary>Gets the semantic World identity used by the target records.</summary>
    public Guid WorldGuid => TargetRecordWorldGuid;

    internal World TargetWorld => _targetWorld;

    /// <summary>Closes plan acquisition; an already captured commit retains independent ownership.</summary>
    public void Dispose()
    {
        WorldLoadPreparedState? state;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            state = _state;
        }

        state?.CloseAcquisition();
    }

    internal void Initialize(
        WorldLoadPreparedState state,
        scoped ReadOnlySpan<byte> sourceCheckpointDigest,
        scoped ReadOnlySpan<byte> sourcePayloadDigest)
    {
        ArgumentNullException.ThrowIfNull(state);

        if ((sourceCheckpointDigest.Length != 32) || (sourcePayloadDigest.Length != 32))
        {
            throw new ArgumentException("World load source digests must each contain exactly 32 bytes.");
        }

        Span<byte> expectedPayloadDigest = stackalloc byte[32];
        _ = SHA256.HashData(state.Checkpoint.WorldRecord.Span, expectedPayloadDigest);

        if ((state.Checkpoint.WorldGuid != SourceWorldGuid)
            || (state.Checkpoint.NextTick != SnapshotTick)
            || (state.ConstructionPlan.WorldGuid != TargetRecordWorldGuid)
            || (state.ConstructionPlan.RestoreIdentityState is null)
            || (state.RestoreIntent is WorldRestoreIntent.None)
            || !CryptographicOperations.FixedTimeEquals(sourceCheckpointDigest, state.Checkpoint.CheckpointDigest.Span)
            || !CryptographicOperations.FixedTimeEquals(sourcePayloadDigest, expectedPayloadDigest))
        {
            throw new ArgumentException("The prepared World state does not match the load plan's target or source identity.", nameof(state));
        }

        var checkpointDigest = sourceCheckpointDigest.ToArray();
        var payloadDigest = sourcePayloadDigest.ToArray();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_isInitialized)
            {
                throw new InvalidOperationException("The World load plan was already initialized.");
            }

            _sourceCheckpointDigest = checkpointDigest;
            _sourcePayloadDigest = payloadDigest;
            _state = state;
            _isInitialized = true;
        }
    }

    internal bool TryCaptureCommit(
        out WorldLoadPreparedState? state,
        out WorldDomainOperationCreditBundle? operationCredits,
        out WorldDefaultDomainHandoff? defaultDomainHandoff,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal admissionRefusal)
    {
        lock (_gate)
        {
            operationCredits = null;
            defaultDomainHandoff = null;

            if (_isDisposed || !_isInitialized)
            {
                state = null;
                bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                admissionRefusal = WorldAdmissionRefusal.None;
                return false;
            }

            var preparedState = _state
                ?? throw new InvalidOperationException("The initialized World load plan omitted its prepared state.");

            if (!preparedState.TryCaptureCommit(
                    out operationCredits,
                    out defaultDomainHandoff,
                    out bindingRefusal,
                    out admissionRefusal))
            {
                state = null;
                preparedState.CloseAcquisition();
                return false;
            }

            state = preparedState;
            return true;
        }
    }
}
