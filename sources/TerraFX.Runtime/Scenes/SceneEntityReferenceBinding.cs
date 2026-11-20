// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Scenes;

internal sealed class SceneEntityReferenceBinding
{
    internal SceneEntityReferenceBinding(
        SceneEntitySelector source,
        ContentSchemaReference componentSchema,
        ImmutableArray<ScenePropertyPathSegment> propertyPath,
        SceneReferenceTarget target)
        : this(source, componentSchema, propertyPath, target, copyPropertyPath: true)
    {
    }

    private SceneEntityReferenceBinding(
        SceneEntitySelector source,
        ContentSchemaReference componentSchema,
        ImmutableArray<ScenePropertyPathSegment> propertyPath,
        SceneReferenceTarget target,
        bool copyPropertyPath)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(componentSchema.SchemaId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(target);

        if (propertyPath.IsDefaultOrEmpty || (propertyPath.Length > 64))
        {
            throw new ArgumentException("The Entity-reference property path must contain 1..64 initialized segments.", nameof(propertyPath));
        }

        Source = source;
        ComponentSchema = componentSchema;
        PropertyPath = copyPropertyPath ? [.. propertyPath] : propertyPath;
        Target = target;
    }

    internal ContentSchemaReference ComponentSchema { get; }

    internal ImmutableArray<ScenePropertyPathSegment> PropertyPath { get; }

    internal SceneEntitySelector Source { get; }

    internal SceneReferenceTarget Target { get; }

    internal static SceneEntityReferenceBinding CreateOwned(
        SceneEntitySelector source,
        ContentSchemaReference componentSchema,
        ImmutableArray<ScenePropertyPathSegment> propertyPath,
        SceneReferenceTarget target)
        => new(source, componentSchema, propertyPath, target, copyPropertyPath: false);
}
