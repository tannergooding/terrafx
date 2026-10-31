// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        var snapshot = options.ValidateAndFreeze();
        return PrepareCoreAsync(sceneId, snapshot, cancellationToken);
    }

    private async Task<PreparedScene> PrepareCoreAsync(
        AssetId sceneId,
        SceneLoadOptionsSnapshot options,
        CancellationToken cancellationToken)
    {
        var limits = new SceneExpansionLimits(
            options.MaximumExpandedEntities,
            options.MaximumPrefabEntities,
            options.MaximumExpandedOccurrences,
            options.MaximumPrefabDepth,
            options.MaximumHierarchyDepth,
            _world.MaximumOwnedStateBytes);
        var plan = await ScenePrefabExpansionPlanner.PrepareAsync(
            _assets,
            sceneId,
            limits,
            cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ReferenceEquals(plan.Schemas, _world.Registration.Schemas))
            {
                throw new ArgumentException(
                    "The Scene AssetStore must use the exact compiled schema registry captured by the target World.");
            }

            ValidateComponentRegistrations(plan, cancellationToken);
            ValidateRootExternalBindings(plan, options.ExternalBindings, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new PreparedScene(_world, _assets, options, plan);
        }
        catch
        {
            plan.Dispose();
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
