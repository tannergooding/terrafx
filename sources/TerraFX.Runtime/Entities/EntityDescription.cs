// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Freezes the requested persistent identity, initial component values, and entity tags.</summary>
public sealed class EntityDescription
{
    private readonly ImmutableArray<ComponentInitialization> _components;
    private readonly ImmutableArray<Guid> _tags;

    /// <summary>Initializes an immutable entity creation description.</summary>
    /// <param name="persistentId">The optional explicit stable identity candidate.</param>
    /// <param name="components">The typed component values or registered-default requests.</param>
    /// <param name="tags">The requested registered tag schema identities.</param>
    /// <exception cref="ArgumentException">An identity is empty, a component type repeats, or a tag repeats.</exception>
    /// <exception cref="ArgumentNullException">A component initialization is null.</exception>
    public EntityDescription(
        Guid? persistentId = null,
        ImmutableArray<ComponentInitialization> components = default,
        ImmutableArray<Guid> tags = default)
    {
        if (persistentId == Guid.Empty)
        {
            throw new ArgumentException("A present persistent identity candidate cannot be empty.", nameof(persistentId));
        }

        _components = components.IsDefault ? [] : [.. components];
        _tags = tags.IsDefault ? [] : [.. tags];

        var componentTypes = new HashSet<Type>();

        foreach (var component in _components)
        {
            ArgumentNullException.ThrowIfNull(component);

            if (!componentTypes.Add(component.ComponentType))
            {
                throw new ArgumentException("An entity description cannot initialize one component type more than once.", nameof(components));
            }
        }

        var tagIds = new HashSet<Guid>();

        foreach (var tag in _tags)
        {
            if (tag == Guid.Empty)
            {
                throw new ArgumentException("An entity tag schema identity cannot be empty.", nameof(tags));
            }

            if (!tagIds.Add(tag))
            {
                throw new ArgumentException("An entity description cannot contain a tag schema identity more than once.", nameof(tags));
            }
        }

        PersistentId = persistentId;
    }

    /// <summary>Gets the copied immutable component initialization requests.</summary>
    public ImmutableArray<ComponentInitialization> Components => _components;

    /// <summary>Gets the optional explicit stable identity candidate.</summary>
    public Guid? PersistentId { get; }

    /// <summary>Gets the copied immutable tag schema identities.</summary>
    public ImmutableArray<Guid> Tags => _tags;
}
