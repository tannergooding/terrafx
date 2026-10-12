// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Defines a component/tag membership filter and its declared query access.</summary>
public sealed class EntityQueryDescription
{
    private const int MaximumSchemaCount = 256;

    /// <summary>Initializes a query and derives its common optional range from its accesses.</summary>
    /// <param name="requiredSchemaIds">Required component/tag schema identities.</param>
    /// <param name="excludedSchemaIds">Excluded component/tag schema identities.</param>
    /// <param name="access">Component/tag accesses required by the query.</param>
    /// <exception cref="ArgumentException">An array, identity, access, membership set, or common range is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A schema/access count exceeds its supported maximum.</exception>
    public EntityQueryDescription(
        ImmutableArray<Guid> requiredSchemaIds,
        ImmutableArray<Guid> excludedSchemaIds,
        ImmutableArray<SystemResourceAccess> access)
    {
        ValidateSchemaArrays(requiredSchemaIds, excludedSchemaIds);

        if (access.IsDefault)
        {
            throw new ArgumentException("The access array cannot be default.", nameof(access));
        }

        var range = access.IsEmpty ? null : access[0].Range;
        ValidateAccess(requiredSchemaIds, access, range);

        RequiredSchemaIds = ImmutableArrayCopy.Create(requiredSchemaIds);
        ExcludedSchemaIds = ImmutableArrayCopy.Create(excludedSchemaIds);
        Access = ImmutableArrayCopy.Create(access);
        Range = range;
    }

    /// <summary>Initializes a query with an explicit range, including a filter-only ranged query.</summary>
    /// <param name="requiredSchemaIds">Required component/tag schema identities.</param>
    /// <param name="excludedSchemaIds">Excluded component/tag schema identities.</param>
    /// <param name="access">Component/tag accesses required by the query.</param>
    /// <param name="range">The common nonempty half-open partition range.</param>
    /// <exception cref="ArgumentException">An array, identity, access, or membership set is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A schema/access count or range exceeds its supported domain.</exception>
    public EntityQueryDescription(
        ImmutableArray<Guid> requiredSchemaIds,
        ImmutableArray<Guid> excludedSchemaIds,
        ImmutableArray<SystemResourceAccess> access,
        SystemPartitionRange range)
    {
        ValidateSchemaArrays(requiredSchemaIds, excludedSchemaIds);

        if (!range.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        if (access.IsDefault)
        {
            throw new ArgumentException("The access array cannot be default.", nameof(access));
        }

        ValidateAccess(requiredSchemaIds, access, range);

        RequiredSchemaIds = ImmutableArrayCopy.Create(requiredSchemaIds);
        ExcludedSchemaIds = ImmutableArrayCopy.Create(excludedSchemaIds);
        Access = ImmutableArrayCopy.Create(access);
        Range = range;
    }

    /// <summary>Gets the immutable declared component/tag accesses.</summary>
    public ImmutableArray<SystemResourceAccess> Access { get; }

    /// <summary>Gets the immutable excluded component/tag schema identities.</summary>
    public ImmutableArray<Guid> ExcludedSchemaIds { get; }

    /// <summary>Gets the optional global work-plan partition range.</summary>
    public SystemPartitionRange? Range { get; }

    /// <summary>Gets the immutable required component/tag schema identities.</summary>
    public ImmutableArray<Guid> RequiredSchemaIds { get; }

    private static void ValidateAccess(
        ImmutableArray<Guid> requiredSchemaIds,
        ImmutableArray<SystemResourceAccess> access,
        SystemPartitionRange? range)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(access.Length, MaximumSchemaCount, nameof(access));

        var required = new HashSet<Guid>(requiredSchemaIds);
        var identities = new HashSet<(SystemResourceKind Kind, Guid ResourceId)>();

        foreach (var item in access)
        {
            if (!item.IsValid)
            {
                throw new ArgumentException("Every query access must be intrinsically valid.", nameof(access));
            }

            if (item.Kind is not (SystemResourceKind.Component or SystemResourceKind.Tag))
            {
                throw new ArgumentException("Query access must name a component or tag.", nameof(access));
            }

            if (!required.Contains(item.ResourceId))
            {
                throw new ArgumentException("Every accessed schema must be required by the query.", nameof(access));
            }

            if (!identities.Add((item.Kind, item.ResourceId)))
            {
                throw new ArgumentException("Query access cannot repeat a resource identity.", nameof(access));
            }

            if (item.Range != range)
            {
                throw new ArgumentException("Query accesses must use the query's exact range presence and value.", nameof(access));
            }
        }
    }

    private static void ValidateSchemaArray(ImmutableArray<Guid> schemaIds, string parameterName)
    {
        if (schemaIds.IsDefault)
        {
            throw new ArgumentException("A schema identity array cannot be default.", parameterName);
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(schemaIds.Length, MaximumSchemaCount, parameterName);

        var identities = new HashSet<Guid>();

        foreach (var schemaId in schemaIds)
        {
            if (schemaId == Guid.Empty)
            {
                throw new ArgumentException("A schema identity cannot be empty.", parameterName);
            }

            if (!identities.Add(schemaId))
            {
                throw new ArgumentException("A schema identity array cannot contain duplicates.", parameterName);
            }
        }
    }

    private static void ValidateSchemaArrays(
        ImmutableArray<Guid> requiredSchemaIds,
        ImmutableArray<Guid> excludedSchemaIds)
    {
        ValidateSchemaArray(requiredSchemaIds, nameof(requiredSchemaIds));
        ValidateSchemaArray(excludedSchemaIds, nameof(excludedSchemaIds));

        var required = new HashSet<Guid>(requiredSchemaIds);

        foreach (var schemaId in excludedSchemaIds)
        {
            if (required.Contains(schemaId))
            {
                throw new ArgumentException("Required and excluded schemas must be disjoint.", nameof(excludedSchemaIds));
            }
        }
    }
}
