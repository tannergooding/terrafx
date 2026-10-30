// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Numerics;

namespace TerraFX.Entities;

/// <summary>Binds one declared system-state field to concrete system and author readers.</summary>
/// <typeparam name="TSystem">The exact closed World system type.</typeparam>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed system and author types are required by the typed accessor factory contract.")]
public sealed class WorldSystemFieldBinding<TSystem, TAuthor>
    where TSystem : class, IWorldSystem
    where TAuthor : notnull
{
    private readonly IWorldSystemFieldAccessor<TSystem, TAuthor> _accessorState;
    private readonly ImmutableArray<WorldSystemSourcePath> _authorSourcePaths;
    private readonly WorldSystemFieldDescriptor _descriptor;
    private readonly WorldSystemFieldBindingKind _kind;
    private readonly ImmutableArray<WorldSystemSourcePath> _runtimeSourcePaths;
    private readonly Type _valueType;

    private WorldSystemFieldBinding(
        WorldSystemFieldDescriptor descriptor,
        WorldSystemFieldBindingKind kind,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        IWorldSystemFieldAccessor<TSystem, TAuthor> accessorState,
        Type valueType)
    {
        _descriptor = descriptor;
        _kind = kind;
        _runtimeSourcePaths = runtimeSourcePaths;
        _authorSourcePaths = authorSourcePaths;
        _accessorState = accessorState;
        _valueType = valueType;
    }

    /// <summary>Gets the author-model source paths selected by the reader.</summary>
    public ImmutableArray<WorldSystemSourcePath> AuthorSourcePaths => _authorSourcePaths;

    /// <summary>Gets the selected Core system-state field descriptor.</summary>
    public WorldSystemFieldDescriptor Descriptor => _descriptor;

    /// <summary>Gets the runtime system source paths selected by the reader.</summary>
    public ImmutableArray<WorldSystemSourcePath> RuntimeSourcePaths => _runtimeSourcePaths;

    internal WorldSystemFieldBindingKind Kind => _kind;

    internal Type ValueType => _valueType;

    /// <summary>Creates a required live-Entity reference binding.</summary>
    /// <typeparam name="TRuntime">The concrete runtime accessor struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete author reader struct.</typeparam>
    /// <param name="field">The selected required Entity-reference field.</param>
    /// <param name="runtimeSourcePaths">The exact runtime source paths.</param>
    /// <param name="authorSourcePaths">The exact author source paths.</param>
    /// <param name="runtime">The concrete runtime reader, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable Entity-reference field binding.</returns>
    /// <exception cref="ArgumentException">The field role or source paths are incompatible.</exception>
    public static WorldSystemFieldBinding<TSystem, TAuthor> EntityReference<TRuntime, TAuthorReader>(
        WorldSystemFieldDescriptor field,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IWorldSystemValueReader<TSystem, Entity>
        where TAuthorReader : struct, IWorldSystemValueReader<TAuthor, Guid>
        => Create(
            field,
            WorldSystemFieldBindingKind.EntityReference,
            runtimeSourcePaths,
            authorSourcePaths,
            new RequiredAccessors<Entity, Guid, TRuntime, TAuthorReader>(runtime, author),
            wireKind: 11,
            isRequired: true,
            reference: ContentReferenceKind.Entity,
            valueType: typeof(Entity));

    /// <summary>Creates an optional scalar field binding.</summary>
    /// <typeparam name="TValue">The unmanaged field value.</typeparam>
    /// <typeparam name="TRuntime">The concrete optional runtime reader struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete optional author reader struct.</typeparam>
    /// <param name="field">The selected optional scalar field.</param>
    /// <param name="runtimeSourcePaths">The exact runtime source paths.</param>
    /// <param name="authorSourcePaths">The exact author source paths.</param>
    /// <param name="runtime">The concrete runtime reader, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable optional field binding.</returns>
    /// <exception cref="ArgumentException">The field kind, reference role, or source paths are incompatible.</exception>
    public static WorldSystemFieldBinding<TSystem, TAuthor> Optional<TValue, TRuntime, TAuthorReader>(
        WorldSystemFieldDescriptor field,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IWorldSystemOptionalReader<TSystem, TValue>
        where TAuthorReader : struct, IWorldSystemOptionalReader<TAuthor, TValue>
        => Create(
            field,
            WorldSystemFieldBindingKind.Optional,
            runtimeSourcePaths,
            authorSourcePaths,
            new OptionalAccessors<TValue, TValue, TRuntime, TAuthorReader>(runtime, author),
            GetWireKind<TValue>(),
            isRequired: false,
            reference: ContentReferenceKind.None,
            valueType: typeof(TValue?));

    /// <summary>Creates an optional live-Entity reference binding.</summary>
    /// <typeparam name="TRuntime">The concrete optional runtime reader struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete optional author reader struct.</typeparam>
    /// <param name="field">The selected optional Entity-reference field.</param>
    /// <param name="runtimeSourcePaths">The exact runtime source paths.</param>
    /// <param name="authorSourcePaths">The exact author source paths.</param>
    /// <param name="runtime">The concrete runtime reader, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable optional Entity-reference field binding.</returns>
    /// <exception cref="ArgumentException">The field role or source paths are incompatible.</exception>
    public static WorldSystemFieldBinding<TSystem, TAuthor> OptionalEntityReference<TRuntime, TAuthorReader>(
        WorldSystemFieldDescriptor field,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TRuntime : struct, IWorldSystemOptionalReader<TSystem, Entity>
        where TAuthorReader : struct, IWorldSystemOptionalReader<TAuthor, Guid>
        => Create(
            field,
            WorldSystemFieldBindingKind.OptionalEntityReference,
            runtimeSourcePaths,
            authorSourcePaths,
            new OptionalAccessors<Entity, Guid, TRuntime, TAuthorReader>(runtime, author),
            wireKind: 11,
            isRequired: false,
            reference: ContentReferenceKind.Entity,
            valueType: typeof(Entity?));

    /// <summary>Creates a required scalar field binding.</summary>
    /// <typeparam name="TValue">The unmanaged field value.</typeparam>
    /// <typeparam name="TRuntime">The concrete runtime reader struct.</typeparam>
    /// <typeparam name="TAuthorReader">The concrete author reader struct.</typeparam>
    /// <param name="field">The selected required scalar field.</param>
    /// <param name="runtimeSourcePaths">The exact runtime source paths.</param>
    /// <param name="authorSourcePaths">The exact author source paths.</param>
    /// <param name="runtime">The concrete runtime reader, copied by value.</param>
    /// <param name="author">The concrete author reader, copied by value.</param>
    /// <returns>The immutable scalar field binding.</returns>
    /// <exception cref="ArgumentException">The field kind, reference role, or source paths are incompatible.</exception>
    public static WorldSystemFieldBinding<TSystem, TAuthor> Scalar<TValue, TRuntime, TAuthorReader>(
        WorldSystemFieldDescriptor field,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        TRuntime runtime,
        TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IWorldSystemValueReader<TSystem, TValue>
        where TAuthorReader : struct, IWorldSystemValueReader<TAuthor, TValue>
        => Create(
            field,
            WorldSystemFieldBindingKind.Scalar,
            runtimeSourcePaths,
            authorSourcePaths,
            new RequiredAccessors<TValue, TValue, TRuntime, TAuthorReader>(runtime, author),
            GetWireKind<TValue>(),
            isRequired: true,
            reference: ContentReferenceKind.None,
            valueType: typeof(TValue));

    private static ImmutableArray<WorldSystemSourcePath> CopyPaths(
        ImmutableArray<WorldSystemSourcePath> paths,
        string parameterName)
    {
        if (paths.IsDefaultOrEmpty)
        {
            throw new ArgumentException("System field source paths must be initialized and nonempty.", parameterName);
        }

        var copiedPaths = ImmutableArray.CreateBuilder<WorldSystemSourcePath>(paths.Length);

        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index]
                ?? throw new ArgumentException("System field source paths cannot contain null entries.", parameterName);

            for (var previousIndex = 0; previousIndex < index; previousIndex++)
            {
                if (path.Equals(paths[previousIndex]))
                {
                    throw new ArgumentException("System field source paths must be unique.", parameterName);
                }
            }

            copiedPaths.Add(path);
        }

        return copiedPaths.MoveToImmutable();
    }

    private static WorldSystemFieldBinding<TSystem, TAuthor> Create(
        WorldSystemFieldDescriptor field,
        WorldSystemFieldBindingKind kind,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        IWorldSystemFieldAccessor<TSystem, TAuthor> accessorState,
        ushort wireKind,
        bool isRequired,
        ContentReferenceKind reference,
        Type valueType)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(accessorState);
        ValidateField(field, wireKind, isRequired, reference);
        var capturedRuntimePaths = CopyPaths(runtimeSourcePaths, nameof(runtimeSourcePaths));
        var capturedAuthorPaths = CopyPaths(authorSourcePaths, nameof(authorSourcePaths));
        return new WorldSystemFieldBinding<TSystem, TAuthor>(
            field,
            kind,
            capturedRuntimePaths,
            capturedAuthorPaths,
            accessorState,
            valueType);
    }

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

    private static void ValidateField(
        WorldSystemFieldDescriptor field,
        ushort wireKind,
        bool isRequired,
        ContentReferenceKind reference)
    {
        if ((field.StateSchema.Scope != ContentSchemaScope.Content)
            || !field.Schemas.TryGetSchema(field.StateSchema, out var stateSchema)
            || (stateSchema.Role != ContentSchemaRole.SystemState)
            || !field.Schemas.TryGetSchema(field.DataSchema.Schema, out var registeredFieldSchema)
            || !ReferenceEquals(registeredFieldSchema, field.DataSchema)
            || (field.Field.WireKind != wireKind)
            || (field.Field.IsRequired != isRequired)
            || (field.Field.Reference != reference))
        {
            throw new ArgumentException("The selected field does not match the exact SystemState declaration.", nameof(field));
        }

        if (wireKind == 0)
        {
            throw new ArgumentException("The selected field value type has no supported canonical wire kind.", nameof(field));
        }
    }

    internal void ValidateAuthor(in TAuthor author)
        => _accessorState.ValidateAuthor(in author);

    internal void ValidateRuntime(in TSystem system)
        => _accessorState.ValidateRuntime(in system);

    private readonly struct OptionalAccessors<TSystemValue, TAuthorValue, TRuntime, TAuthorReader> :
        IWorldSystemFieldAccessor<TSystem, TAuthor>
        where TSystemValue : unmanaged
        where TAuthorValue : unmanaged
        where TRuntime : struct, IWorldSystemOptionalReader<TSystem, TSystemValue>
        where TAuthorReader : struct, IWorldSystemOptionalReader<TAuthor, TAuthorValue>
    {
        private readonly TAuthorReader _author;
        private readonly TRuntime _runtime;

        internal OptionalAccessors(TRuntime runtime, TAuthorReader author)
        {
            _runtime = runtime;
            _author = author;
        }

        public void ValidateAuthor(in TAuthor author)
        {
            if (_author.HasValue(in author))
            {
                _ = _author.GetPresent(in author);
            }
        }

        public void ValidateRuntime(in TSystem system)
        {
            if (_runtime.HasValue(in system))
            {
                _ = _runtime.GetPresent(in system);
            }
        }
    }

    private readonly struct RequiredAccessors<TSystemValue, TAuthorValue, TRuntime, TAuthorReader> :
        IWorldSystemFieldAccessor<TSystem, TAuthor>
        where TSystemValue : unmanaged
        where TAuthorValue : unmanaged
        where TRuntime : struct, IWorldSystemValueReader<TSystem, TSystemValue>
        where TAuthorReader : struct, IWorldSystemValueReader<TAuthor, TAuthorValue>
    {
        private readonly TAuthorReader _author;
        private readonly TRuntime _runtime;

        internal RequiredAccessors(TRuntime runtime, TAuthorReader author)
        {
            _runtime = runtime;
            _author = author;
        }

        public void ValidateAuthor(in TAuthor author)
            => _ = _author.Invoke(in author);

        public void ValidateRuntime(in TSystem system)
            => _ = _runtime.Invoke(in system);
    }
}
