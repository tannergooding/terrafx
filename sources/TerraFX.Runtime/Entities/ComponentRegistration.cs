// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class ComponentRegistration<T> : IComponentRegistration
    where T : unmanaged
{
    internal ComponentRegistration(ComponentSchema<T> schema, ComponentFactoryBinding<T> binding)
    {
        Schema = schema;
        Binding = binding;
    }

    public Type ComponentType => typeof(T);

    public ComponentFactoryDescriptor Descriptor => Binding.Descriptor;

    public ComponentLayout Layout => Schema.Layout;

    public ComponentSchema<T> Schema { get; }

    public ContentSchemaReference SchemaReference => Schema.Schema;

    internal ComponentFactoryBinding<T> Binding { get; }

    internal static Span<T> GetColumnSpan(object column, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return ((T[])column).AsSpan(start, length);
    }

    internal static ReadOnlySpan<T> GetReadOnlyColumnSpan(object column, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return ((T[])column).AsSpan(start, length);
    }

    public ComponentInitialization CaptureColumnRow(object column, int row)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        var value = ((T[])column)[row];
        return ComponentInitialization.Value(in value);
    }

    public void ClearColumn(object column, int row)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ((T[])column)[row] = default;
    }

    public void CopyColumnRow(object sourceColumn, int sourceRow, object destinationColumn, int destinationRow)
    {
        ArgumentNullException.ThrowIfNull(sourceColumn);
        ArgumentNullException.ThrowIfNull(destinationColumn);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRow);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationRow);
        ((T[])destinationColumn)[destinationRow] = ((T[])sourceColumn)[sourceRow];
    }

    public object CreateColumn(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        return new T[capacity];
    }

    public ComponentInitialization CreateInitializedValue(ComponentInitialization initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);

        if (initialization.ComponentType != typeof(T))
        {
            throw new ArgumentException("The component initialization does not match this registered type.", nameof(initialization));
        }

        if (!initialization.IsDefault)
        {
            if (initialization.TryGetValue<T>(out _))
            {
                return initialization;
            }

            throw new InvalidOperationException("The typed component initialization did not provide its registered value.");
        }

        var value = CreateDefault();
        return ComponentInitialization.Value(in value);
    }

    public long GetColumnArrayBytes(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        return checked(64L + ((checked((long)capacity * Unsafe.SizeOf<T>()) + 7L) & ~7L));
    }

    public void InitializeColumn(object column, int row, ComponentInitialization initialization)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentNullException.ThrowIfNull(initialization);

        if (initialization.ComponentType != typeof(T))
        {
            throw new ArgumentException("The component initialization does not match this registered type.", nameof(initialization));
        }

        var values = (T[])column;

        if (initialization.IsDefault)
        {
            values[row] = CreateDefault();
        }
        else if (initialization.TryGetValue<T>(out var value))
        {
            values[row] = value;
        }
        else
        {
            throw new InvalidOperationException("The typed component initialization did not provide its registered value.");
        }
    }

    public bool IsEntityReferencePath(ComponentFieldPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Binding.IsEntityReferencePath(path);
    }

    public void MoveColumnRow(object column, int sourceRow, int destinationRow)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRow);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationRow);
        var values = (T[])column;
        values[destinationRow] = values[sourceRow];
    }

    public ComponentInitialization SetEntityReference(
        ComponentInitialization current,
        ComponentFieldPath path,
        Entity? target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(path);

        if ((current.ComponentType != typeof(T))
            || current.IsDefault
            || !current.TryGetValue<T>(out var value))
        {
            throw new InvalidOperationException("The selected component reference update does not have a resolved typed value.");
        }

        if (!Binding.TrySetEntityReference(ref value, path, target))
        {
            throw new ArgumentException("The selected component field path is not a registered Entity-reference field.", nameof(path));
        }

        return ComponentInitialization.Value(in value);
    }

    public ImmutableArray<Diagnostic> ValidateColumnRow(object column, int row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        var value = ((T[])column)[row];
        return Binding.Validate(in value, cancellationToken);
    }

    public void ValidateEntityReferencesColumnRow(
        object column,
        int row,
        World world,
        Entity? excludedEntity,
        string? parameterName)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        var value = ((T[])column)[row];
        Binding.ValidateEntityReferences(in value, world, excludedEntity, parameterName);
    }

    public void ValidateEntityReferencesInitialization(
        ComponentInitialization initialization,
        World world,
        Entity? excludedEntity,
        string? parameterName)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(world);

        if (initialization.IsDefault || !initialization.TryGetValue<T>(out var value))
        {
            throw new InvalidOperationException("The selected component initialization has no resolved typed value.");
        }

        Binding.ValidateEntityReferences(in value, world, excludedEntity, parameterName);
    }

    public void ValidateEntityReferencesValue<TValue>(
        in TValue value,
        World world,
        Entity? excludedEntity,
        string? parameterName)
        where TValue : unmanaged
    {
        ArgumentNullException.ThrowIfNull(world);

        if (typeof(TValue) != typeof(T))
        {
            throw new InvalidOperationException("The selected reference value does not match the registered component type.");
        }

        ref var mutableValue = ref Unsafe.AsRef(in value);
        ref var typedValue = ref Unsafe.As<TValue, T>(ref mutableValue);
        Binding.ValidateEntityReferences(in typedValue, world, excludedEntity, parameterName);
    }

    public ImmutableArray<Diagnostic> ValidateInitialization(
        ComponentInitialization initialization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initialization);

        if (initialization.IsDefault || !initialization.TryGetValue<T>(out var value))
        {
            throw new InvalidOperationException("The selected component initialization has no resolved typed value.");
        }

        return Binding.Validate(in value, cancellationToken);
    }

    public ImmutableArray<Diagnostic> ValidateValue<TValue>(in TValue value, CancellationToken cancellationToken)
        where TValue : unmanaged
    {
        if (typeof(TValue) != typeof(T))
        {
            throw new InvalidOperationException("The selected validation value does not match the registered component type.");
        }

        ref var mutableValue = ref Unsafe.AsRef(in value);
        ref var typedValue = ref Unsafe.As<TValue, T>(ref mutableValue);
        return Binding.Validate(in typedValue, cancellationToken);
    }

    internal T CreateDefault()
        => Binding.CreateDefault();
}
