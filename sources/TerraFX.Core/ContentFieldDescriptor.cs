// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

/// <summary>Describes the fixed wire shape and authoring annotations of one content field.</summary>
public sealed class ContentFieldDescriptor
{
    private const ulong MaximumNestedRoleMask = (1UL << 13) - 1;
    private const ContentAuthoring SupportedAuthoringFlags = ContentAuthoring.PrefabEditable | ContentAuthoring.Animatable;

    /// <summary>Initializes an immutable content field descriptor.</summary>
    /// <param name="tag">The permanent nonzero field tag.</param>
    /// <param name="wireKind">The permanent EBIN wire kind.</param>
    /// <param name="elementKind">The permanent fixed element kind for FixedArray fields, or zero otherwise.</param>
    /// <param name="isRequired"><see langword="true"/> when the field is required by this schema version.</param>
    /// <param name="minimumCount">The minimum admitted field count.</param>
    /// <param name="maximumCount">The maximum admitted field count.</param>
    /// <param name="nestedSchemas">The exact nested schema set, or an initialized empty array when unused.</param>
    /// <param name="nestedRoleMask">The allowed nested role mask, or zero when unused.</param>
    /// <param name="unit">The semantic unit annotation.</param>
    /// <param name="reference">The semantic reference annotation.</param>
    /// <param name="authoringFlags">The authoring annotations.</param>
    /// <param name="defaultValue">The authoring default, when one is declared.</param>
    /// <exception cref="ArgumentException">A collection is default, a nested selection is inconsistent, or a default is incompatible.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A tag, wire kind, cardinality, enum, or annotation is invalid.</exception>
    public ContentFieldDescriptor(
        uint tag,
        ushort wireKind,
        ushort elementKind,
        bool isRequired,
        ulong minimumCount,
        ulong maximumCount,
        ImmutableArray<ContentSchemaReference> nestedSchemas,
        ulong nestedRoleMask,
        ContentUnit unit,
        ContentReferenceKind reference,
        ContentAuthoring authoringFlags,
        ContentFieldDefaultDescriptor? defaultValue)
    {
        ArgumentOutOfRangeException.ThrowIfZero(tag);

        ArgumentOutOfRangeException.ThrowIfZero(wireKind);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(wireKind, 24);

        if (wireKind == 23)
        {
            if ((elementKind is 0 or 12 or 13 or 22 or 23 or 24) || (EbinCodec.Stride(elementKind) == 0))
            {
                throw new ArgumentOutOfRangeException(nameof(elementKind));
            }
        }
        else if (elementKind != 0)
        {
            throw new ArgumentException("Only a FixedArray field has an element kind.", nameof(elementKind));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumCount, maximumCount, nameof(maximumCount));

        if (nestedSchemas.IsDefault)
        {
            throw new ArgumentException("The nested schema set must be initialized.", nameof(nestedSchemas));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(nestedRoleMask, MaximumNestedRoleMask);

        if (unit is < ContentUnit.None or > ContentUnit.Lumens)
        {
            throw new ArgumentOutOfRangeException(nameof(unit));
        }

        if (reference is < ContentReferenceKind.None or > ContentReferenceKind.ResourceOrSchema)
        {
            throw new ArgumentOutOfRangeException(nameof(reference));
        }

        if ((authoringFlags & ~SupportedAuthoringFlags) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(authoringFlags));
        }

        var isNestedRecord = wireKind is 22 or 24;
        var hasNestedSchemaSet = !nestedSchemas.IsEmpty;
        var hasNestedRoleMask = nestedRoleMask != 0;

        if (hasNestedSchemaSet && hasNestedRoleMask)
        {
            throw new ArgumentException("Nested schema sets and role masks are mutually exclusive.", nameof(nestedRoleMask));
        }

        if (isNestedRecord != (hasNestedSchemaSet || hasNestedRoleMask))
        {
            throw new ArgumentException("Record fields require exactly one nested schema selection.", nameof(nestedSchemas));
        }

        var stride = EbinCodec.Stride(wireKind);

        if (((stride != 0) || (wireKind == 22)) && ((minimumCount != 1) || (maximumCount != 1)))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumCount), "Scalar and nested-record fields have count one.");
        }

        if (defaultValue is { } value)
        {
            if ((value.Kind == ContentFieldDefaultKind.Literal)
                && ((value.WireKind != wireKind) || (value.Count < minimumCount) || (value.Count > maximumCount)))
            {
                throw new ArgumentException("The literal default does not match the field's wire shape.", nameof(defaultValue));
            }

            if ((value.Kind == ContentFieldDefaultKind.Meters)
                && ((unit != ContentUnit.EngineDistance)
                    || (wireKind is not 10 and not 14 and not 15)
                    || (value.MeterNumerators.Length != GetMeterLaneCount(wireKind))
                    || (value.Count < minimumCount)
                    || (value.Count > maximumCount)))
            {
                throw new ArgumentException("A rational meter default requires an engine-distance scalar or vector field.", nameof(defaultValue));
            }

            if ((value.Kind == ContentFieldDefaultKind.MaterialProperty) && (value.Count < minimumCount || value.Count > maximumCount))
            {
                throw new ArgumentException("The material default does not match the field cardinality.", nameof(defaultValue));
            }
        }

        var sortedSchemas = nestedSchemas.AsSpan().ToArray();
        Array.Sort(sortedSchemas, static (left, right) => CompareSchemaReferences(left, right));

        for (var index = 0; index < sortedSchemas.Length; index++)
        {
            if (sortedSchemas[index].SchemaId == Guid.Empty)
            {
                throw new ArgumentException("A nested schema reference must be valid.", nameof(nestedSchemas));
            }

            if ((index > 0) && (sortedSchemas[index - 1] == sortedSchemas[index]))
            {
                throw new ArgumentException("Nested schema references must be unique.", nameof(nestedSchemas));
            }
        }

        Tag = tag;
        WireKind = wireKind;
        ElementKind = elementKind;
        IsRequired = isRequired;
        MinimumCount = minimumCount;
        MaximumCount = maximumCount;
        NestedSchemas = [.. sortedSchemas];
        NestedRoleMask = nestedRoleMask;
        Unit = unit;
        Reference = reference;
        AuthoringFlags = authoringFlags;
        DefaultValue = defaultValue;
    }

    /// <summary>Gets the authoring annotations.</summary>
    public ContentAuthoring AuthoringFlags { get; }

    /// <summary>Gets the declared authoring default, when present.</summary>
    public ContentFieldDefaultDescriptor? DefaultValue { get; }

    /// <summary>Gets the permanent FixedArray element kind, or zero for other wire kinds.</summary>
    public ushort ElementKind { get; }

    /// <summary>Gets whether the field is required by this schema version.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets the maximum field count.</summary>
    public ulong MaximumCount { get; }

    /// <summary>Gets the minimum field count.</summary>
    public ulong MinimumCount { get; }

    /// <summary>Gets the allowed nested schema role mask.</summary>
    public ulong NestedRoleMask { get; }

    /// <summary>Gets the exact allowed nested schema references.</summary>
    public ImmutableArray<ContentSchemaReference> NestedSchemas { get; }

    /// <summary>Gets the semantic reference annotation.</summary>
    public ContentReferenceKind Reference { get; }

    /// <summary>Gets the permanent nonzero field tag.</summary>
    public uint Tag { get; }

    /// <summary>Gets the semantic unit annotation.</summary>
    public ContentUnit Unit { get; }

    /// <summary>Gets the permanent EBIN wire kind.</summary>
    public ushort WireKind { get; }

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

    private static int GetMeterLaneCount(ushort wireKind) => wireKind switch {
        10 => 1,
        14 => 2,
        15 => 3,
        _ => 0,
    };
}
