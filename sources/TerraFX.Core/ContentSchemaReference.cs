// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Identifies a versioned schema in the content or foundation registry.</summary>
public readonly struct ContentSchemaReference : IEquatable<ContentSchemaReference>
{
    /// <summary>Initializes a valid schema reference.</summary>
    /// <param name="schemaId">The nonempty schema identity.</param>
    /// <param name="major">The positive schema major version.</param>
    /// <param name="minor">The nonnegative schema minor version.</param>
    /// <param name="scope">The registry that owns the schema.</param>
    /// <exception cref="ArgumentException"><paramref name="schemaId"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="major"/>, <paramref name="minor"/> or <paramref name="scope"/> is invalid.</exception>
    public ContentSchemaReference(Guid schemaId, int major, int minor, ContentSchemaScope scope)
    {
        if (schemaId == Guid.Empty)
        {
            throw new ArgumentException("A schema identity must be nonempty.", nameof(schemaId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);

        if (scope is not ContentSchemaScope.Content and not ContentSchemaScope.Foundation)
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        SchemaId = schemaId;
        Major = major;
        Minor = minor;
        Scope = scope;
    }

    /// <summary>Gets the major schema version.</summary>
    public int Major { get; }

    /// <summary>Gets the minor schema version.</summary>
    public int Minor { get; }

    /// <summary>Gets the schema identity.</summary>
    public Guid SchemaId { get; }

    /// <summary>Gets the registry that owns the schema.</summary>
    public ContentSchemaScope Scope { get; }

    /// <summary>Negates equality of the complete schema tuple.</summary>
    public static bool operator !=(ContentSchemaReference left, ContentSchemaReference right) => !(left == right);

    /// <summary>Compares the complete schema tuple.</summary>
    public static bool operator ==(ContentSchemaReference left, ContentSchemaReference right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ContentSchemaReference other) =>
        SchemaId.Equals(other.SchemaId) && Major == other.Major && Minor == other.Minor && Scope == other.Scope;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ContentSchemaReference other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(SchemaId, Major, Minor, Scope);
}
