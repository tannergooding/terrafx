// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX;

/// <summary>Provides immutable compiled schema and profile metadata for one application.</summary>
public sealed class CompiledContentSchemaRegistry
{
    /// <summary>Initializes an immutable schema registry and its profile compositions.</summary>
    /// <param name="schemas">The exact compiled schema versions.</param>
    /// <param name="profiles">The compiled profile compositions.</param>
    /// <exception cref="ArgumentException">A collection is default, schemas conflict, or a profile names an unregistered schema.</exception>
    public CompiledContentSchemaRegistry(
        ImmutableArray<DataSchema> schemas,
        ImmutableArray<ContentProfileDescriptor> profiles)
    {
        if (schemas.IsDefault)
        {
            throw new ArgumentException("The schema set must be initialized.", nameof(schemas));
        }

        if (profiles.IsDefault)
        {
            throw new ArgumentException("The profile set must be initialized.", nameof(profiles));
        }

        foreach (var schema in schemas)
        {
            ArgumentNullException.ThrowIfNull(schema);
        }

        var sortedSchemas = schemas.AsSpan().ToArray();
        Array.Sort(sortedSchemas, static (left, right) => CompareSchemaReferences(left.Schema, right.Schema));

        for (var index = 0; index < sortedSchemas.Length; index++)
        {
            var schema = sortedSchemas[index];

            if (index > 0)
            {
                var previous = sortedSchemas[index - 1];

                if (previous.Schema == schema.Schema)
                {
                    throw new ArgumentException("Schema tuples must be unique.", nameof(schemas));
                }

                if ((previous.Schema.SchemaId == schema.Schema.SchemaId)
                    && (previous.IdentityAssignment != schema.IdentityAssignment))
                {
                    throw new ArgumentException("Schema versions cannot assign different identities to the same schema ID.", nameof(schemas));
                }
            }
        }

        foreach (var schema in sortedSchemas)
        {
            foreach (var field in schema.Fields)
            {
                foreach (var nestedSchema in field.NestedSchemas)
                {
                    if (!ContainsSchema(sortedSchemas, nestedSchema))
                    {
                        throw new ArgumentException("Each nested schema reference must resolve to an exact registered tuple.", nameof(schemas));
                    }
                }

                if ((field.NestedRoleMask != 0) && !ContainsNestedRole(sortedSchemas, field.NestedRoleMask))
                {
                    throw new ArgumentException("Each nested role selection must admit a registered content schema.", nameof(schemas));
                }
            }
        }

        foreach (var profile in profiles)
        {
            ArgumentNullException.ThrowIfNull(profile);
        }

        var sortedProfiles = profiles.AsSpan().ToArray();
        Array.Sort(sortedProfiles, static (left, right) => FoundationCatalogue.CompareRfc(left.ProfileId, right.ProfileId));

        for (var index = 0; index < sortedProfiles.Length; index++)
        {
            var profile = sortedProfiles[index];

            if ((index > 0) && (sortedProfiles[index - 1].ProfileId == profile.ProfileId))
            {
                throw new ArgumentException("Profile identities must be unique.", nameof(profiles));
            }

            foreach (var schema in profile.Schemas)
            {
                if (!ContainsSchema(sortedSchemas, schema))
                {
                    throw new ArgumentException("Each profile reference must resolve to an exact registered schema tuple.", nameof(profiles));
                }
            }
        }

        Schemas = [.. sortedSchemas];
        Profiles = [.. sortedProfiles];
    }

    /// <summary>Initializes an immutable schema registry without profile compositions.</summary>
    /// <param name="schemas">The exact compiled schema versions.</param>
    /// <exception cref="ArgumentException"><paramref name="schemas"/> is default or contains conflicting declarations.</exception>
    public CompiledContentSchemaRegistry(ImmutableArray<DataSchema> schemas)
        : this(schemas, [])
    {
    }

    /// <summary>Gets the compiled profile compositions in RFC identity order.</summary>
    public ImmutableArray<ContentProfileDescriptor> Profiles { get; }

    /// <summary>Gets the exact compiled schema versions in canonical order.</summary>
    public ImmutableArray<DataSchema> Schemas { get; }

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

    private static bool ContainsNestedRole(DataSchema[] schemas, ulong nestedRoleMask)
    {
        foreach (var schema in schemas)
        {
            if ((schema.Schema.Scope == ContentSchemaScope.Content)
                && ((nestedRoleMask & (1UL << ((int)schema.Role - 1))) != 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSchema(DataSchema[] schemas, ContentSchemaReference schema)
    {
        var low = 0;
        var high = schemas.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = CompareSchemaReferences(schemas[middle].Schema, schema);

            if (comparison == 0)
            {
                return true;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return false;
    }

    /// <summary>Attempts to resolve a profile identity.</summary>
    /// <param name="profileId">The profile identity.</param>
    /// <param name="profile">The exact profile descriptor, or <see langword="null"/> when not registered.</param>
    /// <returns><see langword="true"/> if the profile is registered.</returns>
    public bool TryGetProfile(Guid profileId, [NotNullWhen(true)] out ContentProfileDescriptor? profile)
    {
        var low = 0;
        var high = Profiles.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = Profiles[middle];
            var comparison = FoundationCatalogue.CompareRfc(candidate.ProfileId, profileId);

            if (comparison == 0)
            {
                profile = candidate;
                return true;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        profile = null;
        return false;
    }

    /// <summary>Attempts to resolve an exact schema tuple.</summary>
    /// <param name="schema">The complete schema tuple.</param>
    /// <param name="dataSchema">The exact descriptor, or <see langword="null"/> when not registered.</param>
    /// <returns><see langword="true"/> if the exact tuple is registered.</returns>
    public bool TryGetSchema(ContentSchemaReference schema, [NotNullWhen(true)] out DataSchema? dataSchema)
    {
        var low = 0;
        var high = Schemas.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = Schemas[middle];
            var comparison = CompareSchemaReferences(candidate.Schema, schema);

            if (comparison == 0)
            {
                dataSchema = candidate;
                return true;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        dataSchema = null;
        return false;
    }
}
