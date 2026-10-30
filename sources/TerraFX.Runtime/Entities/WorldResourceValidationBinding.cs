// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Provides operation-scoped schema-field validation access for one resource codec.</summary>
public sealed class WorldResourceValidationBinding
{
    private readonly WorldResourceOperationControl _operation;

    internal WorldResourceValidationBinding(WorldResourceOperationControl operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Operation != WorldResourceOperationKind.Validation)
        {
            throw new InvalidOperationException("A resource validation binding requires a validation operation.");
        }

        _operation = operation;
    }

    /// <summary>Validates any common schema-field constraints for the selected partitions.</summary>
    /// <typeparam name="T">The unmanaged resource partition type.</typeparam>
    /// <param name="value">The scoped read-only resource state view.</param>
    /// <returns>The initialized immutable diagnostics.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> or the view does not match this resource operation.</exception>
    /// <exception cref="InvalidOperationException">The binding or view has expired.</exception>
    public ImmutableArray<Diagnostic> ValidateFields<T>(scoped WorldResourceStateView<T> value)
        where T : unmanaged
    {
        try
        {
            _operation.EnsureActive(WorldResourceOperationKind.Validation);

            if ((_operation.PartitionType != typeof(T)) || !ReferenceEquals(value.Operation, _operation))
            {
                throw new ArgumentException("The resource state view does not match this validation binding.", nameof(value));
            }

            return [];
        }
        catch (Exception exception)
        {
            throw _operation.RecordFailure(exception);
        }
    }

    internal void Revoke() => _operation.Revoke();
}
