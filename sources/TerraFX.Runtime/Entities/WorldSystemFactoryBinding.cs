// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Captures the closed runtime and source-state family for one World system.</summary>
public sealed class WorldSystemFactoryBinding
{
    private readonly IWorldSystemFactoryDispatch _dispatch;

    private WorldSystemFactoryBinding(IWorldSystemFactoryDispatch dispatch)
    {
        _dispatch = dispatch;
    }

    /// <summary>Gets the immutable system factory descriptor.</summary>
    public WorldSystemFactoryDescriptor Descriptor => _dispatch.Descriptor;

    internal IWorldSystemCreateControl CreateControl => _dispatch.CreateControl;

    internal IWorldSystemDefaultsControl DefaultsControl => _dispatch.DefaultsControl;

    internal IWorldSystemFactoryDispatch Dispatch => _dispatch;

    internal IWorldSystemReleaseControl ReleaseControl => _dispatch.ReleaseControl;

    internal IWorldSystemValidationControl ValidationControl => _dispatch.ValidationControl;

    /// <summary>Captures a stateful system's exact schema, field readers, and provider roots.</summary>
    /// <typeparam name="TSystem">The concrete sealed World system type.</typeparam>
    /// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
    /// <typeparam name="TCreate">The concrete system creation callback struct.</typeparam>
    /// <typeparam name="TDefaults">The concrete system defaults callback struct.</typeparam>
    /// <typeparam name="TRelease">The concrete system release callback struct.</typeparam>
    /// <param name="factoryId">The nonempty factory identity.</param>
    /// <param name="factoryVersion">The positive factory version.</param>
    /// <param name="schema">The exact compiled SystemState schema.</param>
    /// <param name="sourceState">The exact closed source-state declaration.</param>
    /// <param name="fields">The complete ordered field binding set.</param>
    /// <param name="create">The typed system creation callback, copied by value.</param>
    /// <param name="defaults">The typed system defaults callback, copied by value.</param>
    /// <param name="codec">The typed system-state codec captured as an immutable provider root.</param>
    /// <param name="release">The typed system release callback, copied by value.</param>
    /// <returns>The immutable stateful factory binding.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">The schema, source leaves, or field closure is inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factoryId"/> or <paramref name="factoryVersion"/> is invalid.</exception>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed system and author types are required by the typed factory registration contract.")]
    public static WorldSystemFactoryBinding Stateful<TSystem, TAuthor, TCreate, TDefaults, TRelease>(
        Guid factoryId,
        int factoryVersion,
        WorldSystemSchema<TSystem> schema,
        WorldSystemSourceStateDeclaration sourceState,
        ImmutableArray<WorldSystemFieldBinding<TSystem, TAuthor>> fields,
        TCreate create,
        TDefaults defaults,
        IWorldSystemStateCodec<TSystem, TAuthor> codec,
        TRelease release)
        where TSystem : class, IWorldSystem
        where TAuthor : notnull
        where TCreate : struct, IWorldSystemCreate<TSystem>
        where TDefaults : struct, IWorldSystemDefaults<TSystem>
        where TRelease : struct, IWorldSystemRelease<TSystem>
    {
        ArgumentOutOfRangeException.ThrowIfEqual(factoryId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factoryVersion);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(sourceState);
        ArgumentNullException.ThrowIfNull(codec);
        ValidateSystemType<TSystem>(sourceState.SystemType, nameof(sourceState));

        var capturedFields = CopyAndValidateFields(schema, sourceState, fields);
        var descriptorFields = ImmutableArray.CreateBuilder<WorldSystemFieldDescriptor>(capturedFields.Length);

        foreach (var field in capturedFields)
        {
            descriptorFields.Add(field.Descriptor);
        }

        var descriptor = new WorldSystemFactoryDescriptor(
            factoryId,
            factoryVersion,
            typeof(TSystem),
            sourceState,
            schema.Schema,
            typeof(TAuthor),
            descriptorFields.MoveToImmutable());
        var family = new WorldSystemFactoryStatefulFamily<TSystem, TAuthor, TCreate, TDefaults, TRelease>(
            descriptor,
            capturedFields,
            create,
            defaults,
            codec,
            release);
        return new WorldSystemFactoryBinding(family);
    }

