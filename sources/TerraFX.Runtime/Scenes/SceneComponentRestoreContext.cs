// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal sealed class SceneComponentRestoreContext : IComponentRestoreContext
{
    private readonly ImmutableArray<Entity> _candidateEntities;
    private readonly ContentSchemaReference _componentSchema;
    private readonly int _expandedEntityIndex;
    private Exception? _failure;
    private bool _isActive = true;
    private SceneLoadOptionsSnapshot _options;
    private readonly int _ownerThreadId;
    private readonly SceneExpansionPlan _plan;
    private readonly IReadOnlyDictionary<Type, ComponentInitialization> _sameEntityComponents;
    private readonly World _world;

    internal SceneComponentRestoreContext(
        World world,
        SceneExpansionPlan plan,
        SceneLoadOptionsSnapshot options,
        ImmutableArray<Entity> candidateEntities,
        int expandedEntityIndex,
        ContentSchemaReference componentSchema,
        IReadOnlyDictionary<Type, ComponentInitialization> sameEntityComponents)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sameEntityComponents);

        if (candidateEntities.IsDefault || (candidateEntities.Length != plan.Entities.Length))
        {
            throw new ArgumentException("The candidate Entity map must match the exact expanded Scene.", nameof(candidateEntities));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(expandedEntityIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(expandedEntityIndex, candidateEntities.Length);
        ArgumentOutOfRangeException.ThrowIfEqual(componentSchema.SchemaId, Guid.Empty);
        _world = world;
        _plan = plan;
        _options = options;
        _candidateEntities = candidateEntities;
        _expandedEntityIndex = expandedEntityIndex;
        _componentSchema = componentSchema;
        _sameEntityComponents = sameEntityComponents;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public Exception? Failure => Volatile.Read(ref _failure);

    public bool IsActive => Volatile.Read(ref _isActive);

    public int OwnerThreadId => _ownerThreadId;

    private static bool PathMatches(
        ImmutableArray<ScenePropertyPathSegment> scenePath,
        ComponentFieldPath componentPath)
    {
        var componentSegments = componentPath.Segments;

        if (scenePath.Length != componentSegments.Length)
        {
            return false;
        }

        for (var index = 0; index < scenePath.Length; index++)
        {
            var sceneSegment = scenePath[index];
            var componentSegment = componentSegments[index];
            var expectedKind = sceneSegment.Kind switch {
                ScenePropertyPathSegmentKind.FieldTag => ComponentPropertySegmentKind.FieldTag,
                ScenePropertyPathSegmentKind.FixedArrayIndex => ComponentPropertySegmentKind.FixedArrayIndex,
                _ => throw new InvalidDataException("The Scene reference path contains an unknown segment kind."),
            };

            if ((componentSegment.Kind != expectedKind) || (componentSegment.Value != sceneSegment.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TargetMatches(SceneReferenceTarget target, Guid targetEntityId)
    {
        return target.Kind switch {
            SceneReferenceTargetKind.Internal => target.Entity?.AuthoredEntityId == targetEntityId,
            SceneReferenceTargetKind.ExternalSlot => target.ExternalSlotId == targetEntityId,
            _ => throw new InvalidDataException("The Scene reference binding has an unknown target kind."),
        };
    }

    public TDependency ReadSameEntity<TDependency>()
        where TDependency : unmanaged
    {
        EnsureActive();

        if (_sameEntityComponents.TryGetValue(typeof(TDependency), out var initialization)
            && initialization.TryGetValue<TDependency>(out var value))
        {
            return value;
        }

        return Fail<TDependency>(new InvalidOperationException("The exact Scene Entity restore dependency has not been restored."));
    }

    public void RecordFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _ = Interlocked.CompareExchange(ref _failure, exception, null);
    }

    public Entity ResolveEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
    {
        EnsureActive();

        if (referenceField is null)
        {
            return Fail<Entity>(new ArgumentNullException(nameof(referenceField)));
        }

        ValidateComponentField(referenceField);
        var target = ResolveTarget(FindReference(referenceField, targetEntityId));

        if (target is null)
        {
            return Fail<Entity>(new InvalidDataException("A required Scene Entity reference resolved to null."));
        }

        return target.Value;
    }

    public Entity ResolveEntity(WorldSystemFieldDescriptor referenceField, Guid targetEntityId)
    {
        EnsureActive();
        return Fail<Entity>(new ArgumentException("A Scene component restore cannot resolve a World system field.", nameof(referenceField)));
    }

    public Entity? ResolveOptionalEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
    {
        EnsureActive();

        if (referenceField is null)
        {
            return Fail<Entity?>(new ArgumentNullException(nameof(referenceField)));
        }

        ValidateComponentField(referenceField);
        return ResolveTarget(FindReference(referenceField, targetEntityId));
    }

    public void ThrowIfFailed()
    {
        if (Failure is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal void Revoke()
    {
        Volatile.Write(ref _isActive, false);
        _options = default;
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("The Scene component restore context has expired.");
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            ThrowActiveMisuse(new InvalidOperationException("The Scene component restore context is restricted to its issuing thread."));
        }
    }

    private T Fail<T>(Exception exception)
    {
        ThrowActiveMisuse(exception);
        throw new InvalidOperationException("The Scene restore context returned from a sticky failure.");
    }

    private SceneExpandedReferenceBinding FindReference(ComponentFieldDescriptor field, Guid targetEntityId)
    {
        if (targetEntityId == Guid.Empty)
        {
            ThrowActiveMisuse(new ArgumentException("A Scene reference token must be nonempty.", nameof(targetEntityId)));
        }

        SceneExpandedReferenceBinding? selected = null;

        foreach (var binding in _plan.ReferenceBindings)
        {
            bool matches;

            try
            {
                matches = (binding.ExpandedEntityIndex == _expandedEntityIndex)
                    && (binding.ComponentSchema == _componentSchema)
                    && PathMatches(binding.PropertyPath, field.Path)
                    && TargetMatches(binding.Target, targetEntityId);
            }
            catch (InvalidDataException exception)
            {
                return Fail<SceneExpandedReferenceBinding>(exception);
            }

            if (!matches)
            {
                continue;
            }

            if (selected is not null)
            {
                ThrowActiveMisuse(new InvalidDataException("A Scene component field has duplicate expanded reference bindings."));
            }

            selected = binding;
        }

        return selected
            ?? Fail<SceneExpandedReferenceBinding>(
                new InvalidDataException("A Scene component field has no exact expanded reference binding."));
    }

    private Entity? ResolveTarget(SceneExpandedReferenceBinding binding)
    {
        if (binding.ResolvedTargetExpandedEntityIndex is int expandedEntityIndex)
        {
            if ((uint)expandedEntityIndex >= (uint)_candidateEntities.Length)
            {
                return Fail<Entity?>(new InvalidDataException("The Scene reference binding has an invalid expanded Entity map index."));
            }

            return _candidateEntities[expandedEntityIndex];
        }

        if (binding.ResolvedExternalSlotBindingIndex is not int externalSlotBindingIndex)
        {
            return Fail<Entity?>(new InvalidDataException("The Scene reference binding has no resolved target."));
        }

        var externalBindings = _plan.ExternalSlotBindings;

        if ((uint)externalSlotBindingIndex >= (uint)externalBindings.Length)
        {
            return Fail<Entity?>(new InvalidDataException("The Scene reference binding has an invalid external-slot map index."));
        }

        var externalSlotBinding = externalBindings[externalSlotBindingIndex];

        if (externalSlotBinding.TargetExpandedEntityIndex is int targetExpandedEntityIndex)
        {
            if ((uint)targetExpandedEntityIndex >= (uint)_candidateEntities.Length)
            {
                return Fail<Entity?>(new InvalidDataException("The Scene external-slot binding has an invalid expanded Entity map index."));
            }

            return _candidateEntities[targetExpandedEntityIndex];
        }

        if (externalSlotBinding.RootExternalSlotId is not Guid rootSlotId)
        {
            return Fail<Entity?>(new InvalidDataException("The Scene external-slot target did not resolve to a root slot or expanded Entity."));
        }

        var suppliedBindings = _options.ExternalBindings;

        for (var index = 0; index < suppliedBindings.Length; index++)
        {
            if (suppliedBindings[index].SlotId != rootSlotId)
            {
                continue;
            }

            if (suppliedBindings[index].Target is { } target)
            {
                try
                {
                    _world.ValidateEntityReference(target, excludedEntity: null, parameterName: null);
                }
                catch (ArgumentException exception)
                {
                    ThrowActiveMisuse(exception);
                }

                return target;
            }

            return null;
        }

        return null;
    }

    [DoesNotReturn]
    private void ThrowActiveMisuse(Exception exception)
    {
        RecordFailure(exception);
        ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private void ValidateComponentField(ComponentFieldDescriptor referenceField)
    {
        if (referenceField.ComponentSchema != _componentSchema)
        {
            ThrowActiveMisuse(new ArgumentException("The selected reference field belongs to another component schema.", nameof(referenceField)));
        }

        if ((referenceField.Field.WireKind != 11)
            || (referenceField.Field.Reference != ContentReferenceKind.Entity))
        {
            ThrowActiveMisuse(new ArgumentException("The selected component field is not a declared Entity reference.", nameof(referenceField)));
        }
    }
}
