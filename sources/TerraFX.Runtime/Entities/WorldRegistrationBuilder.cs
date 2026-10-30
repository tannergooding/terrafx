// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Builds the immutable component and tag registrations for a World.</summary>
public sealed class WorldRegistrationBuilder
{
    private readonly List<IWorldAttachmentRegistration> _attachments = [];
    private readonly List<IComponentRegistration> _components = [];
    private bool _isBuilt;
    private readonly List<IWorldResourceRegistration> _resources = [];
    private readonly CompiledContentSchemaRegistry _schemas;
    private readonly List<SystemRegistration> _systems = [];
    private readonly List<EntityTagSchema> _tags = [];

    /// <summary>Initializes a registration builder for one compiled schema registry.</summary>
    /// <param name="schemas">The immutable compiled schema metadata.</param>
    /// <exception cref="ArgumentNullException"><paramref name="schemas"/> is null.</exception>
    public WorldRegistrationBuilder(CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        _schemas = schemas;
    }

    /// <summary>Freezes the registered components, tags, and resources.</summary>
    /// <returns>The immutable World registration.</returns>
    /// <exception cref="InvalidOperationException">The builder has already been frozen.</exception>
    public WorldRegistration Build()
    {
        EnsureMutable();
        ValidateDerivedDependencies();
        var systems = ImmutableArray.CreateBuilder<SystemDescription>(_systems.Count);

        foreach (var system in _systems)
        {
            systems.Add(system.Description);
        }

        var resources = ImmutableArray.CreateBuilder<SystemResourceDescription>(_resources.Count);

        foreach (var resource in _resources)
        {
            resources.Add(resource.Description);
        }

        var schedule = SystemExecutionSchedule.Compile(
            systems.MoveToImmutable(),
            resources.MoveToImmutable(),
            WorldSchedulingLimits.Default);
        var registration = new WorldRegistration(
            _schemas,
            [.. _components],
            [.. _tags],
            [.. _resources],
            [.. _systems],
            schedule,
            [.. _attachments]);
        _isBuilt = true;
        return registration;
    }

    /// <summary>Registers one transferred immutable attachment factory for an exact owner scope.</summary>
    /// <typeparam name="T">The immutable attachment CLR type.</typeparam>
    /// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
    /// <param name="scope">The World or Entity attachment owner scope.</param>
    /// <param name="binding">The exact closed attachment factory binding.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is null.</exception>
    /// <exception cref="ArgumentException">The binding is from another schema registry or duplicates an existing key.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scope"/> is undefined.</exception>
    /// <exception cref="InvalidOperationException">The builder has already been frozen.</exception>
    public WorldRegistrationBuilder RegisterAttachment<T, TAuthor>(
        AttachmentScope scope,
        WorldAttachmentFactoryBinding<T, TAuthor> binding)
        where T : class
        where TAuthor : notnull
    {
        EnsureMutable();

        if (scope is not AttachmentScope.World and not AttachmentScope.Entity)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        ArgumentNullException.ThrowIfNull(binding);

        if (!ReferenceEquals(binding.Schemas, _schemas)
            || !_schemas.TryGetSchema(binding.Descriptor.Schema, out var metadata)
            || (metadata.Role != ContentSchemaRole.AttachmentState))
        {
            throw new ArgumentException("The attachment binding must belong to this exact schema registry.", nameof(binding));
        }

        foreach (var registration in _attachments)
        {
            if ((registration.Scope == scope)
                && ((registration.ValueType == typeof(T))
                    || (registration.Descriptor.Schema == binding.Descriptor.Schema)))
            {
                throw new ArgumentException("Attachment scope, CLR type, and schema keys must be unambiguous.", nameof(binding));
            }
        }

        _attachments.Add(new WorldAttachmentRegistration<T, TAuthor>(scope, binding));
        return this;
    }

    /// <summary>Registers one component schema and captures its closed factory binding.</summary>
    /// <typeparam name="T">The unmanaged component value type.</typeparam>
    /// <param name="schema">The exact compiled component schema.</param>
    /// <param name="factory">The component factory whose binding is captured once.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">The schema/factory registration is inconsistent or duplicated.</exception>
    /// <exception cref="InvalidOperationException">The builder has already been frozen.</exception>
    public WorldRegistrationBuilder RegisterComponent<T>(ComponentSchema<T> schema, IComponentFactory<T> factory)
        where T : unmanaged
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(factory);

