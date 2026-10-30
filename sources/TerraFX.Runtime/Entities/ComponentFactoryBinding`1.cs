// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

/// <summary>Captures the closed providers and immutable metadata for one component type.</summary>
/// <typeparam name="T">The unmanaged component type.</typeparam>
public sealed class ComponentFactoryBinding<T>
    where T : unmanaged
{
    private readonly ComponentFactoryDescriptor _descriptor;
    private readonly object _providerRoots;

    private ComponentFactoryBinding(ComponentFactoryDescriptor descriptor, object providerRoots)
    {
        _descriptor = descriptor;
        _providerRoots = providerRoots;
    }

    /// <summary>Gets the copied immutable registration facts.</summary>
    public ComponentFactoryDescriptor Descriptor => _descriptor;

    internal object ProviderRoots => _providerRoots;

    /// <summary>Creates a closed component factory registration.</summary>
    /// <typeparam name="TAuthor">The symbolic author value type.</typeparam>
    /// <param name="factoryId">The nonempty stable factory identity.</param>
    /// <param name="factoryVersion">The positive factory version.</param>
    /// <param name="schema">The exact compiled component schema.</param>
    /// <param name="storage">The complete component storage declaration.</param>
    /// <param name="restore">The selected transient restore policy.</param>
    /// <param name="derivedDependencies">The complete persistent Derived dependency set.</param>
    /// <param name="fields">The complete ordered field binding set.</param>
    /// <param name="defaults">The component default provider.</param>
    /// <param name="restorer">The component restore/rebuild provider.</param>
    /// <param name="codec">The component read/validate/count/write provider.</param>
    /// <returns>The immutable component factory binding.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">The schema, storage, field, or dependency registrations are inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factoryId"/> or <paramref name="factoryVersion"/> is invalid.</exception>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed component type is a required part of the factory registration contract.")]
    public static ComponentFactoryBinding<T> Create<TAuthor>(
        Guid factoryId,
        int factoryVersion,
        ComponentSchema<T> schema,
        ComponentStorageDeclaration storage,
        ComponentTransientRestore restore,
        ImmutableArray<ComponentDerivedDependency> derivedDependencies,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> fields,
        IComponentDefaultProvider<T> defaults,
        IComponentRestoreProvider<T, TAuthor> restorer,
        IComponentStateCodec<T, TAuthor> codec)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(restorer);
        ArgumentNullException.ThrowIfNull(codec);

        ArgumentOutOfRangeException.ThrowIfEqual(factoryId, Guid.Empty);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factoryVersion);

        if (storage.LogicalBytes != schema.Layout.LogicalBytes)
        {
            throw new ArgumentException("The storage declaration size must match the selected component layout.", nameof(storage));
        }

        if (restore != schema.TransientRestore)
        {
            throw new ArgumentException("The restore policy must match the exact Core component schema.", nameof(restore));
        }

        ValidateFieldBindings(schema, fields);
        ValidateStorage(schema, storage, fields);
        ValidateDerivedDependencies(schema, restore, derivedDependencies);

        var descriptorFields = ImmutableArray.CreateBuilder<ComponentFieldDescriptor>(fields.Length);

        foreach (var field in fields)
        {
            descriptorFields.Add(field.Descriptor);
        }

        var descriptor = new ComponentFactoryDescriptor(
            schema.Schema,
            schema.Layout,
            storage,
            restore,
            schema.Flags,
            factoryId,
            factoryVersion,
            descriptorFields.MoveToImmutable(),
            derivedDependencies);
        var roots = new CapturedProviderRoots<TAuthor>(defaults, restorer, codec, ImmutableArrayCopy.Create(fields));
        return new ComponentFactoryBinding<T>(descriptor, roots);
    }

    private static ComponentStorageLeaf? FindStorageLeaf(
        ComponentStorageDeclaration storage,
        ComponentStoragePath path)
    {
        foreach (var leaf in storage.Leaves)
        {
            if (leaf.Path.Equals(path))
            {
                return leaf;
            }
        }

        return null;
    }

    private static ComponentStorageKind GetStorageKind(ComponentFieldBindingKind kind) => kind switch {
        ComponentFieldBindingKind.Aggregate or ComponentFieldBindingKind.OptionalAggregate => ComponentStorageKind.Composite,
        ComponentFieldBindingKind.EntityReference => ComponentStorageKind.EntityHandle,
        ComponentFieldBindingKind.FixedArray => ComponentStorageKind.InlineArray,
        ComponentFieldBindingKind.Optional => ComponentStorageKind.OptionalValue,
        ComponentFieldBindingKind.OptionalEntityReference => ComponentStorageKind.OptionalEntityHandle,
        ComponentFieldBindingKind.OptionalStableGuidReference => ComponentStorageKind.OptionalValue,
        ComponentFieldBindingKind.Scalar or ComponentFieldBindingKind.StableGuidReference => ComponentStorageKind.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void ValidateDerivedDependencies(
        ComponentSchema<T> schema,
        ComponentTransientRestore restore,
        ImmutableArray<ComponentDerivedDependency> dependencies)
    {
        if (dependencies.IsDefault)
        {
            throw new ArgumentException("The Derived dependency array must be initialized.", nameof(dependencies));
        }

        if ((restore != ComponentTransientRestore.Derived) && !dependencies.IsEmpty)
        {
            throw new ArgumentException("Only a Derived component may declare restore dependencies.", nameof(dependencies));
        }

        var schemaReferences = new HashSet<ContentSchemaReference>();
        var componentTypes = new HashSet<Type>();

        foreach (var dependency in dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependency);

            if (!ReferenceEquals(dependency.Schemas, schema.Schemas)
                || !schema.Schemas.TryGetSchema(dependency.Schema, out var registeredSchema)
                || !ReferenceEquals(registeredSchema, dependency.Metadata)
                || (registeredSchema.Role != ContentSchemaRole.Component)
                || (registeredSchema.Storage != ContentSchemaStorage.Persistent)
                || !schemaReferences.Add(dependency.Schema)
                || !componentTypes.Add(dependency.ComponentType))
            {
                throw new ArgumentException("Every Derived dependency must be an exact unique Persistent component registration.", nameof(dependencies));
            }
        }
    }

    private static void ValidateFieldBindings<TAuthor>(
        ComponentSchema<T> schema,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> fields)
    {
        if (fields.IsDefault || (fields.Length != schema.Fields.Length))
        {
            throw new ArgumentException("Component factories require the complete ordered field binding set.", nameof(fields));
        }

        for (var index = 0; index < fields.Length; index++)
        {
            var binding = fields[index] ?? throw new ArgumentException("The component field binding array cannot contain null entries.", nameof(fields));
            var descriptor = binding.Descriptor;
            var path = descriptor.Path.Segments;
            var metadata = schema.Fields[index];

            if ((descriptor.ComponentSchema != schema.Schema)
                || !ReferenceEquals(descriptor.Field, metadata.Field)
                || (path.Length != 1)
                || (path[0].Kind != ComponentPropertySegmentKind.FieldTag)
                || (path[0].Value != metadata.Field.Tag))
            {
                throw new ArgumentException("Component field bindings must match every root schema field in tag order.", nameof(fields));
            }
        }
    }

    private static void ValidateStorage<TAuthor>(
        ComponentSchema<T> schema,
        ComponentStorageDeclaration storage,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> fields)
    {
        var seenPaths = new HashSet<ComponentStoragePath>();
        var pending = new Stack<ComponentFieldBinding<T, TAuthor>>();

        foreach (var field in fields)
        {
            pending.Push(field);
        }

        while (pending.TryPop(out var binding))
        {
            foreach (var child in binding.Children)
            {
                pending.Push(child);
            }

            var expectedKind = GetStorageKind(binding.Kind);

            foreach (var path in binding.StoragePaths)
            {
                var leaf = FindStorageLeaf(storage, path);

                if ((leaf is null)
                    || (leaf.Purpose != ComponentStoragePurpose.SchemaValue)
                    || (leaf.Kind != expectedKind)
                    || !seenPaths.Add(path))
                {
                    throw new ArgumentException("Every component accessor path must select one unique schema-value storage leaf.", nameof(storage));
                }

                if ((binding.Kind == ComponentFieldBindingKind.FixedArray)
                    && (checked((ulong)leaf.InlineCount) != binding.Descriptor.Field.MaximumCount))
                {
                    throw new ArgumentException("A FixedArray storage leaf must match the schema's fixed extent.", nameof(storage));
                }
            }
        }

        foreach (var leaf in storage.Leaves)
        {
            if ((leaf.Purpose == ComponentStoragePurpose.SchemaValue) && !seenPaths.Contains(leaf.Path))
            {
                throw new ArgumentException("Every schema-value storage leaf must be selected by a typed field binding.", nameof(storage));
            }
        }
    }

    internal T CreateDefault()
    {
        if (_providerRoots is not IDefaultProviderRoot defaultRoot)
        {
            throw new InvalidOperationException("The captured component factory has no default-provider root.");
        }

        var context = new DefaultContext();
        var binding = new ComponentDefaultBinding(context);

        try
        {
            return defaultRoot.CreateDefault(binding);
        }
        finally
        {
            binding.Revoke();
            context.Revoke();
        }
    }

    internal bool IsEntityReferencePath(ComponentFieldPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_providerRoots is not IDefaultProviderRoot defaultRoot)
        {
            throw new InvalidOperationException("The captured component factory has no entity-reference field root.");
        }

        return defaultRoot.IsEntityReferencePath(path);
    }

    internal bool TrySetEntityReference(ref T value, ComponentFieldPath path, Entity? target)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_providerRoots is not IDefaultProviderRoot defaultRoot)
        {
            throw new InvalidOperationException("The captured component factory has no entity-reference field root.");
        }

        return defaultRoot.TrySetEntityReference(ref value, path, target);
    }

    internal ImmutableArray<Diagnostic> Validate(in T value, CancellationToken cancellationToken)
    {
        if (_providerRoots is not IDefaultProviderRoot defaultRoot)
        {
            throw new InvalidOperationException("The captured component factory has no validation-provider root.");
        }

        var context = new ValidationContext();
        var binding = new ComponentValidationBinding(context);

        try
        {
            var diagnostics = defaultRoot.Validate(in value, binding, cancellationToken);

            if (diagnostics.IsDefault)
            {
                throw new InvalidOperationException("The component validation provider returned a default diagnostic array.");
            }

            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic is null)
                {
                    throw new InvalidOperationException("The component validation provider returned a null diagnostic.");
                }
            }

            return ImmutableArrayCopy.Create(diagnostics);
        }
        finally
        {
            binding.Revoke();
            context.Revoke();
        }
    }

    internal void ValidateEntityReferences(in T value, World world, Entity? excludedEntity, string? parameterName)
    {
        if (_providerRoots is not IDefaultProviderRoot defaultRoot)
        {
            throw new InvalidOperationException("The captured component factory has no entity-reference validation root.");
        }

        defaultRoot.ValidateEntityReferences(in value, world, excludedEntity, parameterName);
    }

    private sealed class CapturedProviderRoots<TAuthor> : IDefaultProviderRoot
    {
        internal readonly IComponentStateCodec<T, TAuthor> Codec;

        internal readonly IComponentDefaultProvider<T> Defaults;

        internal readonly ImmutableArray<ComponentFieldBinding<T, TAuthor>> Fields;

        internal readonly IComponentRestoreProvider<T, TAuthor> Restorer;

        internal CapturedProviderRoots(
            IComponentDefaultProvider<T> defaults,
            IComponentRestoreProvider<T, TAuthor> restorer,
            IComponentStateCodec<T, TAuthor> codec,
            ImmutableArray<ComponentFieldBinding<T, TAuthor>> fields)
        {
            Defaults = defaults;
            Restorer = restorer;
            Codec = codec;
            Fields = fields;
        }

        public T CreateDefault(ComponentDefaultBinding binding)
            => Defaults.Create(binding);

        public bool IsEntityReferencePath(ComponentFieldPath path)
        {
            foreach (var field in Fields)
            {
                if (field.IsEntityReferencePath(path))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TrySetEntityReference(ref T value, ComponentFieldPath path, Entity? target)
        {
            foreach (var field in Fields)
            {
                if (field.TrySetEntityReference(ref value, path, target))
                {
                    return true;
                }
            }

            return false;
        }

        public ImmutableArray<Diagnostic> Validate(
            in T value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
            => Codec.Validate(in value, binding, cancellationToken);

        public void ValidateEntityReferences(in T value, World world, Entity? excludedEntity, string? parameterName)
        {
            foreach (var field in Fields)
            {
                field.ValidateEntityReferences(in value, world, excludedEntity, parameterName);
            }
        }
    }

    private sealed class DefaultContext : IComponentDefaultContext
    {
        private bool _isActive = true;

        internal DefaultContext()
        {
            OwnerThreadId = Environment.CurrentManagedThreadId;
        }

        public bool IsActive => Volatile.Read(ref _isActive);

        public int OwnerThreadId { get; }

        internal void Revoke() => Volatile.Write(ref _isActive, false);
    }

    private interface IDefaultProviderRoot
    {
        T CreateDefault(ComponentDefaultBinding binding);

        bool IsEntityReferencePath(ComponentFieldPath path);

        bool TrySetEntityReference(ref T value, ComponentFieldPath path, Entity? target);

        ImmutableArray<Diagnostic> Validate(in T value, ComponentValidationBinding binding, CancellationToken cancellationToken);

        void ValidateEntityReferences(in T value, World world, Entity? excludedEntity, string? parameterName);
    }

    private sealed class ValidationContext : IComponentValidationContext
    {
        private bool _isActive = true;

        internal ValidationContext()
        {
            OwnerThreadId = Environment.CurrentManagedThreadId;
        }

        public bool IsActive => Volatile.Read(ref _isActive);

        public int OwnerThreadId { get; }

        public ImmutableArray<Diagnostic> ValidateFields<TValue>(in TValue value)
            where TValue : unmanaged
        {
            if (typeof(TValue) != typeof(T))
            {
                throw new InvalidOperationException("A component validation binding can validate only its registered root component type.");
            }

            return [];
        }

        internal void Revoke() => Volatile.Write(ref _isActive, false);
    }
}
