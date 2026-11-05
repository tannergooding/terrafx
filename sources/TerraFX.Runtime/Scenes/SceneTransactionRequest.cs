// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal sealed class SceneTransactionRequest : SceneOwnerRequest
{
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource<SceneTransactionReceipt> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _entityCount;
    private readonly uint _firstEntityIdentityOrdinal;
    private readonly SceneInstance _instance;
    private readonly ImmutableArray<SceneTransactionOperation> _operations;
    private readonly Guid _sceneInstanceId;
    private readonly World _world;

    internal SceneTransactionRequest(
        SceneInstance instance,
        World world,
        Guid sceneInstanceId,
        uint firstEntityIdentityOrdinal,
        int entityCount,
        ImmutableArray<SceneTransactionOperation> operations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);

        if (operations.IsDefault)
        {
            throw new ArgumentException("The Scene transaction operation array must be initialized.", nameof(operations));
        }

        _instance = instance;
        _world = world;
        _sceneInstanceId = sceneInstanceId;
        _firstEntityIdentityOrdinal = firstEntityIdentityOrdinal;
        _entityCount = entityCount;
        _operations = operations;
        _cancellationToken = cancellationToken;
    }

    internal Task<SceneTransactionReceipt> Task => _completion.Task;

    internal override void CancelFromRegistryDisposal()
        => _ = _completion.TrySetException(new ObjectDisposedException(nameof(SceneWorldRegistry)));

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
            _ = _completion.TrySetCanceled(_cancellationToken);
            return;
        }

        World.WorldSceneControlAppend? controlAppend = null;

        try
        {
            if (!registry.ContainsInstance(_instance)
                || (_instance.SceneInstanceId != _sceneInstanceId))
            {
                Reject(
                    "SceneTransaction.InstanceUnavailable",
                    "The transaction's exact Scene instance is no longer active in this World.");
                return;
            }

            if (!SceneHierarchyTransactionPlanner.TryBuild(
                    world,
                    registry,
                    _instance,
                    _sceneInstanceId,
                    _firstEntityIdentityOrdinal,
                    _entityCount,
                    _operations,
                    out var plan,
                    out var failure))
            {
                Reject(
                    "SceneTransaction.ValidationRejected",
                    failure ?? "The Scene transaction was rejected at the owner boundary.");
                return;
            }

            if (plan is null)
            {
                throw new InvalidOperationException("The Scene transaction planner returned success without a plan.");
            }

            if (!_operations.IsEmpty)
            {
                var limits = WorldPersistenceEbinLimits.Create(world.Options);
                var controlMeasureStatus = ScenePersistenceControlRecordWriter.MeasureEdit(
                    world,
                    _sceneInstanceId,
                    _operations,
                    limits,
                    out var controlWorkspace,
                    out var controlDiagnostic);

                if (controlMeasureStatus is not EbinWriteStatus.Complete)
                {
                    Reject(
                        "SceneTransaction.ControlRecordInvalid",
                        controlDiagnostic?.ToString() ?? "The Scene transaction control could not be measured.");
                    return;
                }

                controlAppend = world.TryPrepareSceneControlAppend(
                    controlWorkspace.RecordBytes,
                    controlWorkspace.WorkspaceBytes);

                if (controlAppend is null)
                {
                    Reject(
                        "SceneTransaction.ControlCapacityExceeded",
                        "The configured recorded-control capacity or shared World budget was reached.");
                    return;
                }

                var controlWriteStatus = ScenePersistenceControlRecordWriter.WriteEdit(
                    world,
                    _sceneInstanceId,
                    _operations,
                    controlWorkspace,
                    limits,
                    controlAppend.CandidateStorage,
                    controlAppend.PayloadRecord,
                    out var controlBytesWritten,
                    out controlDiagnostic);

                if ((controlWriteStatus is not EbinWriteStatus.Complete)
                    || (controlBytesWritten != controlWorkspace.RecordBytes))
                {
                    Reject(
                        "SceneTransaction.ControlRecordInvalid",
                        controlDiagnostic?.ToString() ?? "The Scene transaction control could not be written.");
                    return;
                }
            }

            var revision = world.ApplySceneHierarchyTransaction(
                registry,
                _sceneInstanceId,
                _firstEntityIdentityOrdinal,
                _entityCount,
                plan.ComponentMutations,
                plan.ParentMutations,
                _cancellationToken);
            controlAppend?.Commit();
            _ = _completion.TrySetResult(new SceneTransactionReceipt(
                SceneTransactionStatus.Committed,
                revision,
                []));
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _ = _completion.TrySetCanceled(_cancellationToken);
        }
        catch (ObjectDisposedException exception)
        {
            _ = _completion.TrySetException(exception);
        }
        catch (ArgumentException exception)
        {
            Reject("SceneTransaction.ApplyRejected", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            Reject("SceneTransaction.ApplyRejected", exception.Message);
        }
        catch (OverflowException exception)
        {
            Reject("SceneTransaction.ApplyRejected", exception.Message);
        }
        catch (Exception exception)
        {
            _ = _completion.TrySetException(exception);
        }
        finally
        {
            controlAppend?.Dispose();
        }
    }

    internal void Reject(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _ = _completion.TrySetResult(new SceneTransactionReceipt(
            SceneTransactionStatus.Rejected,
            committedStructuralRevision: null,
            [diagnostic]));
    }

    private void Reject(string code, string message)
    {
        Reject(_world.CreateDiagnostic(
            code,
            DiagnosticSeverity.Error,
            "SceneTransaction.CommitAsync",
            message,
            _instance.AssetId.Value));
    }
}
