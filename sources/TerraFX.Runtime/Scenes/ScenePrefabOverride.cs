// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Scenes;

internal sealed class ScenePrefabOverride
{
    internal ScenePrefabOverride(
        SceneEntitySelector target,
        ContentSchemaReference componentSchema,
        ImmutableArray<ScenePropertyPathSegment> propertyPath,
        ScenePrefabOverrideOperation operation,
        SceneAuthoredRecord? value,
        SceneReferenceTarget? referenceValue)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(componentSchema.SchemaId, Guid.Empty);

        if (propertyPath.IsDefaultOrEmpty || (propertyPath.Length > 64))
        {
            throw new ArgumentException("The Prefab override property path must contain 1..64 initialized segments.", nameof(propertyPath));
        }

        if (operation is not ScenePrefabOverrideOperation.Set and not ScenePrefabOverrideOperation.Omit)
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if ((operation == ScenePrefabOverrideOperation.Set) && (value is null))
        {
            throw new ArgumentException("A Set Prefab override requires a value record.", nameof(value));
        }

        if ((operation == ScenePrefabOverrideOperation.Omit) && ((value is not null) || (referenceValue is not null)))
        {
            throw new ArgumentException("An Omit Prefab override cannot carry a value or reference target.");
        }

        Target = target;
        ComponentSchema = componentSchema;
        PropertyPath = [.. propertyPath];
        Operation = operation;
        Value = value;
        ReferenceValue = referenceValue;
    }

    internal ContentSchemaReference ComponentSchema { get; }

    internal ScenePrefabOverrideOperation Operation { get; }

    internal ImmutableArray<ScenePropertyPathSegment> PropertyPath { get; }

    internal SceneReferenceTarget? ReferenceValue { get; }

    internal SceneEntitySelector Target { get; }

    internal SceneAuthoredRecord? Value { get; }
}
