// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides operation-scoped canonical field access for one system codec.</summary>
public sealed class WorldSystemCaptureBinding
{
    private readonly IWorldSystemCaptureContext _context;
    private bool _isActive = true;

    internal WorldSystemCaptureBinding(IWorldSystemCaptureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Gets the exact SystemState schema tuple.</summary>
    public ContentSchemaReference Schema
    {
        get
        {
            EnsureActive();
            return _context.Schema;
        }
    }

    /// <summary>Gets the stable identity of the system being captured.</summary>
    public Guid SystemId
    {
        get
        {
            EnsureActive();
            return _context.SystemId;
        }
    }

    /// <summary>Reads the stable identity for one declared live-Entity reference.</summary>
    /// <param name="referenceField">The selected Entity-reference field descriptor.</param>
    /// <param name="entity">The live same-World Entity.</param>
    /// <returns>The nonempty stable Entity identity.</returns>
    /// <exception cref="ArgumentException">The field or Entity does not match this capture operation.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired, used from another thread, or the Entity has no stable identity.</exception>
    public Guid GetEntityId(WorldSystemFieldDescriptor referenceField, Entity entity)
    {
        EnsureActive();

        try
        {
            ArgumentNullException.ThrowIfNull(referenceField);

            if ((referenceField.StateSchema != _context.Schema)
                || (referenceField.Field.WireKind != 11)
                || (referenceField.Field.Reference != ContentReferenceKind.Entity)
                || !entity.IsValid)
            {
                throw new ArgumentException("The selected field or Entity is not valid for this system capture.", nameof(referenceField));
            }

            var entityId = _context.GetEntityId(referenceField, entity);

            if (entityId == Guid.Empty)
            {
                throw new InvalidOperationException("The system capture context returned an empty Entity identity.");
            }

            return entityId;
        }
        catch (Exception exception)
        {
            throw _context.RecordFailure(exception);
        }
    }

    internal void Revoke() => _isActive = false;

    private void EnsureActive()
    {
        if (!_isActive || !_context.IsActive)
        {
            throw _context.RecordFailure(new InvalidOperationException("The World-system capture binding has expired."));
        }

        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
        {
            throw _context.RecordFailure(new InvalidOperationException("The World-system capture binding is restricted to its issuing thread."));
        }
    }
}
