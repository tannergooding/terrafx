// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal interface IComponentRegistration
{
    Type ComponentType { get; }

    ComponentFactoryDescriptor Descriptor { get; }

    ComponentLayout Layout { get; }

    ContentSchemaReference SchemaReference { get; }

    ComponentInitialization CaptureColumnRow(object column, int row);

    void ClearColumn(object column, int row);

    void CopyColumnRow(object sourceColumn, int sourceRow, object destinationColumn, int destinationRow);

    object CreateColumn(int capacity);

    ComponentInitialization CreateInitializedValue(ComponentInitialization initialization);

    long GetColumnArrayBytes(int capacity);

    void InitializeColumn(object column, int row, ComponentInitialization initialization);

    bool IsEntityReferencePath(ComponentFieldPath path);

    void MoveColumnRow(object column, int sourceRow, int destinationRow);

    ComponentInitialization SetEntityReference(
        ComponentInitialization current,
        ComponentFieldPath path,
        Entity? target);

    ImmutableArray<Diagnostic> ValidateColumnRow(object column, int row, CancellationToken cancellationToken);

    void ValidateEntityReferencesColumnRow(
        object column,
        int row,
        World world,
        Entity? excludedEntity,
        string? parameterName);

    void ValidateEntityReferencesInitialization(
        ComponentInitialization initialization,
        World world,
        Entity? excludedEntity,
        string? parameterName);

    void ValidateEntityReferencesValue<TValue>(
        in TValue value,
        World world,
        Entity? excludedEntity,
        string? parameterName)
        where TValue : unmanaged;

    ImmutableArray<Diagnostic> ValidateInitialization(
        ComponentInitialization initialization,
        CancellationToken cancellationToken);

    ImmutableArray<Diagnostic> ValidateValue<TValue>(in TValue value, CancellationToken cancellationToken)
        where TValue : unmanaged;
}
