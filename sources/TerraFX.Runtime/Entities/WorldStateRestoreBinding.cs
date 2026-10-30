// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides scoped access to the exact candidate Entity map during component restoration.</summary>
public sealed class WorldStateRestoreBinding
{
    private readonly IComponentRestoreContext _context;
    private bool _isActive = true;

    internal WorldStateRestoreBinding(IComponentRestoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Reads one exact Persistent component on the entity currently being restored as Derived state.</summary>
    /// <typeparam name="TDependency">The unmanaged dependency component type.</typeparam>
    /// <returns>A copy of the exact dependency component value.</returns>
    /// <exception cref="InvalidOperationException">The binding is expired, used from another thread, or the dependency is unavailable.</exception>
    public TDependency ReadSameEntity<TDependency>()
        where TDependency : unmanaged
    {
        EnsureActive();
        return _context.ReadSameEntity<TDependency>();
    }

    /// <summary>Resolves a stable identity through the exact candidate map for one declared Entity-reference field.</summary>
    /// <param name="referenceField">The selected Entity-reference field descriptor.</param>
    /// <param name="targetEntityId">The nonempty stable Entity identity from the author value.</param>
    /// <returns>The candidate's live Entity handle.</returns>
    /// <exception cref="ArgumentException">The field or stable Entity identity is invalid.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired, used from another thread, or the candidate map is inconsistent.</exception>
    public Entity ResolveEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(referenceField);

        if ((referenceField.Field.WireKind != 11) || (referenceField.Field.Reference != ContentReferenceKind.Entity))
        {
            throw new ArgumentException("The selected field is not a declared Entity reference.", nameof(referenceField));
        }

        if (targetEntityId == Guid.Empty)
        {
            throw new ArgumentException("A stable Entity identity must be nonempty.", nameof(targetEntityId));
        }

        var entity = _context.ResolveEntity(referenceField, targetEntityId);

        if (!entity.IsValid)
        {
            throw new InvalidOperationException("The restore map returned an invalid Entity handle.");
        }

        return entity;
    }

    /// <summary>Resolves a stable identity through the exact candidate map for one declared system Entity-reference field.</summary>
    /// <param name="referenceField">The selected system Entity-reference field descriptor.</param>
    /// <param name="targetEntityId">The nonempty stable Entity identity from the author value.</param>
    /// <returns>The candidate's live Entity handle.</returns>
    /// <exception cref="ArgumentException">The field or stable Entity identity is invalid.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired, used from another thread, or the candidate map is inconsistent.</exception>
    public Entity ResolveEntity(WorldSystemFieldDescriptor referenceField, Guid targetEntityId)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(referenceField);

        if ((referenceField.Field.WireKind != 11) || (referenceField.Field.Reference != ContentReferenceKind.Entity))
        {
            throw new ArgumentException("The selected field is not a declared Entity reference.", nameof(referenceField));
        }

        if (targetEntityId == Guid.Empty)
        {
            throw new ArgumentException("A stable Entity identity must be nonempty.", nameof(targetEntityId));
        }

        var entity = _context.ResolveEntity(referenceField, targetEntityId);

        if (!entity.IsValid)
        {
            throw new InvalidOperationException("The restore map returned an invalid Entity handle.");
        }

        return entity;
    }

    internal void Revoke() => _isActive = false;

    private void EnsureActive()
    {
        if (!_isActive || !_context.IsActive)
        {
            throw new InvalidOperationException("The world-state restore binding has expired.");
        }

        if (Environment.CurrentManagedThreadId != _context.OwnerThreadId)
        {
            throw new InvalidOperationException("The world-state restore binding is restricted to its issuing thread.");
        }
    }
}