        ValidateSchema(schema.Schema, schema.Metadata, ContentSchemaRole.Component);

        if (ContainsSchemaId(schema.Schema.SchemaId))
        {
            throw new ArgumentException("A component or tag with this schema identity is already registered.", nameof(schema));
        }

        if (ContainsComponentType(typeof(T)))
        {
            throw new ArgumentException("A CLR component type can be registered only once in one World.", nameof(schema));
        }

        var binding = factory.Binding;
        ArgumentNullException.ThrowIfNull(binding);
        var descriptor = binding.Descriptor;

        if ((descriptor.Schema != schema.Schema)
            || (descriptor.Layout.LayoutId != schema.Layout.LayoutId)
            || (descriptor.Layout.LogicalBytes != schema.Layout.LogicalBytes)
            || (descriptor.Fields.Length != schema.Fields.Length))
        {
            throw new ArgumentException("The captured component factory binding does not match the exact schema and layout.", nameof(factory));
        }

        if (_components.Count + _tags.Count >= 256)
        {
            throw new ArgumentOutOfRangeException(nameof(schema), "The registration cannot exceed 256 component and tag identities.");
        }

        _components.Add(new ComponentRegistration<T>(schema, binding));
        return this;
    }

    /// <summary>Registers one fresh World-owned resource partition set and its typed codec family.</summary>
    /// <typeparam name="T">The unmanaged resource partition type.</typeparam>
    /// <param name="description">The resource identity, partition count, and parallel eligibility.</param>
    /// <param name="factory">The resource factory whose binding is captured once.</param>
    /// <param name="codec">The state codec whose binding is captured once.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">A binding, schema, resource identity, or captured family is inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The resource partition count exceeds its registered FixedArray domain.</exception>
    /// <exception cref="InvalidOperationException">The builder has already been frozen.</exception>
    public WorldRegistrationBuilder RegisterResource<T>(
        SystemResourceDescription description,
        IWorldResourceFactory<T> factory,
        IWorldResourceStateCodec<T> codec)
        where T : unmanaged
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(codec);

        var factoryBinding = factory.Binding
            ?? throw new ArgumentException("The factory returned no resource binding.", nameof(factory));
        var codecBinding = codec.Binding
            ?? throw new ArgumentException("The codec returned no resource binding.", nameof(codec));

        if (!ReferenceEquals(factoryBinding.Control, codecBinding.Control))
        {
            throw new ArgumentException(
                "The resource factory and codec belong to different captured families.",
                nameof(codec));
        }

        var descriptor = factoryBinding.Descriptor;

        if ((descriptor.Schema.Scope != ContentSchemaScope.Content)
            || !_schemas.TryGetSchema(descriptor.Schema, out var metadata)
            || (metadata.Role != ContentSchemaRole.ResourceState))
        {
            throw new ArgumentException("The resource factory does not select an exact active Content ResourceState schema.", nameof(factory));
        }

        foreach (var field in descriptor.Fields)
        {
            if ((ulong)description.PartitionCount < field.Metadata.MinimumCount
                || (ulong)description.PartitionCount > field.Metadata.MaximumCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(description),
                    "The resource partition count is outside a registered FixedArray field domain.");
            }
        }

        if (ContainsResourceId(description.ResourceId))
        {
            throw new ArgumentException("A resource identity can be registered only once in one World.", nameof(description));
        }

        if (_resources.Count >= 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(description), "A World cannot register more than 4096 resources.");
        }

        _resources.Add(new WorldResourceRegistration<T>(description, factoryBinding.Control, codecBinding.Dispatch));
        return this;
    }

    /// <summary>Registers one scheduled World system and captures its typed factory binding once.</summary>
    /// <param name="description">The immutable scheduling identity, stage, access, and dependency facts.</param>
    /// <param name="factory">The system factory whose binding is captured once.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">The binding is null, mismatched, or duplicates a system identity/order.</exception>
    /// <exception cref="InvalidOperationException">The builder has already been frozen.</exception>
    public WorldRegistrationBuilder RegisterSystem(SystemDescription description, IWorldSystemFactory factory)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(factory);
        var binding = factory.Binding
            ?? throw new ArgumentException("The factory returned no system binding.", nameof(factory));
        var descriptor = binding.Descriptor;

        if ((descriptor.SystemType != descriptor.SourceState.SystemType)
            || descriptor.SystemType.ContainsGenericParameters)
        {
            throw new ArgumentException("The captured system binding does not match its exact closed source declaration.", nameof(factory));
        }

        foreach (var registered in _systems)
        {
            if (registered.Description.SystemId == description.SystemId)
            {
                throw new ArgumentException("A system identity can be registered only once in one World.", nameof(description));
            }

            if (registered.Description.Order == description.Order)
            {
                throw new ArgumentException("A system order can be registered only once in one World.", nameof(description));
            }
        }

        if (_systems.Count >= 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(description), "A World cannot register more than 4096 systems.");
        }

        _systems.Add(new SystemRegistration(description, binding));
        return this;
    }

    /// <summary>Registers one zero-payload entity tag.</summary>
    /// <param name="schema">The exact compiled entity-tag schema.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is null.</exception>
    /// <exception cref="ArgumentException">The tag is not from this registry or its identity is duplicated.</exception>
    /// <exception cref="InvalidOperationException">The builder has already been frozen.</exception>
    public WorldRegistrationBuilder RegisterTag(EntityTagSchema schema)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(schema);
        var schemaReference = schema.Schema;

        if (!_schemas.TryGetSchema(schemaReference, out var metadata)
            || (metadata.Role != ContentSchemaRole.EntityTag)
            || !metadata.Fields.IsEmpty)
        {
            throw new ArgumentException("The tag is not the exact zero-payload entity-tag declaration in this registry.", nameof(schema));
        }

        if (ContainsSchemaId(schema.Schema.SchemaId))
        {
            throw new ArgumentException("A component or tag with this schema identity is already registered.", nameof(schema));
        }

        if (_components.Count + _tags.Count >= 256)
        {
            throw new ArgumentOutOfRangeException(nameof(schema), "The registration cannot exceed 256 component and tag identities.");
        }

        _tags.Add(schema);
        return this;
    }

    private bool ContainsComponentType(Type componentType)
    {
        foreach (var component in _components)
        {
            if (component.ComponentType == componentType)
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsResourceId(Guid resourceId)
    {
        foreach (var resource in _resources)
        {
            if (resource.ResourceId == resourceId)
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsSchemaId(Guid schemaId)
    {
        foreach (var component in _components)
        {
            if (component.SchemaReference.SchemaId == schemaId)
            {
                return true;
            }
        }

        foreach (var tag in _tags)
        {
            if (tag.Schema.SchemaId == schemaId)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureMutable()
    {
        if (_isBuilt)
        {
            throw new InvalidOperationException("The World registration builder has already been frozen.");
        }
    }

    private void ValidateDerivedDependencies()
    {
        foreach (var component in _components)
        {
            foreach (var dependency in component.Descriptor.DerivedDependencies)
            {
                var registered = false;

                foreach (var candidate in _components)
                {
                    if ((candidate.ComponentType == dependency.ComponentType)
                        && (candidate.SchemaReference == dependency.Schema))
                    {
                        registered = true;
                        break;
                    }
                }

                if (!registered)
                {
                    throw new InvalidOperationException("Every Derived dependency must be registered in the same World.");
                }
            }
        }
    }

    private void ValidateSchema(ContentSchemaReference schema, DataSchema metadata, ContentSchemaRole role)
    {
        if ((schema.Scope != ContentSchemaScope.Content)
            || (metadata.Schema != schema)
            || (metadata.Role != role)
            || !_schemas.TryGetSchema(schema, out var registered)
            || !ReferenceEquals(metadata, registered))
        {
            throw new ArgumentException("The selected schema is not the exact compatible declaration in this registry.", nameof(schema));
        }
    }
}
