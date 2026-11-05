// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Entities;

namespace TerraFX.Hosting;

/// <summary>Maps live identities from one pinned checkpoint to the corresponding replacement World.</summary>
public sealed class WorldIdentityRemap : IDisposable
{
    private readonly Lock _gate = new();
    private WorldIdentityRemapEntry[]? _entities;
    private bool _isDisposed;
    private bool _isInitialized;
    private IDisposable? _remapLease;
    private WorldIdentityRemapEntry[]? _sceneInstances;
    private byte[]? _sourceCheckpointDigest;
    private Guid _sourceIdentityBranchId;
    private byte[]? _sourcePayloadDigest;
    private Guid _sourceReplayLineageId;
    private Guid _sourceWorldGuid;
    private Guid _targetIdentityBranchId;
    private Guid _targetReplayLineageId;
    private Guid _targetWorldGuid;

    /// <summary>Initializes an empty remap wrapper for engine-owned preparation.</summary>
    internal WorldIdentityRemap()
    {
    }

    /// <summary>Gets the complete source checkpoint digest.</summary>
    public ReadOnlySpan<byte> SourceCheckpointDigest
        => _sourceCheckpointDigest
            ?? throw new InvalidOperationException("The remap has not been initialized.");

    /// <summary>Gets the source identity branch.</summary>
    public Guid SourceIdentityBranchId
        => GetInitializedGuid(0);

    /// <summary>Gets the digest of the pinned source payload.</summary>
    public ReadOnlySpan<byte> SourcePayloadDigest
        => _sourcePayloadDigest
            ?? throw new InvalidOperationException("The remap has not been initialized.");

    /// <summary>Gets the source replay lineage.</summary>
    public Guid SourceReplayLineageId
        => GetInitializedGuid(1);

    /// <summary>Gets the source World identity.</summary>
    public Guid SourceWorldGuid
        => GetInitializedGuid(2);

    /// <summary>Gets the target identity branch.</summary>
    public Guid TargetIdentityBranchId
        => GetInitializedGuid(3);

    /// <summary>Gets the target replay lineage.</summary>
    public Guid TargetReplayLineageId
        => GetInitializedGuid(4);

    /// <summary>Gets the World identity used by the replacement records.</summary>
    public Guid TargetWorldGuid
        => GetInitializedGuid(5);

    /// <summary>Releases the remap's independently retained mapping budget.</summary>
    public void Dispose()
    {
        IDisposable? remapLease;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _entities = null;
            _sceneInstances = null;
            remapLease = _remapLease;
            _remapLease = null;
        }

