// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides operation-scoped field access for attachment capture callbacks.</summary>
public sealed class WorldAttachmentCaptureBinding
{
    private readonly IWorldAttachmentCaptureContext _context;
    private bool _isActive = true;

    internal WorldAttachmentCaptureBinding(IWorldAttachmentCaptureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Reads the stable identity for one declared live-Entity reference.</summary>
    /// <param name="referenceField">The selected Entity-reference field descriptor.</param>
    /// <param name="entity">The live same-World Entity.</param>
    /// <returns>The nonempty stable Entity identity.</returns>
    /// <exception cref="ArgumentException">The field or Entity does not match this capture operation.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired, used from another thread, or the Entity has no stable identity.</exception>
    public Guid GetEntityId(WorldAttachmentFieldDescriptor referenceField, Entity entity)
    {
        EnsureActive();

        try
        {
            ArgumentNullException.ThrowIfNull(referenceField);

            if ((referenceField.Field.WireKind != 11)
                || (referenceField.Field.Reference != ContentReferenceKind.Entity)
                || !entity.IsValid)
            {
                throw new ArgumentException("The selected field or Entity is not valid for this attachment capture.", nameof(referenceField));
            }

            var entityId = _context.GetEntityId(referenceField, entity);

            if (entityId == Guid.Empty)
            {
                throw new InvalidOperationException("The attachment capture context returned an empty Entity identity.");
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
            throw _context.RecordFailure(new InvalidOperationException("The World attachment capture binding has expired."));
        }

        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
        {
            throw _context.RecordFailure(new InvalidOperationException("The World attachment capture binding is restricted to its issuing thread."));
        }
    }
}
