// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

/// <summary>Prepares Scene assets for one exact World and AssetStore binding.</summary>
public sealed class SceneLoader
{
    private readonly AssetStore _assets;
    private readonly SceneWorldRegistry _registry;
    private readonly World _world;

    /// <summary>Initializes a Scene loader for one exact target World and AssetStore.</summary>
    /// <param name="world">The World that will own any later Scene activation.</param>
    /// <param name="assets">The selected package store used for Scene and Prefab preparation.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    public SceneLoader(World world, AssetStore assets)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(assets);
        var support = world.Registration.SceneSupport
            ?? throw new InvalidOperationException("The target World was not constructed with Scene support.");
        _registry = support.GetParticipant(world) as SceneWorldRegistry
            ?? throw new InvalidOperationException("The target World has an incompatible Scene participant.");
        _registry.EnsureBoundTo(world);
        _world = world;
        _assets = assets;
    }

    /// <summary>Queues one prepared Scene for atomic activation at the target World's next owner boundary.</summary>
    /// <param name="preparedScene">The preparation bound to this exact World and AssetStore.</param>
    /// <param name="cancellationToken">The token that cancels work before owner-boundary application.</param>
    /// <returns>The activation receipt, or a faulted/canceled task for an unexpected failure or cancellation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="preparedScene"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="preparedScene"/> belongs to another World.</exception>
    /// <exception cref="InvalidOperationException">The prepared Scene has already been admitted for activation.</exception>
    public Task<SceneActivationReceipt> ActivateAsync(
        PreparedScene preparedScene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparedScene);
        var access = preparedScene.CaptureActivationHeader(_world, _assets, out var activationWorkspaceLimit);
        PreparedScene.ThrowForPreparationAccess(access);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<SceneActivationReceipt>(cancellationToken);
        }

        var activationUse = _world.AcquireGuidUse(activationWorkspaceLimit);
        var activationControl = activationUse.Control;
        var bootstrapWorkspaceReserved = false;
        var completionWorkspaceReserved = false;
        SceneActivationRequest request;

        try
        {
            if (!activationControl.TryReserveWorkspace(SceneActivationRequest.ActivationBootstrapWorkspaceBytes))
            {
                throw new InvalidOperationException("The activation use could not reserve its request and task bootstrap.");
            }

            bootstrapWorkspaceReserved = true;

            if (!activationControl.TryReserveWorkspace(SceneActivationRequest.ActivationCompletionWorkspaceBytes))
            {
                throw new InvalidOperationException("The activation use could not reserve its terminal completion workspace.");
            }

            completionWorkspaceReserved = true;
            request = new SceneActivationRequest(
                preparedScene,
                _world,
                _assets,
                activationWorkspaceLimit,
                activationUse,
                cancellationToken);
        }
        catch
        {
            var workspaceReleaseFailed = false;

            if (completionWorkspaceReserved
                && !activationControl.TryReleaseWorkspace(SceneActivationRequest.ActivationCompletionWorkspaceBytes))
            {
                workspaceReleaseFailed = true;
            }

            if (bootstrapWorkspaceReserved
                && !activationControl.TryReleaseWorkspace(SceneActivationRequest.ActivationBootstrapWorkspaceBytes))
            {
                workspaceReleaseFailed = true;
            }

            if (workspaceReleaseFailed)
            {
                activationControl.Quarantine(activationControl, failureStage: 3, failureCategory: 4);
                throw new InvalidOperationException("The failed activation request could not return its workspace claims.");
            }

            activationUse.Dispose();
            throw;
        }

        var task = request.Task;

        try
        {
            var enqueueStatus = _registry.EnqueueActivation(request);

            if (enqueueStatus is SceneActivationEnqueueStatus.QueueFull)
            {
                request.Reject(_world.CreateDiagnostic(
                    "SceneActivation.OwnerQueueFull",
                    DiagnosticSeverity.Warning,
                    "SceneLoader.ActivateAsync",
                    "The target World owner-control queue is full.",
                    preparedScene.AssetId.Value));
            }
            else if (enqueueStatus is SceneActivationEnqueueStatus.RegistryDisposed)
            {
                throw new ObjectDisposedException(nameof(SceneWorldRegistry));
            }
            else
            {
                PreparedScene.ThrowForPreparationAccess(enqueueStatus switch {
                    SceneActivationEnqueueStatus.Queued => ScenePreparationAccess.Available,
                    SceneActivationEnqueueStatus.PreparedNotAvailable => ScenePreparationAccess.NotAvailable,
                    SceneActivationEnqueueStatus.AlreadyAdmitted => ScenePreparationAccess.AlreadyAdmitted,
                    SceneActivationEnqueueStatus.WrongWorld => ScenePreparationAccess.WrongWorld,
                    SceneActivationEnqueueStatus.WrongAssetStore => ScenePreparationAccess.WrongAssetStore,
                    _ => ScenePreparationAccess.PolicyMismatch,
                });
            }
        }
        catch
        {
            request.CancelFromRegistryDisposal();
            throw;
        }

        return task;
    }

    /// <summary>Loads and validates a Scene and its recursively selected Prefabs.</summary>
    /// <param name="sceneId">The positive AssetId of the root Scene.</param>
    /// <param name="options">The immutable Scene preparation limits and root external bindings.</param>
    /// <param name="cancellationToken">The token that cancels admitted asset reads and bounded expansion.</param>
    /// <returns>A prepared Scene retaining its exact package-backed asset leases.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sceneId"/> or a selected expansion limit is invalid.</exception>
    /// <exception cref="ArgumentException">The World and selected assets use different compiled schema registries.</exception>
    /// <exception cref="InvalidDataException">Root external bindings do not match the Scene slot declarations.</exception>
    /// <exception cref="InvalidOperationException">The selected expansion exceeds its bounded World-owned memory budget.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The anchored preparation owns both roles; unescaped freeze failure aborts it and success transfers its body through the captured Task.")]
    public Task<PreparedScene> PrepareAsync(
        AssetId sceneId,
        SceneLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!sceneId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneId));
        }

        options.ValidateShape();

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<PreparedScene>(cancellationToken);
        }

        var maximumPreparationBytes = options.MaximumPreparationBytes;
        var preparation = PreparedScene.CreatePreparation(_world, _assets, sceneId, maximumPreparationBytes);
        var bindingArrayBytes = options.ExternalBindings.IsEmpty
            ? 0
            : WorldAdmissionDomain.ArrayCharge(options.ExternalBindings.Length, IntPtr.Size);

        try
        {
            preparation.FreezeOptions(options, bindingArrayBytes);
        }
        catch
        {
            preparation.AbortPreparationEntry();
            throw;
        }

        var task = preparation.PreparationTask;
        preparation.StartPreparation(this, cancellationToken);
        return task;
    }

    internal Task<SceneExpansionPlan> PrepareRestorePlanAsync(
        AssetPackageLease packageLease,
        AssetId sceneId,
        SceneLoadOptionsSnapshot options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageLease);

        if (!sceneId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneId));
        }

        if (_world.GetCpuExtractionParticipant<ISceneExpansionResourcePreparer>() is not null)
        {
            throw new NotSupportedException(
                "Scene restore with CPU extraction is unavailable until resource measurement and producer ownership are fully admitted.");
        }

        return PreparePlanCoreAsync(
            sceneId,
            options,
            packageLease,
            useRestoredStorage: true,
            useControl: null,
            cancellationToken: cancellationToken);
    }

    internal Task<SceneExpansionPlan> StartPlanPreparation(
        AssetId sceneId,
        SceneLoadOptionsSnapshot options,
        PreparedScene preparationOwner,
        CancellationToken cancellationToken)
    {
        var useControl = preparationOwner.PreparationControl;
        var limits = new SceneExpansionLimits(
            options.MaximumExpandedEntities,
            options.MaximumPrefabEntities,
            options.MaximumExpandedOccurrences,
            options.MaximumPrefabDepth,
            options.MaximumHierarchyDepth,
            options.MaximumPreparationBytes,
            rootExternalBindingCount: options.ExternalBindings.Length,
            reservationSource: SceneExpansionReservationSource.ForWorld(
                _world,
                _registry,
                useControl,
                useRestoredStorage: false));
        return ScenePrefabExpansionPlanner.PrepareAsync(
            _assets,
            sceneId,
            preparationOwner,
            limits,
            cancellationToken);
    }

    internal Task<IDisposable?> StartResourcePreparation(
        ISceneExpansionResourcePreparer preparer,
        SceneExpansionPlan plan,
        PreparedScene preparationOwner,
        CancellationToken cancellationToken)
        => preparer.PrepareOwnedAsync(
            plan,
            new ScenePreparationFunding(preparationOwner, _registry),
            cancellationToken);

    internal ISceneExpansionResourcePreparer? ValidatePreparedPlan(
        SceneExpansionPlan plan,
        SceneLoadOptionsSnapshot options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _registry.EnsureBoundTo(_world);

        if (!ReferenceEquals(plan.Schemas, _world.Registration.Schemas))
        {
            throw new ArgumentException(
                "The Scene AssetStore must use the exact compiled schema registry captured by the target World.");
        }

        ValidateComponentRegistrations(plan, cancellationToken);
        ValidateRootExternalBindings(plan, options.ExternalBindings, cancellationToken);
        return _world.GetCpuExtractionParticipant<ISceneExpansionResourcePreparer>();
    }

    private async Task<SceneExpansionPlan> PreparePlanCoreAsync(
        AssetId sceneId,
        SceneLoadOptionsSnapshot options,
        AssetPackageLease? packageLease,
        bool useRestoredStorage,
        WorldGuidUseControl? useControl,
        CancellationToken cancellationToken)
    {
        var limits = new SceneExpansionLimits(
            options.MaximumExpandedEntities,
            options.MaximumPrefabEntities,
            options.MaximumExpandedOccurrences,
            options.MaximumPrefabDepth,
            options.MaximumHierarchyDepth,
            (useControl is null) ? _world.MaximumOwnedStateBytes : options.MaximumPreparationBytes,
            rootExternalBindingCount: options.ExternalBindings.Length,
            reservationSource: SceneExpansionReservationSource.ForWorld(
                _world,
                _registry,
                useControl,
                useRestoredStorage));
        var plan = (packageLease is null)
            ? await ScenePrefabExpansionPlanner.PrepareAsync(
                _assets,
                sceneId,
                limits,
                useControl,
                cancellationToken).ConfigureAwait(false)
            : await ScenePrefabExpansionPlanner.PrepareAsync(
                packageLease,
                sceneId,
                limits,
                cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _registry.EnsureBoundTo(_world);

            if (!ReferenceEquals(plan.Schemas, _world.Registration.Schemas))
            {
                throw new ArgumentException(
                    "The Scene AssetStore must use the exact compiled schema registry captured by the target World.");
            }

            ValidateComponentRegistrations(plan, cancellationToken);
            ValidateRootExternalBindings(plan, options.ExternalBindings, cancellationToken);

            var resourcePreparer = _world.GetCpuExtractionParticipant<ISceneExpansionResourcePreparer>();

            if (resourcePreparer is not null)
            {
                WorldSceneStorageReservation? ReserveStorage(long bytes)
                {
                    return limits.ReserveStorage(bytes);
                }

                WorldSceneStorageReservation? ReserveWorkspace(long bytes)
                {
                    return limits.HasWorkspaceReservation
                        ? limits.ReserveWorkspace(bytes)
                        : limits.ReserveStorage(bytes);
                }

                var preparedAssets = await resourcePreparer.PrepareAsync(
                    plan,
                    ReserveStorage,
                    ReserveWorkspace,
                    cancellationToken).ConfigureAwait(false);

                if (preparedAssets is not null)
                {
                    plan.AttachPreparedAssets(preparedAssets);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return plan;
        }
        catch
        {
            try
            {
                plan.Dispose();
            }
            catch
            {
                useControl?.Quarantine(plan, failureStage: 2, failureCategory: 2);
                throw;
            }

            throw;
        }
    }

    private void ValidateComponentRegistrations(
        SceneExpansionPlan plan,
        CancellationToken cancellationToken)
    {
        var schemas = new HashSet<ContentSchemaReference>();

        foreach (var entity in plan.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var component in entity.Components)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!schemas.Add(component.Schema))
                {
                    continue;
                }

                try
                {
                    _ = _world.Registration.GetComponentRegistration(component.Schema);
                }
                catch (KeyNotFoundException exception)
                {
                    throw new InvalidDataException(
                        "A Scene entity contains a component schema that is not registered in the target World.",
                        exception);
                }
            }
        }
    }

    private void ValidateRequiredComponents(
        SceneExternalSlot slot,
        CancellationToken cancellationToken)
    {
        foreach (var schema in slot.RequiredComponents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _ = _world.Registration.GetComponentRegistration(schema);
            }
            catch (KeyNotFoundException exception)
            {
                throw new InvalidDataException(
                    "A root Scene external slot requires a component that is not registered in the target World.",
                    exception);
            }
        }
    }

    private void ValidateRootExternalBindings(
        SceneExpansionPlan plan,
        ImmutableArray<SceneExternalBinding> bindings,
        CancellationToken cancellationToken)
    {
        var slots = plan.RootExternalSlots;
        var slotIndex = 0;
        var bindingIndex = 0;

        while ((slotIndex < slots.Length) && (bindingIndex < bindings.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slot = slots[slotIndex];
            var binding = bindings[bindingIndex];
            var comparison = FoundationCatalogue.CompareRfc(slot.SlotId, binding.SlotId);

            if (comparison < 0)
            {
                if (slot.Required)
                {
                    throw new InvalidDataException("A required root Scene external slot has no supplied binding.");
                }

                slotIndex++;
                continue;
            }

            if (comparison > 0)
            {
                throw new InvalidDataException("A supplied root Scene external binding names an unknown slot.");
            }

            if ((binding.Target is null) && !slot.AllowNull)
            {
                throw new InvalidDataException("A root Scene external slot does not admit an explicit null binding.");
            }

            if (binding.Target is not null)
            {
                ValidateRequiredComponents(slot, cancellationToken);
            }

            slotIndex++;
            bindingIndex++;
        }

        while (slotIndex < slots.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (slots[slotIndex].Required)
            {
                throw new InvalidDataException("A required root Scene external slot has no supplied binding.");
            }

            slotIndex++;
        }

        if (bindingIndex != bindings.Length)
        {
            throw new InvalidDataException("A supplied root Scene external binding names an unknown slot.");
        }
    }
}
