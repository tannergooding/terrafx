// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Stages typed hierarchy edits for one exact Scene instance.</summary>
public sealed class SceneTransaction : IDisposable
{
    private readonly Lock _gate = new();
    private readonly SceneInstance _instance;
    private readonly int _instanceEntityCount;
    private readonly uint _instanceFirstEntityIdentityOrdinal;
    private bool _isCommitted;
    private bool _isDisposed;
    private readonly int _maximumOperations;
    private readonly List<SceneTransactionOperation> _operations = [];
    private readonly SceneWorldRegistry _registry;
    private readonly Guid _sceneInstanceId;
    private readonly World _world;

    internal SceneTransaction(
        SceneInstance instance,
        World world,
        SceneWorldRegistry registry,
        Guid sceneInstanceId,
        uint firstEntityIdentityOrdinal,
        int entityCount)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        _instance = instance;
        _world = world;
        _registry = registry;
        _sceneInstanceId = sceneInstanceId;
        _instanceFirstEntityIdentityOrdinal = firstEntityIdentityOrdinal;
        _instanceEntityCount = entityCount;
        _maximumOperations = world.MaximumDeferredCommands;
    }

    /// <summary>Queues this transaction's retained intent for owner-boundary revalidation and atomic application.</summary>
    /// <param name="cancellationToken">The token that cancels the request before owner-boundary application.</param>
    /// <returns>The committed or rejected transaction receipt.</returns>
    /// <exception cref="InvalidOperationException">The transaction is disposed or has already been submitted.</exception>
    public Task<SceneTransactionReceipt> CommitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<SceneTransactionReceipt>(cancellationToken);
        }

        ImmutableArray<SceneTransactionOperation> operations;

        lock (_gate)
        {
            EnsureCanEdit();
            operations = [.. _operations];
            _isCommitted = true;
        }

        var request = new SceneTransactionRequest(
            _instance,
            _world,
            _sceneInstanceId,
            _instanceFirstEntityIdentityOrdinal,
            _instanceEntityCount,
            operations,
            cancellationToken);

        if (!_registry.TryEnqueue(request))
        {
            request.Reject(_world.CreateDiagnostic(
                "SceneTransaction.OwnerQueueFull",
                DiagnosticSeverity.Warning,
                "SceneTransaction.CommitAsync",
                "The target World owner-control queue is full.",
                _instance.AssetId.Value));
        }

        return request.Task;
    }

    /// <summary>Discards unsubmitted operations; an already queued commit retains its own immutable copy.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _isDisposed = true;
            _operations.Clear();
        }
    }

    /// <summary>Stages a reparent operation with an explicit local-pose preservation policy.</summary>
    /// <param name="child">The child Entity, which must be live in this exact Scene instance at validation and application.</param>
    /// <param name="parent">The new parent Entity in this exact World, or null to make the child a root.</param>
    /// <param name="mode">The explicit local or world pose preservation policy.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is undefined.</exception>
    /// <exception cref="InvalidOperationException">The transaction is disposed, submitted, or has reached its operation bound.</exception>
    public void Reparent(Entity child, Entity? parent, ReparentMode mode)
    {
        if (mode is not ReparentMode.KeepLocal and not ReparentMode.KeepWorld)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        lock (_gate)
        {
            EnsureCanEdit();
            EnsureOperationCapacity();
            _operations.Add(new SceneTransactionOperation(
                SceneTransactionOperationKind.Reparent,
                child,
                parent,
                mode,
                default));
        }
    }

    /// <summary>Stages a root spatial-position change.</summary>
    /// <param name="root">The target root Entity, which must be live in this exact Scene instance in the sequential transaction state.</param>
    /// <param name="position">The requested canonical spatial position.</param>
    /// <exception cref="InvalidOperationException">The transaction is disposed, submitted, or has reached its operation bound.</exception>
    public void SetRootPosition(Entity root, SpatialPosition3 position)
    {
        lock (_gate)
        {
            EnsureCanEdit();
            EnsureOperationCapacity();
            _operations.Add(new SceneTransactionOperation(
                SceneTransactionOperationKind.SetRootPosition,
                root,
                null,
                default,
                position));
        }
    }

    /// <summary>Previews the staged operations without reserving capacity or committing mutations.</summary>
    /// <returns>The validation result and the World structural revision observed by the preview.</returns>
    /// <exception cref="InvalidOperationException">The transaction is disposed or has already been submitted.</exception>
    public SceneValidationResponse Validate()
    {
        ImmutableArray<SceneTransactionOperation> operations;

        lock (_gate)
        {
            EnsureCanEdit();
            operations = [.. _operations];
        }

        var baseStructuralRevision = _world.CanonicalMutationRevision;

        if (SceneHierarchyTransactionPlanner.TryBuild(
                _world,
                _registry,
                _instance,
                _sceneInstanceId,
                _instanceFirstEntityIdentityOrdinal,
                _instanceEntityCount,
                operations,
                out var plan,
                out var failure))
        {
            if (plan is null)
            {
                throw new InvalidOperationException("The Scene transaction planner returned success without a plan.");
            }

            return new SceneValidationResponse(
                SceneValidationStatus.Valid,
                [],
                plan.BaseStructuralRevision);
        }

        return new SceneValidationResponse(
            SceneValidationStatus.Rejected,
            [
                _world.CreateDiagnostic(
                    "SceneTransaction.ValidationRejected",
                    DiagnosticSeverity.Error,
                    "SceneTransaction.Validate",
                    failure ?? "The Scene transaction was rejected.",
                    _instance.AssetId.Value),
            ],
            baseStructuralRevision);
    }

    private void EnsureCanEdit()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isCommitted)
        {
            throw new InvalidOperationException("A Scene transaction can be submitted only once.");
        }
    }

    private void EnsureOperationCapacity()
    {
        if (_operations.Count >= _maximumOperations)
        {
            throw new InvalidOperationException("The Scene transaction operation limit was reached.");
        }
    }
}
