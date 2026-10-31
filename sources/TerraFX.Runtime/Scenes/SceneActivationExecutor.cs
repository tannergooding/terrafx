// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class SceneActivationExecutor
{
    internal static SceneActivationReceipt Activate(
        World world,
        SceneWorldRegistry registry,
        SceneActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(request);
        var plan = request.Plan;
        var cancellationToken = request.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        var bindingDiagnostics = ValidateRootBindings(world, plan, request.Options);

        if (!bindingDiagnostics.IsEmpty)
        {
            return new SceneActivationReceipt(
                SceneActivationStatus.Rejected,
                instance: null,
                committedStructuralRevision: null,
                bindingDiagnostics);
        }

        if (request.Options.SceneInstanceId is Guid requestedId
            && world.IdentityLedger.TryFind(requestedId, out _))
        {
            return CreateRejectedReceipt(
                world,
                plan.SceneAssetId,
                "SceneActivation.IdentityAlreadyAssigned",
                "The requested SceneInstance identity has already been assigned.");
        }

        SceneInstanceReservation? reservation = null;

        try
        {
            if (!registry.TryReserveInstance(plan.Occurrences.Length, out reservation))
            {
                return CreateRejectedReceipt(
                    world,
                    plan.SceneAssetId,
                    "SceneActivation.RegistryCapacityExceeded",
                    "The configured Scene instance or occurrence capacity was reached.");
            }

            var admittedReservation = reservation
                ?? throw new InvalidOperationException("The Scene registry returned no reservation for an admitted instance.");

            SceneInstance? candidateInstance = null;
            World.WorldSceneEntityBatchCommit commit;

            try
            {
                commit = world.CreateSceneEntitiesAtomically(
                    plan.Entities.Length,
                    request.Options.SceneInstanceId,
                    plan.EntityIndicesByAssignmentOrdinal,
                    candidates => {
                        cancellationToken.ThrowIfCancellationRequested();
                        var descriptions = BuildEntityDescriptions(
                            world,
                            plan,
                            request.Options,
                            candidates,
                            cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        candidateInstance = new SceneInstance(world, registry, plan);
                        return descriptions;
                    },
                    parentEntityIndices: plan.EntityParentIndices,
                    cancellationToken: cancellationToken);
            }
            catch (SceneActivationRejectedException exception)
            {
                return new SceneActivationReceipt(
                    SceneActivationStatus.Rejected,
                    instance: null,
                    committedStructuralRevision: null,
                    exception.Diagnostics);
            }

            var instance = candidateInstance
                ?? throw new InvalidOperationException("Scene activation committed without preparing its SceneInstance.");
            instance.Activate(commit.SceneInstanceId, commit.FirstEntityIdentityOrdinal);
            admittedReservation.Commit(instance);
            request.TransferPlan();
            return new SceneActivationReceipt(
                SceneActivationStatus.Activated,
                instance,
                commit.StructuralRevision,
                []);
        }
        finally
        {
            reservation?.Dispose();
        }
    }

    private static bool AreDependenciesRestored(
        IComponentRegistration registration,
        ImmutableArray<SceneAuthoredComponentRecord> records,
        Dictionary<Type, ComponentInitialization> restored,
        World world)
    {
        foreach (var dependency in registration.Descriptor.DerivedDependencies)
        {
            if (restored.ContainsKey(dependency.ComponentType))
            {
                continue;
            }

            var present = false;

            foreach (var record in records)
            {
                if (world.Registration.GetComponentRegistration(record.Schema).ComponentType == dependency.ComponentType)
                {
                    present = true;
                    break;
                }
            }

            if (!present)
            {
                throw new InvalidDataException("A Scene Derived component omits one of its registered same-Entity dependencies.");
            }

            return false;
        }

        return true;
    }

    private static ImmutableArray<EntityDescription> BuildEntityDescriptions(
        World world,
        SceneExpansionPlan plan,
        SceneLoadOptionsSnapshot options,
        ImmutableArray<Entity> candidateEntities,
        CancellationToken cancellationToken)
    {
        var expandedEntities = plan.Entities;
        var descriptions = ImmutableArray.CreateBuilder<EntityDescription>(expandedEntities.Length);

        for (var expandedEntityIndex = 0; expandedEntityIndex < expandedEntities.Length; expandedEntityIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expandedEntity = expandedEntities[expandedEntityIndex];
            var records = expandedEntity.Components;
            var restored = new Dictionary<Type, ComponentInitialization>(records.Length);
            var pendingRecords = new List<SceneAuthoredComponentRecord>(records.Length);

            foreach (var record in records)
            {
                pendingRecords.Add(record);
            }

            var components = ImmutableArray.CreateBuilder<ComponentInitialization>(records.Length);

            while (pendingRecords.Count != 0)
            {
                var progress = false;

                for (var recordIndex = 0; recordIndex < pendingRecords.Count; recordIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var record = pendingRecords[recordIndex];
                    var registration = world.Registration.GetComponentRegistration(record.Schema);

                    if (!AreDependenciesRestored(registration, records, restored, world))
                    {
                        continue;
                    }

                    var context = new SceneComponentRestoreContext(
                        world,
                        plan,
                        options,
                        candidateEntities,
                        expandedEntityIndex,
                        record.Schema,
                        restored);
                    ComponentRecordRestoreResult result;

                    try
                    {
                        result = registration.RestoreFromRecord(
                            record.EncodedRecord,
                            context,
                            cancellationToken);
                    }
                    finally
                    {
                        context.Revoke();
                    }

                    if (result.Status is ComponentReadStatus.Rejected)
                    {
                        throw new SceneActivationRejectedException(result.Diagnostics);
                    }

                    if (!result.TryGetInitialization(out var initialization)
                        || (initialization is null))
                    {
                        throw new InvalidOperationException("A successful Scene component restore did not provide an initialization.");
                    }

                    if (!restored.TryAdd(registration.ComponentType, initialization))
                    {
                        throw new InvalidDataException("A Scene Entity repeats a registered component type.");
                    }

                    components.Add(initialization);
                    pendingRecords.RemoveAt(recordIndex);
                    progress = true;
                    break;
                }

                if (!progress)
                {
                    throw new InvalidDataException("Scene component restoration has a missing or cyclic Derived dependency.");
                }
            }

            descriptions.Add(new EntityDescription(components: components.MoveToImmutable()));
        }

        return descriptions.MoveToImmutable();
    }

    private static SceneActivationReceipt CreateRejectedReceipt(
        World world,
        AssetId assetId,
        string code,
        string message)
        => new(
            SceneActivationStatus.Rejected,
            instance: null,
            committedStructuralRevision: null,
            [world.CreateDiagnostic(
                code,
                DiagnosticSeverity.Warning,
                "SceneLoader.ActivateAsync",
                message,
                assetId.Value)]);

    private static ImmutableArray<Diagnostic> ValidateRootBindings(
        World world,
        SceneExpansionPlan plan,
        SceneLoadOptionsSnapshot options)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var binding in options.ExternalBindings)
        {
            if (binding.Target is not Entity target)
            {
                continue;
            }

            try
            {
                world.ValidateEntityReference(target, excludedEntity: null, parameterName: null);
            }
            catch (InvalidOperationException)
            {
                diagnostics.Add(world.CreateDiagnostic(
                    "SceneActivation.ExternalTargetNotLive",
                    DiagnosticSeverity.Warning,
                    "SceneLoader.ActivateAsync",
                    "A supplied root Scene slot target is not a live Entity in the exact target World.",
                    plan.SceneAssetId.Value));
                continue;
            }

            foreach (var slot in plan.RootExternalSlots)
            {
                if (slot.SlotId != binding.SlotId)
                {
                    continue;
                }

                foreach (var requiredComponent in slot.RequiredComponents)
                {
                    if (world.HasComponentFromScene(target, requiredComponent))
                    {
                        continue;
                    }

                    diagnostics.Add(world.CreateDiagnostic(
                        "SceneActivation.ExternalTargetComponentMissing",
                        DiagnosticSeverity.Warning,
                        "SceneLoader.ActivateAsync",
                        "A supplied root Scene slot target lacks a required registered component.",
                        plan.SceneAssetId.Value));
                }

                break;
            }
        }

        return diagnostics.ToImmutable();
    }
}
