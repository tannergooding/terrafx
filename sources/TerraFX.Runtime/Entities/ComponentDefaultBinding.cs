// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides scoped authority for one component default-provider invocation.</summary>
public sealed class ComponentDefaultBinding
{
    private readonly IComponentDefaultContext _context;
    private bool _isActive = true;

    internal ComponentDefaultBinding(IComponentDefaultContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    internal void EnsureActive()
    {
        if (!_isActive || !_context.IsActive)
        {
            throw new InvalidOperationException("The component default binding has expired.");
        }

        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
        {
            throw new InvalidOperationException("The component default binding is restricted to its issuing thread.");
        }
    }

    internal void Revoke() => _isActive = false;
}
