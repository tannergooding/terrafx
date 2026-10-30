// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

/// <summary>Describes one exact compiled content schema version.</summary>
public sealed class DataSchema
{
    /// <summary>Initializes an immutable content schema descriptor.</summary>
    /// <param name="schema">The exact schema tuple.</param>
    /// <param name="identityAssignment">The fixed name UUID assignment for the schema key.</param>
    /// <param name="role">The schema's semantic role.</param>
    /// <param name="owner">The subsystem that owns the schema.</param>
    /// <param name="storage">The component storage policy.</param>
    /// <param name="transientRestore">The transient restore policy.</param>
    /// <param name="fields">The fields in strictly increasing tag order.</param>
    /// <exception cref="ArgumentException">The identity assignment or field set is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The role, owner, storage, or restore policy is invalid.</exception>
    public DataSchema(
        ContentSchemaReference schema,
        ContentSchemaIdentityAssignment identityAssignment,
        ContentSchemaRole role,
        ContentSchemaOwner owner,
        ContentSchemaStorage storage,
        ContentTransientRestore transientRestore,
        ImmutableArray<ContentFieldDescriptor> fields)
    {
        if ((schema.SchemaId == Guid.Empty)
            || (identityAssignment.Name is null)
            || (identityAssignment.SchemaId != schema.SchemaId))
        {
            throw new ArgumentException("The schema tuple and stable-key identity assignment must match.", nameof(identityAssignment));
        }

        if (role is < ContentSchemaRole.Descriptor or > ContentSchemaRole.TypedSettings)
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        if (owner is < ContentSchemaOwner.Core or > ContentSchemaOwner.Application)
        {
            throw new ArgumentOutOfRangeException(nameof(owner));
        }

        if (storage is < ContentSchemaStorage.NotComponent or > ContentSchemaStorage.Transient)
        {
            throw new ArgumentOutOfRangeException(nameof(storage));
        }

        if (transientRestore is < ContentTransientRestore.NotApplicable or > ContentTransientRestore.ExactDerivedRehydration)
        {
            throw new ArgumentOutOfRangeException(nameof(transientRestore));
        }

        if (fields.IsDefault)
        {
            throw new ArgumentException("The field set must be initialized.", nameof(fields));
        }

        var isTransient = storage == ContentSchemaStorage.Transient;
        var hasTransientRestore = transientRestore != ContentTransientRestore.NotApplicable;
        var isComponent = role == ContentSchemaRole.Component;

        if (isComponent == (storage == ContentSchemaStorage.NotComponent)
            || isTransient != hasTransientRestore
            || (role == ContentSchemaRole.EntityTag && !fields.IsEmpty))
        {
            throw new ArgumentException("The storage, restore, or tag declaration is inconsistent.", nameof(storage));
        }

        uint previousTag = 0;

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index] ?? throw new ArgumentException("The field set cannot contain null entries.", nameof(fields));

            if (field.Tag <= previousTag)
            {
                throw new ArgumentException("Field tags must be unique and strictly increasing.", nameof(fields));
            }

            if ((field.DefaultValue is { Kind: ContentFieldDefaultKind.MaterialProperty })
                && (!StringComparer.Ordinal.Equals(identityAssignment.Name, "native/material")
                    || (field.Tag is not 6 and not 9 and not 10 and not 11 and not 12 and not 13)))
            {
                throw new ArgumentException("Material-property defaults are limited to the declared native/material tags.", nameof(fields));
            }

            previousTag = field.Tag;
        }

        Schema = schema;
        IdentityAssignment = identityAssignment;
        Role = role;
        Owner = owner;
        Storage = storage;
        TransientRestore = transientRestore;
        Fields = [.. fields];
    }

    /// <summary>Gets the complete sorted field descriptor set.</summary>
    public ImmutableArray<ContentFieldDescriptor> Fields { get; }

    /// <summary>Gets the stable key and recipe assignment for the schema identity.</summary>
    public ContentSchemaIdentityAssignment IdentityAssignment { get; }

    /// <summary>Gets the subsystem that owns the schema.</summary>
    public ContentSchemaOwner Owner { get; }

    /// <summary>Gets the semantic role of the schema.</summary>
    public ContentSchemaRole Role { get; }

    /// <summary>Gets the exact schema tuple.</summary>
    public ContentSchemaReference Schema { get; }

    /// <summary>Gets the component storage policy.</summary>
    public ContentSchemaStorage Storage { get; }

    /// <summary>Gets the transient restore policy.</summary>
    public ContentTransientRestore TransientRestore { get; }
}
