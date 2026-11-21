// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal sealed class SceneActivationRequest : SceneOwnerRequest
{
    internal const long ActivationBootstrapWorkspaceBytes = 256;

    internal const long ActivationCompletionWorkspaceBytes = 512;

    private WorldGuidUse _activationUse;
    private readonly AssetStore _assets;
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource<SceneActivationReceipt> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _maximumPreparationBytes;
    private SceneLoadOptionsSnapshot _options;
    private SceneExpansionPlan? _plan;
    private PreparedScene? _preparationOwner;
    private PreparedScene? _preparedScene;
    private readonly World _world;

    internal SceneActivationRequest(
        PreparedScene preparedScene,
        World world,
        AssetStore assets,
        long maximumPreparationBytes,
        WorldGuidUse activationUse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparedScene);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(assets);

        if (!activationUse.IsValid)
        {
            throw new ArgumentException("Scene activation requires its separately admitted World use.", nameof(activationUse));
        }

        _preparedScene = preparedScene;
        _activationUse = activationUse;
        _activationUse.Control.SetRetainedOwner(this);
        _cancellationToken = cancellationToken;
        _assets = assets;
        _maximumPreparationBytes = maximumPreparationBytes;
        _world = world;
    }

    internal CancellationToken CancellationToken => _cancellationToken;

    internal SceneLoadOptionsSnapshot Options => _options;

    internal SceneExpansionPlan Plan
        => _plan ?? throw new ObjectDisposedException(nameof(SceneActivationRequest));

    internal override Exception? RetainedCleanupFailure
        => _preparationOwner?.CleanupFailure ?? _plan?.CleanupFailure?.SourceException;

    internal Task<SceneActivationReceipt> Task => _completion.Task;

    internal void AcceptPreparationBody(
        PreparedScene owner,
        SceneExpansionPlan plan,
        SceneLoadOptionsSnapshot options)
    {
        _preparationOwner = owner;
        _plan = plan;
        _options = options;
    }

    internal override void Admit()
        => PreparedScene.ThrowForPreparationAccess(AdmitPreparationBody());

    internal ScenePreparationAccess AdmitPreparationBody()
    {
        var preparedScene = _preparedScene;

        if (preparedScene is null)
        {
            return ScenePreparationAccess.AlreadyAdmitted;
        }

        var access = preparedScene.DetachForActivation(
            _world,
            _assets,
            this,
            _maximumPreparationBytes);

        if (access is ScenePreparationAccess.Available)
        {
            _preparedScene = null;
        }

        return access;
    }

    internal override void CancelFromRegistryDisposal()
    {
        var cleanupFailure = ReleasePreparationBody(operationFailureStage: 3);

        try
        {
            _ = _completion.TrySetException(cleanupFailure ?? new ObjectDisposedException(nameof(SceneWorldRegistry)));
        }
        catch
        {
            QuarantineActivationUse(failureStage: 3);
            throw;
        }

        var activationCleanupFailure = ReleaseActivationUseAfterPublication(failureStage: 3);

        if (activationCleanupFailure is not null)
        {
            throw activationCleanupFailure;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "An admitted owner request must fault its own task without aborting later requests at the World boundary.")]
    internal override void Execute(World world, SceneWorldRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);

        SceneActivationReceipt? receipt = null;
        Exception? operationFailure = null;
        var canceled = false;

        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            receipt = SceneActivationExecutor.Activate(world, registry, this);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            canceled = true;
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        var cleanupFailure = ReleasePreparationBody(operationFailureStage: 3);

        try
        {
            if (cleanupFailure is not null)
            {
                _ = _completion.TrySetException(cleanupFailure);
            }
            else if (operationFailure is not null)
            {
                _ = _completion.TrySetException(operationFailure);
            }
            else if (canceled)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
            else
            {
                _ = _completion.TrySetResult(
                    receipt ?? throw new InvalidOperationException("The Scene activation produced no receipt."));
            }
        }
        catch
        {
            QuarantineActivationUse(failureStage: 3);
            throw;
        }

        var activationCleanupFailure = ReleaseActivationUseAfterPublication(failureStage: 3);

        if (activationCleanupFailure is not null)
        {
            throw activationCleanupFailure;
        }
    }

    internal override ExceptionDispatchInfo GetCleanupDispatch(Exception exception)
        => (_plan?.CleanupFailure is { } failure && ReferenceEquals(failure.SourceException, exception))
            ? failure
            : ExceptionDispatchInfo.Capture(exception);

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed terminal receipt publication must retain its admitted activation-use anchor.")]
    internal void Reject(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var cleanupFailure = ReleasePreparationBody(operationFailureStage: 3);

        if (cleanupFailure is not null)
        {
            try
            {
                _ = _completion.TrySetException(cleanupFailure);
            }
            catch
            {
                QuarantineActivationUse(failureStage: 3);
                throw;
            }

            var activationCleanupFailure = ReleaseActivationUseAfterPublication(failureStage: 3);

            if (activationCleanupFailure is not null)
            {
                throw activationCleanupFailure;
            }

            return;
        }

        try
        {
            _ = _completion.TrySetResult(new SceneActivationReceipt(
                SceneActivationStatus.Rejected,
                instance: null,
                committedStructuralRevision: null,
                [diagnostic]));
        }
        catch
        {
            QuarantineActivationUse(failureStage: 3);
            throw;
        }

        var cleanupFailureAfterPublication = ReleaseActivationUseAfterPublication(failureStage: 3);

        if (cleanupFailureAfterPublication is not null)
        {
            throw cleanupFailureAfterPublication;
        }
    }

    internal void TransferPlan()
    {
        _ = Interlocked.Exchange(ref _plan, null)
            ?? throw new ObjectDisposedException(nameof(SceneActivationRequest));
    }

    private void QuarantineActivationUse(int failureStage)
    {
        if (!_activationUse.IsValid)
        {
            return;
        }

        _activationUse.Control.Quarantine(this, failureStage, failureCategory: 5);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed activation-use release must remain anchored after terminal task publication.")]
    private Exception? ReleaseActivationUseAfterPublication(int failureStage)
    {
        if (!_activationUse.IsValid)
        {
            return null;
        }

        var control = _activationUse.Control;

        if (control.IsQuarantined)
        {
            return null;
        }

        if (!control.IsActive)
        {
            return new InvalidOperationException("The Scene activation use is not active after publication.");
        }

        try
        {
            if (!control.TryReleaseWorkspace(ActivationCompletionWorkspaceBytes)
                || !control.TryReleaseWorkspace(ActivationBootstrapWorkspaceBytes))
            {
                throw new InvalidOperationException("The Scene activation did not release its preclaimed completion workspace.");
            }

            _activationUse.Dispose();
            _activationUse = default;
            return null;
        }
        catch (Exception exception)
        {
            control.Quarantine(this, failureStage, failureCategory: 4);
            return exception;
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "An admitted owner request must fault its own task without aborting later requests at the World boundary.")]
    [SuppressMessage(
        "Style",
        "IDE0059:Unnecessary assignment of a value",
        Justification = "End the request's final plan alias before returning its preparation-body role.")]
    private Exception? ReleasePreparationBody(int operationFailureStage)
    {
        _preparedScene = null;
        _options = default;
        var plan = _plan;

        if (plan is not null)
        {
            try
            {
                plan.Dispose();
            }
            catch (Exception exception)
            {
                _preparationOwner?.QuarantineAdmittedBody(this, exception, operationFailureStage, category: 2);

                if (_activationUse.IsValid)
                {
                    _activationUse.Control.Quarantine(this, operationFailureStage, failureCategory: 2);
                }

                return exception;
            }

            _plan = null;
        }

        plan = null;

        if (_preparationOwner is { } preparationOwner)
        {
            try
            {
                preparationOwner.CompleteAdmittedBody(this);
            }
            catch (Exception exception)
            {
                if (_activationUse.IsValid)
                {
                    _activationUse.Control.Quarantine(this, operationFailureStage, failureCategory: 4);
                }

                return exception;
            }

            _preparationOwner = null;
        }

        return null;
    }
}
