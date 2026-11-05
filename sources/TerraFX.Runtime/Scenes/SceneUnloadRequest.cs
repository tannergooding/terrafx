// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
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
            Reject(world.CreateDiagnostic(
                "SceneUnload.ControlRecordInvalid",
                DiagnosticSeverity.Error,
                "SceneInstance.UnloadAsync",
                controlDiagnostic?.ToString() ?? "The Scene unload control could not be measured.",
                _instance.AssetId.Value));
            return;
        }

        using var controlAppend = world.TryPrepareSceneControlAppend(
            controlWorkspace.RecordBytes,
            controlWorkspace.WorkspaceBytes);

        if (controlAppend is null)
        {
            Reject(world.CreateDiagnostic(
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
            Reject(world.CreateDiagnostic(
                "SceneUnload.ControlRecordInvalid",
                DiagnosticSeverity.Error,
                "SceneInstance.UnloadAsync",
                controlDiagnostic?.ToString() ?? "The Scene unload control could not be written.",
                _instance.AssetId.Value));
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
            controlAppend.Commit();
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
