// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Provides scoped field-validation operations for one component value.</summary>
public sealed class ComponentValidationBinding
{
    private readonly IComponentValidationContext _context;
    private bool _isActive = true;

    internal ComponentValidationBinding(IComponentValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Validates the declared schema fields of one unmanaged value.</summary>
    /// <typeparam name="TValue">The unmanaged value type.</typeparam>
    /// <param name="value">The value whose declared fields are checked.</param>
    /// <returns>The immutable diagnostics for invalid declared fields.</returns>
    /// <exception cref="InvalidOperationException">The binding is expired, used from another thread, or the engine context returned an invalid diagnostic array.</exception>
    public ImmutableArray<Diagnostic> ValidateFields<TValue>(in TValue value)
        where TValue : unmanaged
    {
        EnsureActive();

        var diagnostics = _context.ValidateFields(in value);

        if (diagnostics.IsDefault)
        {
            throw new InvalidOperationException("The validation context returned a default diagnostic array.");
        }

        return ImmutableArrayCopy.Create(diagnostics);
    }

    internal void Revoke() => _isActive = false;

    private void EnsureActive()
    {
        if (!_isActive || !_context.IsActive)
        {
            throw new InvalidOperationException("The component validation binding has expired.");
        }

        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
        {
            throw new InvalidOperationException("The component validation binding is restricted to its issuing thread.");
        }
    }
}