    /// <summary>Captures a stateless system factory with no authoritative source-state leaves.</summary>
    /// <typeparam name="TSystem">The concrete sealed World system type.</typeparam>
    /// <typeparam name="TCreate">The concrete system creation callback struct.</typeparam>
    /// <typeparam name="TRelease">The concrete system release callback struct.</typeparam>
    /// <param name="factoryId">The nonempty factory identity.</param>
    /// <param name="factoryVersion">The positive factory version.</param>
    /// <param name="sourceState">The exact closed source-state declaration.</param>
    /// <param name="create">The typed system creation callback, copied by value.</param>
    /// <param name="release">The typed system release callback, copied by value.</param>
    /// <returns>The immutable stateless factory binding.</returns>
    /// <exception cref="ArgumentException">The factory ID, type, or source declaration is inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factoryVersion"/> is not positive.</exception>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed system type is required by the typed creation and release roots.")]
    public static WorldSystemFactoryBinding Stateless<TSystem, TCreate, TRelease>(
        Guid factoryId,
        int factoryVersion,
        WorldSystemSourceStateDeclaration sourceState,
        TCreate create,
        TRelease release)
        where TSystem : class, IWorldSystem
        where TCreate : struct, IWorldSystemCreate<TSystem>
        where TRelease : struct, IWorldSystemRelease<TSystem>
    {
        if (factoryId == Guid.Empty)
        {
            throw new ArgumentException("A system factory identity cannot be empty.", nameof(factoryId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factoryVersion);
        ArgumentNullException.ThrowIfNull(sourceState);
        var systemType = typeof(TSystem);

        if ((sourceState.SystemType != systemType)
            || !systemType.IsSealed
            || systemType.IsAbstract
            || systemType.ContainsGenericParameters)
        {
            throw new ArgumentException("A stateless system binding requires its exact concrete sealed type and no open generic parameters.", nameof(sourceState));
        }

        if (!sourceState.Leaves.IsEmpty)
        {
            throw new ArgumentException("A stateless system cannot declare authoritative, derived, or configuration leaves.", nameof(sourceState));
        }

        var descriptor = new WorldSystemFactoryDescriptor(
            factoryId,
            factoryVersion,
            systemType,
            sourceState,
            null,
            null,
            []);
        var family = new WorldSystemFactoryFamily<TSystem, TCreate, TRelease>(descriptor, create, release);
        return new WorldSystemFactoryBinding(family);
    }

    private static ImmutableArray<WorldSystemFieldBinding<TSystem, TAuthor>> CopyAndValidateFields<TSystem, TAuthor>(
        WorldSystemSchema<TSystem> schema,
        WorldSystemSourceStateDeclaration sourceState,
        ImmutableArray<WorldSystemFieldBinding<TSystem, TAuthor>> fields)
        where TSystem : class, IWorldSystem
        where TAuthor : notnull
    {
        if (fields.IsDefault)
        {
            throw new ArgumentException("System field bindings must be initialized.", nameof(fields));
        }

        var expectedFields = new List<WorldSystemFieldDescriptor>();
        EnsureAndAppendFieldDescriptors(schema, schema.Metadata, [], [], expectedFields, nameof(fields));

        if (fields.Length != expectedFields.Count)
        {
            throw new ArgumentException("System field bindings must close every canonical SystemState field.", nameof(fields));
        }

        var authoritativePaths = new HashSet<WorldSystemSourcePath>();

        foreach (var leaf in sourceState.Leaves)
        {
            if (leaf.Purpose == WorldSystemSourcePurpose.Authoritative)
            {
                if (!authoritativePaths.Add(leaf.Path))
                {
                    throw new ArgumentException("Authoritative system source paths must be unique.", nameof(sourceState));
                }
            }
        }

        var capturedFields = ImmutableArray.CreateBuilder<WorldSystemFieldBinding<TSystem, TAuthor>>(fields.Length);
        var usedRuntimePaths = new HashSet<WorldSystemSourcePath>();
        var usedAuthorPaths = new HashSet<WorldSystemSourcePath>();

        for (var index = 0; index < fields.Length; index++)
        {
            var binding = fields[index]
                ?? throw new ArgumentException("System field bindings cannot contain null entries.", nameof(fields));
            var descriptor = binding.Descriptor;
            var expected = expectedFields[index];

            if ((descriptor.StateSchema != schema.Schema)
                || !ReferenceEquals(descriptor.Schemas, schema.Schemas)
                || !ReferenceEquals(descriptor.DataSchema, expected.DataSchema)
                || !ReferenceEquals(descriptor.Field, expected.Field)
                || !descriptor.Path.Segments.AsSpan().SequenceEqual(expected.Path.Segments.AsSpan()))
            {
                throw new ArgumentException("System field bindings must match every SystemState field in canonical order.", nameof(fields));
            }

            foreach (var runtimePath in binding.RuntimeSourcePaths)
            {
                var sourceLeaf = FindSourceLeaf(sourceState.Leaves, runtimePath);

                if ((sourceLeaf is null)
                    || (sourceLeaf.Purpose != WorldSystemSourcePurpose.Authoritative)
                    || (sourceLeaf.ValueType != binding.ValueType)
                    || !usedRuntimePaths.Add(runtimePath))
                {
                    throw new ArgumentException("Every runtime field source path must select one unique authoritative source leaf of the exact value type.", nameof(fields));
                }
            }

            foreach (var authorPath in binding.AuthorSourcePaths)
            {
                if (!usedAuthorPaths.Add(authorPath))
                {
                    throw new ArgumentException("Author source paths cannot be selected by more than one canonical field.", nameof(fields));
                }
            }

            capturedFields.Add(binding);
        }

        if (usedRuntimePaths.Count != authoritativePaths.Count)
        {
            throw new ArgumentException("System field bindings must cover every authoritative source leaf exactly once.", nameof(fields));
        }

        return capturedFields.MoveToImmutable();
    }

    private static void EnsureAndAppendFieldDescriptors<TSystem>(
        WorldSystemSchema<TSystem> schema,
        DataSchema currentSchema,
        List<ComponentPropertySegment> prefix,
        List<DataSchema> schemaStack,
        List<WorldSystemFieldDescriptor> fields,
        string parameterName)
        where TSystem : class, IWorldSystem
    {
        foreach (var activeSchema in schemaStack)
        {
            if (ReferenceEquals(activeSchema, currentSchema))
            {
                throw new ArgumentException("The selected SystemState schema contains a recursive record closure.", parameterName);
            }
        }

        schemaStack.Add(currentSchema);

        foreach (var field in currentSchema.Fields)
        {
            if (prefix.Count >= 64)
            {
                throw new ArgumentException("The selected SystemState schema exceeds the supported field-path depth.", parameterName);
            }

            prefix.Add(ComponentPropertySegment.FieldTag(field.Tag));

            if ((field.WireKind == 22)
                && field.IsRequired
                && (field.Reference == ContentReferenceKind.None)
                && schema.TryResolveNestedSchema(field, out var nestedSchema))
            {
                EnsureAndAppendFieldDescriptors(schema, nestedSchema, prefix, schemaStack, fields, parameterName);
            }
            else if (field.WireKind is not 22 and not 23 and not 24)
            {
                fields.Add(schema.GetField(new ComponentFieldPath([.. prefix])));
            }
            else
            {
                throw new ArgumentException("SystemState records must resolve to required scalar fields.", parameterName);
            }

            prefix.RemoveAt(prefix.Count - 1);
        }

        schemaStack.RemoveAt(schemaStack.Count - 1);
    }

    private static WorldSystemSourceLeaf? FindSourceLeaf(
        ImmutableArray<WorldSystemSourceLeaf> sourceLeaves,
        WorldSystemSourcePath path)
    {
        foreach (var sourceLeaf in sourceLeaves)
        {
            if (sourceLeaf.Path.Equals(path))
            {
                return sourceLeaf;
            }
        }

        return null;
    }

    private static void ValidateSystemType<TSystem>(Type sourceSystemType, string parameterName)
        where TSystem : class, IWorldSystem
    {
        var systemType = typeof(TSystem);

        if ((sourceSystemType != systemType)
            || !systemType.IsSealed
            || systemType.IsAbstract
            || systemType.ContainsGenericParameters)
        {
            throw new ArgumentException("A stateful system binding requires its exact concrete sealed type and no open generic parameters.", parameterName);
        }
    }
}
