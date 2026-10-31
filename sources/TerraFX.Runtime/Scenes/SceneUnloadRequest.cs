// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal sealed class SceneUnloadRequest : SceneOwnerRequest
{
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource<SceneUnloadReceipt> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SceneInstance _instance;
    private readonly HierarchyDestroyMode _mode;

    internal SceneUnloadRequest(
        SceneInstance instance,
        HierarchyDestroyMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _instance = instance;
        _mode = mode;
        _cancellationToken = cancellationToken;
    }

    internal Task<SceneUnloadReceipt> Task => _completion.Task;

    internal override void CancelFromRegistryDisposal()
        => _ = _completion.TrySetException(new ObjectDisposedException(nameof(SceneWorldRegistry)));

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "An admitted unload request must fault its own task without aborting later requests at the World boundary.")]
    internal override void Execute(World world, SceneWorldRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);

        if (_cancellationToken.IsCancellationRequested)
        {
            _ = _completion.TrySetCanceled(_cancellationToken);
            return;
        }

        if (!registry.ContainsInstance(_instance))
        {
            _ = _completion.TrySetResult(new SceneUnloadReceipt(
                SceneUnloadStatus.AlreadyUnloaded,
                _instance.SceneInstanceId,
                world.CanonicalMutationRevision,
                []));
            return;
        }

        try
        {
            var revision = world.DestroySceneEntitiesAtomically(
                _instance.SceneInstanceId,
                _instance.FirstEntityIdentityOrdinal,
                _instance.EntityCount,
                _mode,
                _cancellationToken);
            registry.RemoveInstance(_instance);
            _instance.RevokeFromWorld();
            _ = _completion.TrySetResult(new SceneUnloadReceipt(
                SceneUnloadStatus.Unloaded,
                _instance.SceneInstanceId,
                revision,
                []));
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _ = _completion.TrySetCanceled(_cancellationToken);
        }
        catch (Exception exception)
        {
            _ = _completion.TrySetException(exception);
        }
    }

    internal void Reject(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _ = _completion.TrySetResult(new SceneUnloadReceipt(
            SceneUnloadStatus.Rejected,
            _instance.SceneInstanceId,
            structuralRevision: null,
            [diagnostic]));
    }
}
