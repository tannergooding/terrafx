// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using TerraFX;

namespace TerraFX.Scenes;

internal static class ScenePrefabSlotBindingResolver
{
    internal static ImmutableArray<ScenePlacementSlotBinding> Resolve(
        ScenePrefabPlacement placement,
        ScenePrefabAsset childPrefab,
        ImmutableArray<SceneExternalSlot> containingExternalSlots,
        Func<SceneEntitySelector, ImmutableArray<ContentSchemaReference>?> resolveInternalTargetSchemas,
        bool allowInstanceAbsoluteInternalTargets)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(childPrefab);
        ArgumentNullException.ThrowIfNull(resolveInternalTargetSchemas);

        if (containingExternalSlots.IsDefault)
        {
            throw new ArgumentException("The containing external-slot set must be initialized.", nameof(containingExternalSlots));
        }

        if (placement.PrefabAssetId != childPrefab.AssetId)
        {
            throw new InvalidDataException("The loaded child Prefab does not match the placement AssetId.");
        }

        ValidateSlotOrder(childPrefab.ExternalSlots, nameof(childPrefab));
        ValidateSlotOrder(containingExternalSlots, nameof(containingExternalSlots));
        ValidateBindingOrder(placement.SlotBindings);

        var resolvedBindings = ImmutableArray.CreateBuilder<ScenePlacementSlotBinding>(childPrefab.ExternalSlots.Length);
        var bindingIndex = 0;

        foreach (var childSlot in childPrefab.ExternalSlots)
        {
            if ((bindingIndex < placement.SlotBindings.Length)
                && (CompareRfc(placement.SlotBindings[bindingIndex].SlotId, childSlot.SlotId) < 0))
            {
                throw new InvalidDataException("A placement binds an external slot that the child Prefab does not declare.");
            }

            var hasBinding = (bindingIndex < placement.SlotBindings.Length)
                && (placement.SlotBindings[bindingIndex].SlotId == childSlot.SlotId);
            SceneReferenceTarget? target = null;

            if (hasBinding)
            {
                target = placement.SlotBindings[bindingIndex].Target;
                bindingIndex++;

                if (target is null)
                {
                    if (childSlot.Required || !childSlot.AllowNull)
                    {
                        throw new InvalidDataException("A placement supplies null for a required or non-null child external slot.");
                    }
                }
                else if (target.Kind == SceneReferenceTargetKind.Internal)
                {
                    var selector = target.Entity
                        ?? throw new InvalidDataException("An Internal slot binding has no EntitySelector.");

                    if (selector.Scope is not SceneEntitySelectorScope.OccurrenceRelative
                        and not SceneEntitySelectorScope.InstanceAbsolute)
                    {
                        throw new InvalidDataException("An Internal slot binding has an undefined selector scope.");
                    }

                    if ((selector.Scope == SceneEntitySelectorScope.InstanceAbsolute)
                        && !allowInstanceAbsoluteInternalTargets)
                    {
                        throw new InvalidDataException("A Prefab cannot bind a child external slot to an InstanceAbsolute entity.");
                    }

                    if (selector.PlacementPath.IsDefault
                        || (selector.PlacementPath.Length > 32)
                        || (selector.AuthoredEntityId == Guid.Empty))
                    {
                        throw new InvalidDataException("An Internal slot binding has an invalid EntitySelector.");
                    }

                    var targetSchemas = resolveInternalTargetSchemas(selector);

                    if (targetSchemas is not { } schemas || schemas.IsDefault)
                    {
                        throw new InvalidDataException("An Internal slot binding does not resolve to an entity in the containing occurrence.");
                    }

                    ValidateRequiredComponents(childSlot, schemas);
                }
                else if (target.Kind == SceneReferenceTargetKind.ExternalSlot)
                {
                    var externalSlotId = target.ExternalSlotId
                        ?? throw new InvalidDataException("An ExternalSlot binding has no ExternalSlotId.");
                    var containingSlot = FindSlot(containingExternalSlots, externalSlotId)
                        ?? throw new InvalidDataException("A placement forwards a child slot to an undeclared containing external slot.");
                    ValidateForwardedSlot(childSlot, containingSlot);
                }
                else
                {
                    throw new InvalidDataException("A placement slot binding has an undefined target kind.");
                }
            }
            else if (childSlot.Required)
            {
                throw new InvalidDataException("A placement does not bind a required child external slot.");
            }

            resolvedBindings.Add(new ScenePlacementSlotBinding(childSlot.SlotId, target));
        }

        if (bindingIndex != placement.SlotBindings.Length)
        {
            throw new InvalidDataException("A placement binds an external slot that the child Prefab does not declare.");
        }

        return resolvedBindings.MoveToImmutable();
    }

    private static int CompareRfc(Guid left, Guid right)
        => FoundationCatalogue.CompareRfc(left, right);

    private static SceneExternalSlot? FindSlot(ImmutableArray<SceneExternalSlot> slots, Guid slotId)
    {
        var low = 0;
        var high = slots.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = slots[middle];
            var comparison = CompareRfc(candidate.SlotId, slotId);

            if (comparison == 0)
            {
                return candidate;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return null;
    }

    private static void ValidateBindingOrder(ImmutableArray<ScenePlacementSlotBinding> bindings)
    {
        if (bindings.IsDefault)
        {
            throw new ArgumentException("The placement slot-binding array must be initialized.", nameof(bindings));
        }

        for (var index = 1; index < bindings.Length; index++)
        {
            if (CompareRfc(bindings[index - 1].SlotId, bindings[index].SlotId) >= 0)
            {
                throw new InvalidDataException("Placement slot bindings are not uniquely sorted by RFC SlotId.");
            }
        }
    }

    private static void ValidateForwardedSlot(SceneExternalSlot childSlot, SceneExternalSlot containingSlot)
    {
        if (childSlot.Required && !containingSlot.Required)
        {
            throw new InvalidDataException("A required child external slot cannot be forwarded through an optional containing slot.");
        }

        if (!childSlot.AllowNull && !containingSlot.Required && containingSlot.AllowNull)
        {
            throw new InvalidDataException("A non-null child external slot cannot be forwarded through a nullable containing slot.");
        }

        ValidateRequiredComponents(childSlot, containingSlot.RequiredComponents);
    }

    private static void ValidateRequiredComponents(
        SceneExternalSlot childSlot,
        ImmutableArray<ContentSchemaReference> targetSchemas)
    {
        for (var requiredIndex = 0; requiredIndex < childSlot.RequiredComponents.Length; requiredIndex++)
        {
            var requiredSchema = childSlot.RequiredComponents[requiredIndex];
            var found = false;

            for (var targetIndex = 0; targetIndex < targetSchemas.Length; targetIndex++)
            {
                if (targetSchemas[targetIndex] == requiredSchema)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidDataException($"The bound target does not satisfy child external slot {childSlot.SlotId:N}.");
            }
        }
    }

    private static void ValidateSlotOrder(ImmutableArray<SceneExternalSlot> slots, string parameterName)
    {
        if (slots.IsDefault)
        {
            throw new ArgumentException("The external-slot set must be initialized.", parameterName);
        }

        for (var index = 1; index < slots.Length; index++)
        {
            if (CompareRfc(slots[index - 1].SlotId, slots[index].SlotId) >= 0)
            {
                throw new InvalidDataException("External slots are not uniquely sorted by RFC SlotId.");
            }
        }
    }
}
