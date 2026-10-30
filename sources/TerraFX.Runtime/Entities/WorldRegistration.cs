// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Entities;

/// <summary>Contains the immutable component and tag registrations selected before World construction.</summary>
public sealed class WorldRegistration
{
    private readonly ImmutableArray<IWorldAttachmentRegistration> _attachments;
    private readonly ImmutableArray<IComponentRegistration> _components;
    private readonly ImmutableArray<IWorldResourceRegistration> _resources;
    private readonly SystemExecutionSchedule _schedule;
    private readonly ImmutableArray<SystemRegistration> _systems;
    private readonly ImmutableArray<EntityTagSchema> _tags;

    internal WorldRegistration(
        CompiledContentSchemaRegistry schemas,
        ImmutableArray<IComponentRegistration> components,
        ImmutableArray<EntityTagSchema> tags,
        ImmutableArray<IWorldResourceRegistration> resources = default,
        ImmutableArray<SystemRegistration> systems = default,
        SystemExecutionSchedule? schedule = null,
        ImmutableArray<IWorldAttachmentRegistration> attachments = default)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        if (components.IsDefault)
        {
            throw new ArgumentException("The component registration array must be initialized.", nameof(components));
        }

        if (tags.IsDefault)
        {
            throw new ArgumentException("The tag registration array must be initialized.", nameof(tags));
        }

        if (resources.IsDefault)
        {
            resources = [];
        }

        if (systems.IsDefault)
        {
            systems = [];
        }

        if (attachments.IsDefault)
        {
            attachments = [];
        }

        var componentTypes = new HashSet<Type>();

        foreach (var component in components)
        {
            ArgumentNullException.ThrowIfNull(component);

            if (!componentTypes.Add(component.ComponentType))
            {
                throw new ArgumentException("A CLR component type can be registered only once in one World.", nameof(components));
            }
        }

        var resourceIds = new HashSet<Guid>();

        foreach (var resource in resources)
        {
            ArgumentNullException.ThrowIfNull(resource);

            if (!resourceIds.Add(resource.ResourceId))
            {
                throw new ArgumentException("Resource identities must be unique within one World registration.", nameof(resources));
            }
        }

        var attachmentTypes = new HashSet<(AttachmentScope Scope, Type ValueType)>();
        var attachmentSchemas = new HashSet<(AttachmentScope Scope, ContentSchemaReference Schema)>();

        foreach (var attachment in attachments)
        {
            ArgumentNullException.ThrowIfNull(attachment);

            if ((attachment.Scope is not AttachmentScope.World and not AttachmentScope.Entity)
                || !attachmentTypes.Add((attachment.Scope, attachment.ValueType))
                || !attachmentSchemas.Add((attachment.Scope, attachment.Descriptor.Schema)))
            {
                throw new ArgumentException("Attachment registrations must have unique scopes, CLR types, and schema tuples.", nameof(attachments));
            }
        }

        var sortedComponents = components.AsSpan().ToArray();
        Array.Sort(sortedComponents, static (left, right) => CompareSchema(left.SchemaReference, right.SchemaReference));

