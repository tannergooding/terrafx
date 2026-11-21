// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
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

    internal void EndExecution(SceneWorldRegistry registry)
    {
        _instance.EndUnloadInvocation(this);
        registry.FinalizeInstanceCleanup(_instance);
    }

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
            if (_instance.CleanupFailure is { } failure)
            {
                _ = _completion.TrySetException(failure.SourceException);
            }
            else if (_instance.IsCleanupComplete)
            {
                _ = _completion.TrySetResult(new SceneUnloadReceipt(
                    SceneUnloadStatus.AlreadyUnloaded,
                    _instance.SceneInstanceId,
                    world.CanonicalMutationRevision,
                    []));
            }
            else
            {
                _ = _completion.TrySetException(
                    new InvalidOperationException("The inactive Scene still owns an unfinished cleanup invocation."));
            }

            return;
        }

        World.WorldSceneControlAppend? controlAppend = null;
        var commitFacts = default(World.WorldSceneStructuralCommitFacts);
        ExceptionDispatchInfo? operationFailure = null;
        SceneUnloadReceipt? receipt = null;
        var canceled = false;

        try
        {
            try
            {
                PrepareControl(world, out receipt, out controlAppend);

                if ((controlAppend is not null) && (receipt is null))
                {
                    controlAppend.ValidateForCommit();
                    _ = world.DestroySceneEntitiesAtomically(
                        _instance.SceneInstanceId,
                        _instance.FirstEntityIdentityOrdinal,
                        _instance.EntityCount,
                        _mode,
                        _cancellationToken,
                        out commitFacts);
                }
            }
            catch (OperationCanceledException) when (!commitFacts.InstallationStarted && _cancellationToken.IsCancellationRequested)
            {
                canceled = true;
            }
            catch (Exception exception)
            {
                operationFailure = commitFacts.Failure ?? ExceptionDispatchInfo.Capture(exception);
            }

            if (commitFacts.InstallationStarted)
            {
                try
                {
                    registry.BeginUnloadCleanup(
                        _instance,
                        controlAppend ?? throw new InvalidOperationException("The committed Scene unload lost its prepared control."),
                        this,
                        operationFailure);
                    controlAppend = null;

                    if (commitFacts.Committed)
                    {
                        registry.CleanupInstance(_instance);
                    }
                    else
                    {
                        _instance.RetainUncommittedFailure(operationFailure
                            ?? throw new InvalidOperationException("An incomplete Scene structural installation lost its failure."));
                    }
                }
                catch (Exception exception)
                {
                    operationFailure ??= _instance.CleanupFailure ?? ExceptionDispatchInfo.Capture(exception);

                    if (controlAppend is not null)
                    {
                        _instance.RetainControlReturnFailure(controlAppend, this, operationFailure);
                        controlAppend = null;
                        _instance.RetainUncommittedFailure(operationFailure);
                        world.FaultSceneCleanupInvariant(exception);
                    }
                }

                if (operationFailure is null)
                {
                    receipt = new SceneUnloadReceipt(
                        SceneUnloadStatus.Unloaded,
                        _instance.SceneInstanceId,
                        commitFacts.CommittedRevision,
                        []);
                }
            }
        }
        finally
        {
            try
            {
                controlAppend?.Dispose();
            }
            catch (Exception exception) when (controlAppend is not null)
            {
                operationFailure ??= controlAppend.CleanupFailure ?? ExceptionDispatchInfo.Capture(exception);
                _instance.RetainControlReturnFailure(controlAppend, this, operationFailure);
                world.FaultSceneCleanupInvariant(exception);
            }
        }

        if (operationFailure is not null)
        {
            _ = _completion.TrySetException(operationFailure.SourceException);
        }
        else if (canceled)
        {
            _ = _completion.TrySetCanceled(_cancellationToken);
        }
        else
        {
            _ = _completion.TrySetResult(
                receipt ?? throw new InvalidOperationException("The Scene unload produced no terminal receipt."));
        }
    }

    internal void Reject(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _ = _completion.TrySetResult(CreateRejectedReceipt(diagnostic));
    }

    private SceneUnloadReceipt CreateRejectedReceipt(Diagnostic diagnostic)
        => new(
            SceneUnloadStatus.Rejected,
            _instance.SceneInstanceId,
            structuralRevision: null,
            [diagnostic]);

    private void PrepareControl(
        World world,
        out SceneUnloadReceipt? receipt,
        out World.WorldSceneControlAppend? controlAppend)
    {
        receipt = null;
        controlAppend = null;
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var controlMeasureStatus = ScenePersistenceControlRecordWriter.MeasureUnload(
            world,
            _instance.SceneInstanceId,
            _mode,
            limits,
            out var controlWorkspace,
            out var controlDiagnostic);

        if (controlMeasureStatus is not EbinWriteStatus.Complete)
        {
            receipt = CreateRejectedReceipt(world.CreateDiagnostic(
                "SceneUnload.ControlRecordInvalid",
                DiagnosticSeverity.Error,
                "SceneInstance.UnloadAsync",
                controlDiagnostic?.ToString() ?? "The Scene unload control could not be measured.",
                _instance.AssetId.Value));
            return;
        }

        controlAppend = world.TryPrepareSceneControlAppend(
            controlWorkspace.RecordBytes,
            controlWorkspace.WorkspaceBytes);

        if (controlAppend is null)
        {
            receipt = CreateRejectedReceipt(world.CreateDiagnostic(
                "SceneUnload.ControlCapacityExceeded",
                DiagnosticSeverity.Error,
                "SceneInstance.UnloadAsync",
                "The configured recorded-control capacity or shared World budget was reached.",
                _instance.AssetId.Value));
            return;
        }

        var controlWriteStatus = ScenePersistenceControlRecordWriter.WriteUnload(
            _instance.SceneInstanceId,
            _mode,
            controlWorkspace,
            limits,
            controlAppend.CandidateStorage,
            controlAppend.PayloadRecord,
            out var controlBytesWritten,
            out controlDiagnostic);

        if ((controlWriteStatus is not EbinWriteStatus.Complete)
            || (controlBytesWritten != controlWorkspace.RecordBytes))
        {
            receipt = CreateRejectedReceipt(world.CreateDiagnostic(
                "SceneUnload.ControlRecordInvalid",
                DiagnosticSeverity.Error,
                "SceneInstance.UnloadAsync",
                controlDiagnostic?.ToString() ?? "The Scene unload control could not be written.",
                _instance.AssetId.Value));
            return;
        }
    }
}
