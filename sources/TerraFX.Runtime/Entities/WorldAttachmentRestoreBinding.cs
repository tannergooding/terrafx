// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides scoped access to the exact candidate Entity map during attachment restoration.</summary>
public sealed class WorldAttachmentRestoreBinding
{
    private readonly IWorldAttachmentRestoreContext _context;
    private bool _isActive = true;

    internal WorldAttachmentRestoreBinding(IWorldAttachmentRestoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Resolves a stable identity through the exact candidate map for one declared Entity-reference field.</summary>
    /// <param name="referenceField">The selected Entity-reference field descriptor.</param>
    /// <param name="targetEntityId">The nonempty stable Entity identity from the author value.</param>
    /// <returns>The candidate's live Entity handle.</returns>
    /// <exception cref="ArgumentException">The field or stable Entity identity is invalid.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired, used from another thread, or the candidate map is inconsistent.</exception>
    public Entity ResolveEntity(WorldAttachmentFieldDescriptor referenceField, Guid targetEntityId)
    {
        EnsureActive();

        try
        {
            ArgumentNullException.ThrowIfNull(referenceField);

            if ((referenceField.Field.WireKind != 11)
                || (referenceField.Field.Reference != ContentReferenceKind.Entity)
                || (targetEntityId == Guid.Empty))
            {
                throw new ArgumentException("The selected field or stable Entity identity is invalid.", nameof(referenceField));
            }

            var entity = _context.ResolveEntity(referenceField, targetEntityId);

            if (!entity.IsValid)
            {
                throw new InvalidOperationException("The attachment restore map returned an invalid Entity handle.");
            }

            return entity;
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
            throw _context.RecordFailure(new InvalidOperationException("The World attachment restore binding has expired."));
        }

        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
        {
            throw _context.RecordFailure(new InvalidOperationException("The World attachment restore binding is restricted to its issuing thread."));
        }
    }
}
