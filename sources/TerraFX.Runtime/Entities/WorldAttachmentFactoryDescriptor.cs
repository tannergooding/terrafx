// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Describes one captured World attachment provider family.</summary>
public sealed class WorldAttachmentFactoryDescriptor
{
    internal WorldAttachmentFactoryDescriptor(
        ContentSchemaReference schema,
        Guid factoryId,
        int factoryVersion,
        ImmutableArray<WorldAttachmentFieldDescriptor> fields)
    {
        if ((schema.Scope != ContentSchemaScope.Content)
            || (schema.SchemaId == Guid.Empty)
            || (schema.Major <= 0)
            || (schema.Minor < 0))
        {
            throw new ArgumentException("An attachment factory requires an exact Content schema tuple.", nameof(schema));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(factoryId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factoryVersion);

        if (fields.IsDefault)
        {
            throw new ArgumentException("Attachment factory fields must be initialized.", nameof(fields));
        }

        foreach (var field in fields)
        {
            ArgumentNullException.ThrowIfNull(field);

            if (field.Schema != schema)
            {
                throw new ArgumentException("Every attachment factory field must belong to its exact schema.", nameof(fields));
            }
        }

        Schema = schema;
        FactoryId = factoryId;
        FactoryVersion = factoryVersion;
        Fields = ImmutableArrayCopy.Create(fields);
    }

    /// <summary>Gets the stable factory identity.</summary>
    public Guid FactoryId { get; }

    /// <summary>Gets the positive factory version.</summary>
    public int FactoryVersion { get; }

    /// <summary>Gets the immutable selected attachment fields in canonical schema order.</summary>
    public ImmutableArray<WorldAttachmentFieldDescriptor> Fields { get; }

    /// <summary>Gets the exact AttachmentState schema tuple.</summary>
    public ContentSchemaReference Schema { get; }
}
