// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Animation;

internal static class RootMotionResourceSchemas
{
    private static readonly ImmutableArray<DataSchema> s_schemas = CreateSchemas();

    internal static ImmutableArray<DataSchema> Schemas => s_schemas;

    internal static DataSchema GetRequired(CompiledContentSchemaRegistry schemas, string key)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        var schema = Reference(key);

        if (!schemas.TryGetSchema(schema, out var actual))
        {
            throw new ArgumentException("A root-motion resource schema is missing from the exact registry.", nameof(schemas));
        }

        foreach (var expected in s_schemas)
        {
            if (expected.Schema != schema)
            {
                continue;
            }

            if ((actual.Role != expected.Role) || (actual.Owner != expected.Owner)
                || (actual.Storage != expected.Storage) || (actual.TransientRestore != expected.TransientRestore)
                || (actual.Fields.Length != expected.Fields.Length))
            {
                throw new ArgumentException("Root-motion resource metadata must match its complete fixed owner schema.", nameof(schemas));
            }

            for (var index = 0; index < expected.Fields.Length; index++)
            {
                var left = actual.Fields[index];
                var right = expected.Fields[index];

                if ((left.Tag != right.Tag) || (left.WireKind != right.WireKind) || (left.ElementKind != right.ElementKind)
                    || (left.IsRequired != right.IsRequired) || (left.MinimumCount != right.MinimumCount)
                    || (left.MaximumCount != right.MaximumCount) || (left.NestedRoleMask != 0)
                    || !left.NestedSchemas.AsSpan().SequenceEqual(right.NestedSchemas.AsSpan())
                    || (left.Unit != right.Unit) || (left.Reference != right.Reference)
                    || (left.AuthoringFlags != right.AuthoringFlags) || (left.DefaultValue is not null))
                {
                    throw new ArgumentException("Root-motion resource fields must match their exact owner declarations.", nameof(schemas));
                }
            }

            return actual;
        }

        throw new ArgumentException("The selected tuple is not a root-motion resource record.", nameof(key));
    }

    internal static ContentSchemaReference Reference(string key)
        => new(new ContentSchemaIdentityAssignment(key).SchemaId, 1, 0, ContentSchemaScope.Content);

    private static ContentFieldDescriptor CreateField(
        uint tag,
        ushort kind,
        bool required = true,
        ulong minimumCount = 1,
        ulong maximumCount = 1,
        ImmutableArray<ContentSchemaReference> nested = default,
        ContentReferenceKind reference = ContentReferenceKind.None,
        ContentUnit unit = ContentUnit.None)
        => new(tag, kind, 0, required, minimumCount, maximumCount, nested.IsDefault ? [] : nested,
            0, unit, reference, ContentAuthoring.None, null);

    private static DataSchema CreateSchema(string key, ContentSchemaRole role, ImmutableArray<ContentFieldDescriptor> fields)
    {
        var assignment = new ContentSchemaIdentityAssignment(key);
        return new DataSchema(
            Reference(key), assignment, role, ContentSchemaOwner.Animation,
            ContentSchemaStorage.NotComponent, ContentTransientRestore.NotApplicable, fields);
    }

    private static ImmutableArray<DataSchema> CreateSchemas()
    {
        var asset = Reference("native/asset-reference");
        return [
            CreateSchema("animation/root-motion-resource", ContentSchemaRole.ResourceState, [
                CreateField(1, 24, minimumCount: 4096, maximumCount: 4096, nested: [Reference("animation/root-motion-slot")]),
            ]),
            CreateSchema("animation/root-motion-slot", ContentSchemaRole.Auxiliary, [
                CreateField(1, 9),
                CreateField(2, 11, required: false, reference: ContentReferenceKind.HistoricalEntity),
                CreateField(3, 11, required: false),
                CreateField(4, 11, required: false),
                CreateField(5, 11, required: false, reference: ContentReferenceKind.HistoricalEntity),
                CreateField(6, 11, required: false),
                CreateField(7, 11, required: false),
                CreateField(8, 8, required: false),
                CreateField(9, 15, required: false, unit: ContentUnit.EngineDistance),
                CreateField(10, 17, required: false),
                CreateField(11, 24, required: false, minimumCount: 0, maximumCount: 4, nested: [Reference("animation/root-motion-source")]),
                CreateField(12, 1, required: false),
                CreateField(13, 22, required: false, nested: [asset], reference: ContentReferenceKind.Asset),
            ]),
            CreateSchema("animation/root-motion-source", ContentSchemaRole.Auxiliary, [
                CreateField(1, 11),
                CreateField(2, 22, nested: [asset], reference: ContentReferenceKind.Asset),
                CreateField(3, 8),
                CreateField(4, 8),
                CreateField(5, 10),
            ]),
        ];
    }
}