        remapLease?.Dispose();
    }

    /// <summary>Maps a live source Entity from the exact pinned checkpoint.</summary>
    /// <param name="sourceEntityId">The source Entity identity.</param>
    /// <param name="targetEntityId">The replacement Entity identity, when mapped.</param>
    /// <returns><see langword="true"/> when the source Entity is live and included in this remap.</returns>
    /// <exception cref="ObjectDisposedException">The remap has been disposed.</exception>
    public bool TryMapEntity(Guid sourceEntityId, out Guid targetEntityId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            EnsureInitialized();
            return WorldIdentityRemapHelpers.TryMap(_entities!, sourceEntityId, out targetEntityId);
        }
    }

    /// <summary>Maps a live source SceneInstance from the exact pinned checkpoint.</summary>
    /// <param name="sourceSceneInstanceId">The source SceneInstance identity.</param>
    /// <param name="targetSceneInstanceId">The replacement SceneInstance identity, when mapped.</param>
    /// <returns><see langword="true"/> when the source SceneInstance is live and included in this remap.</returns>
    /// <exception cref="ObjectDisposedException">The remap has been disposed.</exception>
    public bool TryMapSceneInstance(Guid sourceSceneInstanceId, out Guid targetSceneInstanceId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            EnsureInitialized();
            return WorldIdentityRemapHelpers.TryMap(_sceneInstances!, sourceSceneInstanceId, out targetSceneInstanceId);
        }
    }

    internal void Initialize(
        Guid sourceWorldGuid,
        Guid sourceReplayLineageId,
        Guid sourceIdentityBranchId,
        Guid targetWorldGuid,
        Guid targetReplayLineageId,
        Guid targetIdentityBranchId,
        scoped ReadOnlySpan<byte> sourceCheckpointDigest,
        scoped ReadOnlySpan<byte> sourcePayloadDigest,
        scoped ReadOnlySpan<Guid> sourceEntityIds,
        scoped ReadOnlySpan<Guid> targetEntityIds,
        scoped ReadOnlySpan<Guid> sourceSceneInstanceIds,
        scoped ReadOnlySpan<Guid> targetSceneInstanceIds,
        WorldDomainOperationCredit mappingCredit)
    {
        ArgumentNullException.ThrowIfNull(mappingCredit);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceWorldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceReplayLineageId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceIdentityBranchId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(targetWorldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(targetReplayLineageId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(targetIdentityBranchId, Guid.Empty);

        if ((sourceCheckpointDigest.Length != 32) || (sourcePayloadDigest.Length != 32))
        {
            throw new ArgumentException("The remap digests must each contain exactly 32 bytes.");
        }

        WorldIdentityRemapHelpers.ValidateMappings(sourceEntityIds, targetEntityIds, nameof(sourceEntityIds));
        WorldIdentityRemapHelpers.ValidateMappings(sourceSceneInstanceIds, targetSceneInstanceIds, nameof(sourceSceneInstanceIds));
        WorldIdentityRemapHelpers.ValidateDisjointSourceIds(sourceEntityIds, sourceSceneInstanceIds);

        var targetIdentityCount = checked(targetEntityIds.Length + targetSceneInstanceIds.Length);
        var retainedBytes = WorldIdentityRemapHelpers.MeasureRetainedBytes(
            sourceEntityIds.Length,
            sourceSceneInstanceIds.Length);
        IDisposable? remapLease = null;

        try
        {
            remapLease = mappingCredit.AcquireRetainedUse(retainedBytes);
            var targetIds = new Guid[targetIdentityCount];
            targetEntityIds.CopyTo(targetIds);
            targetSceneInstanceIds.CopyTo(targetIds.AsSpan(targetEntityIds.Length));
            Array.Sort(targetIds);

            for (var index = 1; index < targetIds.Length; index++)
            {
                if (targetIds[index - 1] == targetIds[index])
                {
                    throw new ArgumentException("Target Entity and SceneInstance remap identities must be unique.");
                }
            }

            var entityMappings = WorldIdentityRemapHelpers.CreateMappings(sourceEntityIds, targetEntityIds);
            var sceneMappings = WorldIdentityRemapHelpers.CreateMappings(sourceSceneInstanceIds, targetSceneInstanceIds);
            var checkpointDigest = sourceCheckpointDigest.ToArray();
            var payloadDigest = sourcePayloadDigest.ToArray();

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);

                if (_isInitialized)
                {
                    throw new InvalidOperationException("The remap has already been initialized.");
                }

                _sourceWorldGuid = sourceWorldGuid;
                _sourceReplayLineageId = sourceReplayLineageId;
                _sourceIdentityBranchId = sourceIdentityBranchId;
                _targetWorldGuid = targetWorldGuid;
                _targetReplayLineageId = targetReplayLineageId;
                _targetIdentityBranchId = targetIdentityBranchId;
                _sourceCheckpointDigest = checkpointDigest;
                _sourcePayloadDigest = payloadDigest;
                _entities = entityMappings;
                _sceneInstances = sceneMappings;
                _remapLease = remapLease;
                _isInitialized = true;
                remapLease = null;
            }
        }
        finally
        {
            remapLease?.Dispose();
        }
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("The remap has not been initialized.");
        }
    }

    private Guid GetInitializedGuid(int property)
    {
        lock (_gate)
        {
            EnsureInitialized();

            return property switch {
                0 => _sourceIdentityBranchId,
                1 => _sourceReplayLineageId,
                2 => _sourceWorldGuid,
                3 => _targetIdentityBranchId,
                4 => _targetReplayLineageId,
                5 => _targetWorldGuid,
                _ => throw new ArgumentOutOfRangeException(nameof(property)),
            };
        }
    }
}
