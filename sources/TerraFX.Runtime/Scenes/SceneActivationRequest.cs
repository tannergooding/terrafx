// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal sealed class SceneActivationRequest : SceneOwnerRequest
{
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource<SceneActivationReceipt> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SceneLoadOptionsSnapshot _options;
    private SceneExpansionPlan? _plan;
    private PreparedScene? _preparedScene;
    private readonly World _world;

    internal SceneActivationRequest(
        PreparedScene preparedScene,
        World world,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparedScene);
        ArgumentNullException.ThrowIfNull(world);
        _preparedScene = preparedScene;
        _options = preparedScene.Options;
        _cancellationToken = cancellationToken;
        _world = world;
    }

    internal CancellationToken CancellationToken => _cancellationToken;

    internal SceneLoadOptionsSnapshot Options => _options;

    internal SceneExpansionPlan Plan
        => _plan ?? throw new ObjectDisposedException(nameof(SceneActivationRequest));

    internal Task<SceneActivationReceipt> Task => _completion.Task;

    internal override void Admit()
    {
        var preparedScene = Interlocked.Exchange(ref _preparedScene, null)
            ?? throw new InvalidOperationException("The Scene activation request has already been admitted.");
        _plan = preparedScene.DetachPlan(_world);
    }

    internal override void CancelFromRegistryDisposal()
    {
        Interlocked.Exchange(ref _plan, null)?.Dispose();
        _ = _completion.TrySetException(new ObjectDisposedException(nameof(SceneWorldRegistry)));
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "An admitted owner request must fault its own task without aborting later requests at the World boundary.")]
    internal override void Execute(World world, SceneWorldRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);

        if (_cancellationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _plan, null)?.Dispose();
            _ = _completion.TrySetCanceled(_cancellationToken);
            return;
        }

        try
        {
            var receipt = SceneActivationExecutor.Activate(world, registry, this);
            _ = _completion.TrySetResult(receipt);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _ = _completion.TrySetCanceled(_cancellationToken);
        }
        catch (Exception exception)
        {
            _ = _completion.TrySetException(exception);
        }
        finally
        {
            Interlocked.Exchange(ref _plan, null)?.Dispose();
        }
    }

    internal void Reject(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        Interlocked.Exchange(ref _plan, null)?.Dispose();
        _ = _completion.TrySetResult(new SceneActivationReceipt(
            SceneActivationStatus.Rejected,
            instance: null,
            committedStructuralRevision: null,
            [diagnostic]));
    }

    internal void TransferPlan()
    {
        _ = Interlocked.Exchange(ref _plan, null)
            ?? throw new ObjectDisposedException(nameof(SceneActivationRequest));
    }
}
