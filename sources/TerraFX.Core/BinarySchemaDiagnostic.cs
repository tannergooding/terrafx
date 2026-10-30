// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

/// <summary>Describes a schema-version validation failure at a physical record path.</summary>
public sealed class BinarySchemaDiagnostic
{
    /// <summary>Initializes an immutable schema validation diagnostic.</summary>
    /// <param name="schemaId">The nonempty schema identity.</param>
    /// <param name="tag">The physical field tag, or zero for a record-level invariant.</param>
    /// <param name="path">The ordered field-tag and fixed-array-index path.</param>
    /// <param name="category">The nonempty schema-version failure category.</param>
    /// <param name="claim">The exact rejected claim, when applicable.</param>
    /// <param name="limit">The exact applicable limit, when applicable.</param>
    /// <exception cref="ArgumentException"><paramref name="schemaId"/> is empty, the path is default or contains invalid segments, or <paramref name="category"/> is empty.</exception>
    public BinarySchemaDiagnostic(
        Guid schemaId,
        uint tag,
        ImmutableArray<BinarySchemaPathSegment> path,
        string category,
        ulong? claim,
        ulong? limit)
    {
        if (schemaId == Guid.Empty)
        {
            throw new ArgumentException("A schema diagnostic requires a nonempty schema identity.", nameof(schemaId));
        }

        if (path.IsDefault)
        {
            throw new ArgumentException("The physical path must be initialized.", nameof(path));
        }

        ArgumentException.ThrowIfNullOrEmpty(category);

        for (var index = 0; index < path.Length; index++)
        {
            var segment = path[index];

            if (segment.Kind is not BinarySchemaPathSegmentKind.FieldTag and not BinarySchemaPathSegmentKind.ArrayIndex)
            {
                throw new ArgumentException("The physical path contains an invalid segment.", nameof(path));
            }

            if ((segment.Kind == BinarySchemaPathSegmentKind.FieldTag) && (segment.Value == 0))
            {
                throw new ArgumentException("A field-tag path segment must be nonzero.", nameof(path));
            }
        }

        SchemaId = schemaId;
        Tag = tag;
        Path = [.. path];
        Category = category;
        Claim = claim;
        Limit = limit;
    }

    /// <summary>Gets the failure category.</summary>
    public string Category { get; }

    /// <summary>Gets the rejected claim, when applicable.</summary>
    public ulong? Claim { get; }

    /// <summary>Gets the applicable limit, when present.</summary>
    public ulong? Limit { get; }

    /// <summary>Gets the immutable physical field-tag and array-index path.</summary>
    public ImmutableArray<BinarySchemaPathSegment> Path { get; }

    /// <summary>Gets the schema identity.</summary>
    public Guid SchemaId { get; }

    /// <summary>Gets the physical field tag, or zero for a record-level invariant.</summary>
    public uint Tag { get; }
}
