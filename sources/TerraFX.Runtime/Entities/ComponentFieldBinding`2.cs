// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Numerics;

namespace TerraFX.Entities;

/// <summary>Binds one declared component field to concrete runtime and author accessors.</summary>
/// <typeparam name="T">The unmanaged component type.</typeparam>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed component and author types are required by the typed accessor factory contract.")]
public sealed class ComponentFieldBinding<T, TAuthor>
    where T : unmanaged
{
    private readonly object _accessorState;
    private readonly ImmutableArray<ComponentFieldBinding<T, TAuthor>> _children;
    private readonly ComponentFieldDescriptor _descriptor;
    private readonly IEntityReferenceAccessor? _entityReferenceAccessor;
    private readonly ComponentFieldBindingKind _kind;
    private readonly IOptionalAggregatePresenceAccessor? _optionalAggregatePresenceAccessor;
    private readonly ImmutableArray<ComponentStoragePath> _storagePaths;

    private ComponentFieldBinding(
        ComponentFieldDescriptor descriptor,
        ComponentFieldBindingKind kind,
        ImmutableArray<ComponentStoragePath> storagePaths,
        object accessorState,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> children,
        IEntityReferenceAccessor? entityReferenceAccessor = null,
        IOptionalAggregatePresenceAccessor? optionalAggregatePresenceAccessor = null)
    {
        _descriptor = descriptor;
        _kind = kind;
        _storagePaths = storagePaths;
        _accessorState = accessorState;
        _children = children;
        _entityReferenceAccessor = entityReferenceAccessor;
        _optionalAggregatePresenceAccessor = optionalAggregatePresenceAccessor;
    }

    /// <summary>Gets the selected Core field descriptor.</summary>
    public ComponentFieldDescriptor Descriptor => _descriptor;

    /// <summary>Gets the immutable physical storage paths selected by the accessors.</summary>
    public ImmutableArray<ComponentStoragePath> StoragePaths => _storagePaths;

    internal object AccessorState => _accessorState;

    internal ImmutableArray<ComponentFieldBinding<T, TAuthor>> Children => _children;

    internal ComponentFieldBindingKind Kind => _kind;

    /// <summary>Creates a required composite-record field binding.</summary>
    /// <typeparam name="TStored">The unmanaged stored record representation.</typeparam>
    /// <typeparam name="TSymbolic">The immutable author record representation.</typeparam>
    /// <typeparam name="TRuntime">The concrete runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete author reader struct.</typeparam>
    /// <param name="field">The selected required Record field.</param>
    /// <param name="storagePaths">The physical storage paths selected by the accessors.</param>
    /// <param name="runtime">The concrete runtime accessors, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <param name="children">The complete ordered binding of the selected nested schema fields.</param>
    /// <returns>The immutable composite field binding.</returns>
    /// <exception cref="ArgumentException">The field, storage paths, or child closure is incompatible.</exception>
    public static ComponentFieldBinding<T, TAuthor> Aggregate<TStored, TSymbolic, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> children)
        where TStored : unmanaged
        where TRuntime : struct, IComponentValueAccessor<T, TStored>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, TSymbolic>
    {
        ValidateRecordField(field, isRequired: true, storagePaths);
        ValidateAggregateChildren(field, children);

        return new ComponentFieldBinding<T, TAuthor>(
            field,
            ComponentFieldBindingKind.Aggregate,
            CopyStoragePaths(storagePaths, allowEmpty: true),
            new CapturedAccessors<TRuntime, TAuthorReader>(runtime, author),
            ImmutableArrayCopy.Create(children));
    }

    /// <summary>Creates a required live-Entity reference binding.</summary>
    /// <typeparam name="TRuntime">The concrete runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete author reader struct.</typeparam>
    /// <param name="field">The selected required Entity-reference field.</param>
    /// <param name="storagePaths">The physical storage paths selected by the accessors.</param>
    /// <param name="runtime">The concrete runtime accessors, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable Entity-reference field binding.</returns>
    /// <exception cref="ArgumentException">The field role or storage paths are incompatible.</exception>
    public static ComponentFieldBinding<T, TAuthor> EntityReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IComponentValueAccessor<T, Entity>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, Guid>
    {
        ValidateField(field, storagePaths, 11, isRequired: true, ContentReferenceKind.Entity);

        var accessors = new CapturedAccessors<TRuntime, TAuthorReader>(runtime, author);
        var referenceAccessor = new RequiredEntityReferenceAccessor<TRuntime>(runtime);
        return new ComponentFieldBinding<T, TAuthor>(
            field,
            ComponentFieldBindingKind.EntityReference,
            CopyStoragePaths(storagePaths, allowEmpty: false),
            accessors,
            [],
            referenceAccessor);
    }

    /// <summary>Creates a required FixedArray field binding.</summary>
    /// <typeparam name="TElement">The unmanaged array element type.</typeparam>
    /// <typeparam name="TRuntime">The concrete runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete author array reader struct.</typeparam>
    /// <param name="field">The selected required FixedArray field.</param>
    /// <param name="storagePaths">The physical storage paths selected by the accessors.</param>
    /// <param name="runtime">The concrete runtime accessors, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable FixedArray field binding.</returns>
    /// <exception cref="ArgumentException">The field element kind or storage paths are incompatible.</exception>
    public static ComponentFieldBinding<T, TAuthor> FixedArray<TElement, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TElement : unmanaged
        where TRuntime : struct, IComponentArrayAccessor<T, TElement>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, TElement>
    {
        ArgumentNullException.ThrowIfNull(field);

        if ((field.Field.WireKind != 23)
            || (field.Field.ElementKind != GetWireKind<TElement>())
            || !field.Field.IsRequired
            || (field.Field.Reference != ContentReferenceKind.None))
        {
            throw new ArgumentException("The selected field is not a matching required FixedArray.", nameof(field));
        }

        return Create(ComponentFieldBindingKind.FixedArray, field, storagePaths, runtime, author);
    }

    /// <summary>Creates an optional scalar field binding.</summary>
    /// <typeparam name="TValue">The unmanaged field value.</typeparam>
    /// <typeparam name="TRuntime">The concrete optional runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete optional author reader struct.</typeparam>
    /// <param name="field">The selected optional scalar field.</param>
    /// <param name="storagePaths">The physical storage paths selected by the accessors.</param>
    /// <param name="runtime">The concrete runtime accessors, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable optional field binding.</returns>
    /// <exception cref="ArgumentException">The field kind, reference role, or storage paths are incompatible.</exception>
    public static ComponentFieldBinding<T, TAuthor> Optional<TValue, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IComponentOptionalAccessor<T, TValue>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, TValue>
    {
        ValidateField(field, storagePaths, GetWireKind<TValue>(), isRequired: false, ContentReferenceKind.None);

        return Create(ComponentFieldBindingKind.Optional, field, storagePaths, runtime, author);
    }

    /// <summary>Creates an optional composite-record field binding.</summary>
    /// <typeparam name="TStored">The unmanaged stored record representation.</typeparam>
    /// <typeparam name="TSymbolic">The immutable author record representation.</typeparam>
    /// <typeparam name="TRuntime">The concrete optional runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete optional author reader struct.</typeparam>
    /// <param name="field">The selected optional Record field.</param>
    /// <param name="storagePaths">The physical storage paths selected by the accessors.</param>
    /// <param name="runtime">The concrete runtime accessors, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <param name="children">The complete ordered binding of the selected nested schema fields.</param>
    /// <returns>The immutable optional composite field binding.</returns>
    /// <exception cref="ArgumentException">The field, storage paths, or child closure is incompatible.</exception>
    public static ComponentFieldBinding<T, TAuthor> OptionalAggregate<TStored, TSymbolic, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> children)
        where TStored : unmanaged
        where TRuntime : struct, IComponentOptionalAccessor<T, TStored>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, TSymbolic>
    {
        ValidateRecordField(field, isRequired: false, storagePaths);
        ValidateAggregateChildren(field, children);

        var accessors = new CapturedAccessors<TRuntime, TAuthorReader>(runtime, author);
        var presenceAccessor = new OptionalAggregatePresenceAccessor<TRuntime, TStored>(runtime);
        return new ComponentFieldBinding<T, TAuthor>(
            field,
            ComponentFieldBindingKind.OptionalAggregate,
            CopyStoragePaths(storagePaths, allowEmpty: true),
            accessors,
            ImmutableArrayCopy.Create(children),
            optionalAggregatePresenceAccessor: presenceAccessor);
    }

    /// <summary>Creates an optional live-Entity reference binding.</summary>
    /// <typeparam name="TRuntime">The concrete optional runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete optional author reader struct.</typeparam>
    /// <param name="field">The selected optional Entity-reference field.</param>
    /// <param name="storagePaths">The physical storage paths selected by the accessors.</param>
    /// <param name="runtime">The concrete runtime accessors, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable optional Entity-reference binding.</returns>
    /// <exception cref="ArgumentException">The field role or storage paths are incompatible.</exception>
    public static ComponentFieldBinding<T, TAuthor> OptionalEntityReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IComponentOptionalAccessor<T, Entity>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, Guid>
    {
        ValidateField(field, storagePaths, 11, isRequired: false, ContentReferenceKind.Entity);

        var accessors = new CapturedAccessors<TRuntime, TAuthorReader>(runtime, author);
        var referenceAccessor = new OptionalEntityReferenceAccessor<TRuntime>(runtime);
        return new ComponentFieldBinding<T, TAuthor>(
            field,
            ComponentFieldBindingKind.OptionalEntityReference,
            CopyStoragePaths(storagePaths, allowEmpty: false),
            accessors,
            [],
            referenceAccessor);
    }

    /// <summary>Creates an optional stable-Guid reference binding.</summary>
    /// <typeparam name="TRuntime">The concrete optional runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete optional author reader struct.</typeparam>
    /// <param name="field">The selected optional stable-Guid reference field.</param>
    /// <param name="storagePaths">The physical storage paths selected by the accessors.</param>
    /// <param name="runtime">The concrete runtime accessors, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable optional stable-Guid reference binding.</returns>
    /// <exception cref="ArgumentException">The field role or storage paths are incompatible.</exception>
    public static ComponentFieldBinding<T, TAuthor> OptionalStableGuidReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IComponentOptionalAccessor<T, Guid>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, Guid>
    {
        ValidateStableGuidReference(field, storagePaths, isRequired: false);

        return Create(ComponentFieldBindingKind.OptionalStableGuidReference, field, storagePaths, runtime, author);
    }

    /// <summary>Creates a required scalar field binding.</summary>
    /// <typeparam name="TValue">The unmanaged field value.</typeparam>
    /// <typeparam name="TRuntime">The concrete runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete author reader struct.</typeparam>
    /// <param name="field">The selected required scalar field.</param>
    /// <param name="storagePaths">The physical storage paths selected by the accessors.</param>
    /// <param name="runtime">The concrete runtime accessors, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable scalar field binding.</returns>
    /// <exception cref="ArgumentException">The field kind, reference role, or storage paths are incompatible.</exception>
    public static ComponentFieldBinding<T, TAuthor> Scalar<TValue, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IComponentValueAccessor<T, TValue>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, TValue>
    {
        ValidateField(field, storagePaths, GetWireKind<TValue>(), isRequired: true, ContentReferenceKind.None);

        return Create(ComponentFieldBindingKind.Scalar, field, storagePaths, runtime, author);
    }

    /// <summary>Creates a required stable-Guid reference binding.</summary>
    /// <typeparam name="TRuntime">The concrete runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete author reader struct.</typeparam>
    /// <param name="field">The selected required stable-Guid reference field.</param>
    /// <param name="storagePaths">The physical storage paths selected by the accessors.</param>
    /// <param name="runtime">The concrete runtime accessors, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable stable-Guid reference binding.</returns>
    /// <exception cref="ArgumentException">The field role or storage paths are incompatible.</exception>
    public static ComponentFieldBinding<T, TAuthor> StableGuidReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IComponentValueAccessor<T, Guid>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, Guid>
    {
        ValidateStableGuidReference(field, storagePaths, isRequired: true);

        return Create(ComponentFieldBindingKind.StableGuidReference, field, storagePaths, runtime, author);
    }

    private static ImmutableArray<ComponentStoragePath> CopyStoragePaths(
        ImmutableArray<ComponentStoragePath> storagePaths,
        bool allowEmpty)
    {
        ValidateStoragePaths(storagePaths, allowEmpty);
        return ImmutableArrayCopy.Create(storagePaths);
    }

    private static ComponentFieldBinding<T, TAuthor> Create<TRuntime, TAuthorReader>(
        ComponentFieldBindingKind kind,
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct
        where TAuthorReader : struct
        => new(
            field,
            kind,
            CopyStoragePaths(storagePaths, allowEmpty: false),
            new CapturedAccessors<TRuntime, TAuthorReader>(runtime, author),
            []);

    private static ushort GetWireKind<TValue>()
        where TValue : unmanaged
    {
        var type = typeof(TValue);

        if (type == typeof(byte))
        {
            return 1;
        }

        if (type == typeof(ushort))
        {
            return 2;
        }

        if (type == typeof(uint))
        {
            return 3;
        }

        if (type == typeof(ulong))
        {
            return 4;
        }

        if (type == typeof(sbyte))
        {
            return 5;
        }

        if (type == typeof(short))
        {
            return 6;
        }

        if (type == typeof(int))
        {
            return 7;
        }

        if (type == typeof(long))
        {
            return 8;
        }

        if (type == typeof(bool))
        {
            return 9;
        }

        if (type == typeof(float))
        {
            return 10;
        }

        if (type == typeof(Guid))
        {
            return 11;
        }

        if (type == typeof(Vector2))
        {
            return 14;
        }

        if (type == typeof(Vector3))
        {
            return 15;
        }

        if (type == typeof(Vector4))
        {
            return 16;
        }

        if (type == typeof(Quaternion))
        {
            return 17;
        }

        if (type == typeof(Matrix3x2))
        {
            return 18;
        }

        if (type == typeof(Matrix4x4))
        {
            return 19;
        }

        if (type == typeof(SpatialPosition2))
        {
            return 20;
        }

        if (type == typeof(SpatialPosition3))
        {
            return 21;
        }

        return 0;
    }

    private static bool PathsEqual(ComponentFieldPath left, ComponentFieldPath right)
    {
        var leftSegments = left.Segments;
        var rightSegments = right.Segments;

        if (leftSegments.Length != rightSegments.Length)
        {
            return false;
        }

        for (var index = 0; index < leftSegments.Length; index++)
        {
            if (!leftSegments[index].Equals(rightSegments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveNestedSchema(ComponentFieldDescriptor field, out DataSchema nestedSchema)
    {
        if (!field.Field.NestedSchemas.IsEmpty)
        {
            if ((field.Field.NestedSchemas.Length == 1)
                && field.Schemas.TryGetSchema(field.Field.NestedSchemas[0], out var exactSchema))
            {
                nestedSchema = exactSchema;
                return true;
            }
        }
        else if (field.Field.NestedRoleMask != 0)
        {
            DataSchema? match = null;

            foreach (var candidate in field.Schemas.Schemas)
            {
                if ((candidate.Schema.Scope == ContentSchemaScope.Content)
                    && (field.Field.NestedRoleMask & (1UL << ((int)candidate.Role - 1))) != 0)
                {
                    if (match is not null)
                    {
                        nestedSchema = null!;
                        return false;
                    }

                    match = candidate;
                }
            }

            if (match is not null)
            {
                nestedSchema = match;
                return true;
            }
        }

        nestedSchema = null!;
        return false;
    }

    private static void ValidateAggregateChildren(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> children)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (children.IsDefault)
        {
            throw new ArgumentException("The aggregate child bindings must be initialized.", nameof(children));
        }

        if (!TryResolveNestedSchema(field, out var nestedSchema) || (children.Length != nestedSchema.Fields.Length))
        {
            throw new ArgumentException("The aggregate children must exactly close one selected nested schema.", nameof(children));
        }

        var parentPath = field.Path.Segments;

        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index] ?? throw new ArgumentException("Aggregate children cannot contain null entries.", nameof(children));
            var childField = child.Descriptor;
            var childPath = childField.Path.Segments;

            if ((childField.ComponentSchema != field.ComponentSchema)
                || !ReferenceEquals(childField.DataSchema, nestedSchema)
                || (childPath.Length != parentPath.Length + 1)
                || !childPath.AsSpan(0, parentPath.Length).SequenceEqual(parentPath.AsSpan())
                || (childPath[^1].Kind != ComponentPropertySegmentKind.FieldTag)
                || (childPath[^1].Value != nestedSchema.Fields[index].Tag)
                || !ReferenceEquals(childField.Field, nestedSchema.Fields[index]))
            {
                throw new ArgumentException("Aggregate children must bind every direct nested field in schema order.", nameof(children));
            }
        }
    }

    private static void ValidateField(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        ushort wireKind,
        bool isRequired,
        ContentReferenceKind reference)
    {
        ArgumentNullException.ThrowIfNull(field);

        if ((wireKind == 0)
            || (field.Field.WireKind != wireKind)
            || (field.Field.IsRequired != isRequired)
            || (field.Field.Reference != reference))
        {
            throw new ArgumentException("The field kind, presence, or reference role does not match the typed accessor.", nameof(field));
        }

        ValidateStoragePaths(storagePaths, allowEmpty: false);
    }

    private static void ValidateRecordField(
        ComponentFieldDescriptor field,
        bool isRequired,
        ImmutableArray<ComponentStoragePath> storagePaths)
    {
        ArgumentNullException.ThrowIfNull(field);

        if ((field.Field.WireKind != 22) || (field.Field.IsRequired != isRequired))
        {
            throw new ArgumentException("The field is not a Record with the selected presence.", nameof(field));
        }

        ValidateStoragePaths(storagePaths, allowEmpty: true);
    }

    private static void ValidateStableGuidReference(
        ComponentFieldDescriptor field,
        ImmutableArray<ComponentStoragePath> storagePaths,
        bool isRequired)
    {
        ArgumentNullException.ThrowIfNull(field);

        if ((field.Field.WireKind != 11)
            || (field.Field.IsRequired != isRequired)
            || (field.Field.Reference is ContentReferenceKind.None or ContentReferenceKind.Entity))
        {
            throw new ArgumentException("The field is not a declared stable-Guid reference with the selected presence.", nameof(field));
        }

        ValidateStoragePaths(storagePaths, allowEmpty: false);
    }

    private static void ValidateStoragePaths(
        ImmutableArray<ComponentStoragePath> storagePaths,
        bool allowEmpty)
    {
        if (storagePaths.IsDefault || (!allowEmpty && storagePaths.IsEmpty))
        {
            throw new ArgumentException("The component storage paths must be initialized and nonempty for this field shape.", nameof(storagePaths));
        }

        for (var index = 0; index < storagePaths.Length; index++)
        {
            var path = storagePaths[index] ?? throw new ArgumentException("A storage path cannot be null.", nameof(storagePaths));

            for (var previousIndex = 0; previousIndex < index; previousIndex++)
            {
                if (path.Equals(storagePaths[previousIndex]))
                {
                    throw new ArgumentException("Component storage paths must be unique.", nameof(storagePaths));
                }
            }
        }
    }

    internal bool IsEntityReferencePath(ComponentFieldPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (PathsEqual(_descriptor.Path, path) && (_entityReferenceAccessor is not null))
        {
            return true;
        }

        foreach (var child in _children)
        {
            if (child.IsEntityReferencePath(path))
            {
                return true;
            }
        }

        return false;
    }

    internal bool TrySetEntityReference(ref T value, ComponentFieldPath path, Entity? target)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (PathsEqual(_descriptor.Path, path) && (_entityReferenceAccessor is not null))
        {
            _entityReferenceAccessor.Set(ref value, target);
            return true;
        }

        foreach (var child in _children)
        {
            if (child.TrySetEntityReference(ref value, path, target))
            {
                return true;
            }
        }

        return false;
    }

    internal void ValidateEntityReferences(in T value, World world, Entity? excludedEntity, string? parameterName)
    {
        ArgumentNullException.ThrowIfNull(world);

        if ((_optionalAggregatePresenceAccessor is not null)
            && !_optionalAggregatePresenceAccessor.IsPresent(in value))
        {
            return;
        }

        if ((_entityReferenceAccessor is not null)
            && _entityReferenceAccessor.TryGetTarget(in value, out var target))
        {
            world.ValidateEntityReference(target, excludedEntity, parameterName);
        }

        foreach (var child in _children)
        {
            child.ValidateEntityReferences(in value, world, excludedEntity, parameterName);
        }
    }

    private sealed class CapturedAccessors<TRuntime, TAuthorReader>
        where TRuntime : struct
        where TAuthorReader : struct
    {
        internal readonly TAuthorReader Author;
        internal readonly TRuntime Runtime;

        internal CapturedAccessors(TRuntime runtime, TAuthorReader author)
        {
            Runtime = runtime;
            Author = author;
        }
    }

    private interface IEntityReferenceAccessor
    {
        void Set(ref T value, Entity? target);

        bool TryGetTarget(in T value, out Entity target);
    }

    private interface IOptionalAggregatePresenceAccessor
    {
        bool IsPresent(in T value);
    }

    private sealed class OptionalAggregatePresenceAccessor<TRuntime, TStored> : IOptionalAggregatePresenceAccessor
        where TStored : unmanaged
        where TRuntime : struct, IComponentOptionalAccessor<T, TStored>
    {
        private readonly TRuntime _runtime;

        internal OptionalAggregatePresenceAccessor(TRuntime runtime)
        {
            _runtime = runtime;
        }

        public bool IsPresent(in T value)
            => _runtime.IsPresent(in value);
    }

    private sealed class OptionalEntityReferenceAccessor<TRuntime> : IEntityReferenceAccessor
        where TRuntime : struct, IComponentOptionalAccessor<T, Entity>
    {
        private readonly TRuntime _runtime;

        internal OptionalEntityReferenceAccessor(TRuntime runtime)
        {
            _runtime = runtime;
        }

        public void Set(ref T value, Entity? target)
        {
            if (target is { } entity)
            {
                _runtime.SetPresent(ref value, in entity);
            }
            else
            {
                _runtime.Clear(ref value);
            }
        }

        public bool TryGetTarget(in T value, out Entity target)
        {
            if (!_runtime.IsPresent(in value))
            {
                target = default;
                return false;
            }

            target = _runtime.GetPresent(in value);
            return true;
        }
    }

    private sealed class RequiredEntityReferenceAccessor<TRuntime> : IEntityReferenceAccessor
        where TRuntime : struct, IComponentValueAccessor<T, Entity>
    {
        private readonly TRuntime _runtime;

        internal RequiredEntityReferenceAccessor(TRuntime runtime)
        {
            _runtime = runtime;
        }

        public void Set(ref T value, Entity? target)
        {
            if (target is not { } entity)
            {
                throw new ArgumentException("A required Entity reference cannot be cleared.", nameof(target));
            }

            _runtime.Set(ref value, in entity);
        }

        public bool TryGetTarget(in T value, out Entity target)
        {
            target = _runtime.Get(in value);
            return true;
        }
    }
}