        var sortedTags = tags.AsSpan().ToArray();
        Array.Sort(sortedTags, static (left, right) => CompareSchema(left.Schema, right.Schema));
        var sortedResources = resources.AsSpan().ToArray();
        Array.Sort(sortedResources, static (left, right) => left.ResourceId.CompareTo(right.ResourceId));
        var sortedSystems = systems.AsSpan().ToArray();
        Array.Sort(
            sortedSystems,
            static (left, right) => FoundationCatalogue.CompareRfc(left.Description.SystemId, right.Description.SystemId));
        var sortedAttachments = attachments.AsSpan().ToArray();
        Array.Sort(sortedAttachments, static (left, right) => {
            var scopeOrder = left.Scope.CompareTo(right.Scope);
            return (scopeOrder != 0) ? scopeOrder : CompareSchema(left.Descriptor.Schema, right.Descriptor.Schema);
        });
        Schemas = schemas;
        _components = [.. sortedComponents];
        _resources = [.. sortedResources];
        _systems = [.. sortedSystems];
        _attachments = [.. sortedAttachments];
        _schedule = schedule ?? SystemExecutionSchedule.Compile(
            [],
            [.. sortedResources.Select(static resource => resource.Description)],
            WorldSchedulingLimits.Default);
        _tags = [.. sortedTags];
    }

    /// <summary>Gets the compiled schema metadata used by this registration.</summary>
    public CompiledContentSchemaRegistry Schemas { get; }

    /// <summary>Gets the immutable entity-tag registrations in canonical schema order.</summary>
    public ImmutableArray<EntityTagSchema> Tags => _tags;

    internal ImmutableArray<IWorldAttachmentRegistration> Attachments => _attachments;

    internal ImmutableArray<IComponentRegistration> Components => _components;

    internal int RegisteredTypeCount => checked(_components.Length + _tags.Length);

    internal ImmutableArray<IWorldResourceRegistration> Resources => _resources;

    internal SystemExecutionSchedule Schedule => _schedule;

    internal long SystemInstanceStorageByteReservation
    {
        get
        {
            var instanceArrayBytes = checked(64L + ((checked((long)_systems.Length * IntPtr.Size) + 7L) & ~7L));
            var scheduleIndexBytes = checked(64L + ((checked((long)_systems.Length * sizeof(int)) + 7L) & ~7L));
            return checked(instanceArrayBytes + scheduleIndexBytes + ((long)_systems.Length * 128L));
        }
    }

    internal ImmutableArray<SystemRegistration> Systems => _systems;

    private static int CompareSchema(ContentSchemaReference left, ContentSchemaReference right)
    {
        var comparison = FoundationCatalogue.CompareRfc(left.SchemaId, right.SchemaId);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Major.CompareTo(right.Major);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Minor.CompareTo(right.Minor);
        return (comparison != 0) ? comparison : left.Scope.CompareTo(right.Scope);
    }

    /// <summary>Gets the exact transferred attachment descriptor registered for one scope/schema pair.</summary>
    /// <param name="scope">The World or Entity owner scope.</param>
    /// <param name="schema">The exact AttachmentState schema tuple.</param>
    /// <returns>The immutable descriptor captured by this registration.</returns>
    /// <exception cref="ArgumentException"><paramref name="schema"/> is invalid or is not a Content tuple.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scope"/> is undefined.</exception>
    /// <exception cref="KeyNotFoundException">The exact scope/schema pair is not registered.</exception>
    public WorldAttachmentFactoryDescriptor GetAttachmentBinding(
        AttachmentScope scope,
        ContentSchemaReference schema)
    {
        if (scope is not AttachmentScope.World and not AttachmentScope.Entity)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if ((schema.SchemaId == Guid.Empty)
            || (schema.Major <= 0)
            || (schema.Minor < 0)
            || (schema.Scope != ContentSchemaScope.Content))
        {
            throw new ArgumentException("An attachment lookup requires a valid Content schema tuple.", nameof(schema));
        }

        foreach (var attachment in _attachments)
        {
            if ((attachment.Scope == scope) && (attachment.Descriptor.Schema == schema))
            {
                return attachment.Descriptor;
            }
        }

        throw new KeyNotFoundException("No attachment with the supplied scope and schema is registered.");
    }

    /// <summary>Gets the immutable binding descriptor captured from the component factory.</summary>
    /// <param name="schema">The exact component schema tuple.</param>
    /// <returns>The descriptor retained by the successful registration.</returns>
    /// <exception cref="ArgumentException">The schema tuple is invalid.</exception>
    /// <exception cref="KeyNotFoundException">The component schema is not registered.</exception>
    public ComponentFactoryDescriptor GetComponentBinding(ContentSchemaReference schema)
    {
        var component = FindComponent(schema);
        return component.Descriptor;
    }

    /// <summary>Gets the exact registered component layout.</summary>
    /// <param name="schema">The exact component schema tuple.</param>
    /// <returns>The layout retained by the successful registration.</returns>
    /// <exception cref="ArgumentException">The schema tuple is invalid.</exception>
    /// <exception cref="KeyNotFoundException">The component schema is not registered.</exception>
    public ComponentLayout GetComponentLayout(ContentSchemaReference schema)
    {
        var component = FindComponent(schema);
        return component.Layout;
    }

    /// <summary>Gets the immutable descriptor for one exactly registered resource identity.</summary>
    /// <param name="resourceId">The nonempty full resource identity.</param>
    /// <returns>The descriptor retained by this registration.</returns>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty.</exception>
    /// <exception cref="KeyNotFoundException">No resource with this identity is registered.</exception>
    public WorldResourceRegistrationDescriptor GetResourceBinding(Guid resourceId)
    {
        if (resourceId == Guid.Empty)
        {
            throw new ArgumentException("The resource ID must be nonempty.", nameof(resourceId));
        }

        foreach (var resource in _resources)
        {
            if (resource.ResourceId == resourceId)
            {
                return resource.Descriptor;
            }
        }

        throw new KeyNotFoundException("No resource with the supplied ID is registered.");
    }

    internal bool TryGetAttachment(AttachmentScope scope, Type valueType, out IWorldAttachmentRegistration? attachment)
    {
        ArgumentNullException.ThrowIfNull(valueType);

        foreach (var candidate in _attachments)
        {
            if ((candidate.Scope == scope) && (candidate.ValueType == valueType))
            {
                attachment = candidate;
                return true;
            }
        }

        attachment = null;
        return false;
    }

    internal bool TryGetComponent<T>(ContentSchemaReference schema, out ComponentRegistration<T>? component)
        where T : unmanaged
    {
        var index = FindComponentIndex(schema);

        if ((index >= 0) && (_components[index] is ComponentRegistration<T> typed))
        {
            component = typed;
            return true;
        }

        component = null;
        return false;
    }

    internal bool TryGetComponentType<T>(out ComponentRegistration<T>? component)
        where T : unmanaged
    {
        foreach (var registeredComponent in _components)
        {
            if (registeredComponent.ComponentType == typeof(T))
            {
                component = registeredComponent as ComponentRegistration<T>
                    ?? throw new InvalidOperationException("The registered component type does not match its closed registration.");
                return true;
            }
        }

        component = null;
        return false;
    }

    internal bool TryGetResource<T>(Guid resourceId, out int index, out IWorldResourceRegistration? resource)
        where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfEqual(resourceId, Guid.Empty);

        for (var resourceIndex = 0; resourceIndex < _resources.Length; resourceIndex++)
        {
            var candidate = _resources[resourceIndex];

            if (candidate.ResourceId == resourceId)
            {
                if (candidate.ResourceType != typeof(T))
                {
                    break;
                }

                index = resourceIndex;
                resource = candidate;
                return true;
            }
        }

        index = -1;
        resource = null;
        return false;
    }

    private IComponentRegistration FindComponent(ContentSchemaReference schema)
    {
        if ((schema.SchemaId == Guid.Empty) || (schema.Major <= 0) || (schema.Minor < 0))
        {
            throw new ArgumentException("The component schema tuple is invalid.", nameof(schema));
        }

        var index = FindComponentIndex(schema);

        if (index >= 0)
        {
            return _components[index];
        }

        throw new KeyNotFoundException("No component with the exact schema tuple is registered.");
    }

    private int FindComponentIndex(ContentSchemaReference schema)
    {
        var low = 0;
        var high = _components.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = CompareSchema(_components[middle].SchemaReference, schema);

            if (comparison == 0)
            {
                return middle;
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

        return -1;
    }
}
