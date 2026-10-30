// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

/// <summary>Describes one immutable profile-to-schema composition.</summary>
public sealed class ContentProfileDescriptor
{
    /// <summary>Initializes a profile composition from exact schema tuples.</summary>
    /// <param name="profileId">The nonempty profile identity.</param>
    /// <param name="schemas">The exact schema tuples in RFC-identity order.</param>
    /// <exception cref="ArgumentException"><paramref name="profileId"/> is empty or <paramref name="schemas"/> is default.</exception>
    public ContentProfileDescriptor(Guid profileId, ImmutableArray<ContentSchemaReference> schemas)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A profile identity must be nonempty.", nameof(profileId));
        }

        if (schemas.IsDefault)
        {
            throw new ArgumentException("The profile schema set must be initialized.", nameof(schemas));
        }

        var sortedSchemas = schemas.AsSpan().ToArray();
        Array.Sort(sortedSchemas, static (left, right) => CompareSchemaReferences(left, right));

        for (var index = 0; index < sortedSchemas.Length; index++)
        {
            if (sortedSchemas[index].SchemaId == Guid.Empty)
            {
                throw new ArgumentException("A profile schema reference must be valid.", nameof(schemas));
            }

            if ((index > 0) && (sortedSchemas[index - 1] == sortedSchemas[index]))
            {
                throw new ArgumentException("Profile schema references must be unique.", nameof(schemas));
            }
        }

        ProfileId = profileId;
        Schemas = [.. sortedSchemas];
    }

    /// <summary>Gets the nonempty profile identity.</summary>
    public Guid ProfileId { get; }

    /// <summary>Gets the exact schema tuples selected by the profile.</summary>
    public ImmutableArray<ContentSchemaReference> Schemas { get; }

    private static int CompareSchemaReferences(ContentSchemaReference left, ContentSchemaReference right)
    {
        var comparison = FoundationCatalogue.CompareRfc(left.SchemaId, right.SchemaId);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Major.CompareTo(right.Major);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.Minor.CompareTo(right.Minor);
        return (comparison != 0) ? comparison : left.Scope.CompareTo(right.Scope);
    }
}
