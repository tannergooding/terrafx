// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides scoped access to the exact candidate Entity identity map during capture.</summary>
public sealed class ComponentCaptureBinding
{
    private readonly IComponentCaptureContext _context;
    private bool _isActive = true;

    internal ComponentCaptureBinding(IComponentCaptureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Gets the stable Entity identity for one declared Entity-reference field.</summary>
    /// <param name="referenceField">The selected Entity-reference field descriptor.</param>
    /// <param name="entity">The copied live Entity handle to map.</param>
    /// <returns>The stable Entity identity in the exact capture map.</returns>
    /// <exception cref="ArgumentException">The field or Entity handle is invalid or absent from the capture map.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired or used from another thread.</exception>
    public Guid GetEntityId(ComponentFieldDescriptor referenceField, Entity entity)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(referenceField);

        if ((referenceField.Field.WireKind != 11) || (referenceField.Field.Reference != ContentReferenceKind.Entity))
        {
            throw new ArgumentException("The selected field is not a declared Entity reference.", nameof(referenceField));
        }

        if (!entity.IsValid)
        {
            throw new ArgumentException("A valid live Entity handle is required.", nameof(entity));
        }

        var entityId = _context.GetEntityId(referenceField, entity);

        if (entityId == Guid.Empty)
        {
            throw new InvalidOperationException("The capture map returned an empty Entity identity.");
        }

        return entityId;
    }

    internal void Revoke() => _isActive = false;

    private void EnsureActive()
    {
        if (!_isActive || !_context.IsActive)
        {
            throw new InvalidOperationException("The component capture binding has expired.");
        }

        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
        {
            throw new InvalidOperationException("The component capture binding is restricted to its issuing thread.");
        }
    }
}
