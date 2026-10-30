// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Assigns a schema identity to one stable key using the registered name UUID recipe.</summary>
public readonly struct ContentSchemaIdentityAssignment : IEquatable<ContentSchemaIdentityAssignment>
{
    private static readonly Guid s_namespaceId = new Guid("2ed8b750-e1a0-4b46-9000-000000000001");

    /// <summary>Initializes an identity assignment for one exact ASCII schema key.</summary>
    /// <param name="name">The exact stable schema key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid exact ASCII name.</exception>
    public ContentSchemaIdentityAssignment(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        SchemaId = FoundationNameUuid.Create(s_namespaceId, name);
    }

    /// <summary>Gets the fixed schema identity namespace.</summary>
    public static Guid NamespaceId => s_namespaceId;

    /// <summary>Gets the exact name UUID recipe.</summary>
    public static string Recipe => FoundationNameUuid.Recipe;

    /// <summary>Gets the stable schema key.</summary>
    public string Name { get; }

    /// <summary>Gets the derived schema identity.</summary>
    public Guid SchemaId { get; }

    /// <summary>Negates equality of the complete schema identity assignment.</summary>
    public static bool operator !=(ContentSchemaIdentityAssignment left, ContentSchemaIdentityAssignment right) => !(left == right);

    /// <summary>Compares the complete schema identity assignment.</summary>
    public static bool operator ==(ContentSchemaIdentityAssignment left, ContentSchemaIdentityAssignment right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ContentSchemaIdentityAssignment other)
        => SchemaId == other.SchemaId && StringComparer.Ordinal.Equals(Name, other.Name);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ContentSchemaIdentityAssignment other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(SchemaId, Name is null ? 0 : StringComparer.Ordinal.GetHashCode(Name));
}
