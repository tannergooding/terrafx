// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;

namespace TerraFX.Assets;

internal static class AssetReferenceScanner
{
    private const int MaximumDepth = 64;
    private const int MaximumReferences = 1_048_576;
    private static readonly DataSchema s_assetReferenceSchema = FindAssetReferenceSchema();

    internal static int CopyAssetIds(
        scoped EbinRecordReadView record,
        DataSchema schema,
        CompiledContentSchemaRegistry schemas,
        scoped Span<uint> destination)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(schemas);

        if (MemoryMarshal.AsBytes(destination).Overlaps(record.EncodedBytes))
        {
            throw new ArgumentException("The AssetId destination must not overlap the encoded record.", nameof(destination));
        }

        var copiedCount = 0;
        VisitRecord(record, schema, schemas, null, null, destination, true, [], false, ref copiedCount, 0);
        return copiedCount;
    }

    internal static int CopyAssetReferences(
        scoped EbinRecordReadView record,
        DataSchema schema,
        CompiledContentSchemaRegistry schemas,
        scoped Span<AssetReference> destination)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(schemas);

        if (MemoryMarshal.AsBytes(destination).Overlaps(record.EncodedBytes))
        {
            throw new ArgumentException("The AssetReference destination must not overlap the encoded record.", nameof(destination));
        }

        var measuredCount = CountAssetIds(record, schema, schemas);

        if (destination.Length != measuredCount)
        {
            throw new ArgumentException("The AssetReference destination must match the exact reference count.", nameof(destination));
        }

        var copiedCount = 0;
        VisitRecord(record, schema, schemas, null, null, [], false, destination, true, ref copiedCount, 0);

        if (copiedCount != measuredCount)
        {
            throw new InvalidOperationException("The AssetReference references changed during capture.");
        }

        return copiedCount;
    }

    internal static void CopyUniqueReferences(
        scoped ReadOnlySpan<AssetReference> referenceWorkspace,
        scoped ReadOnlySpan<int> uniqueIndices,
        scoped Span<AssetReference> destination)
    {
        if (destination.Length != uniqueIndices.Length)
        {
            throw new ArgumentException("The AssetReference destination must match the unique reference count.", nameof(destination));
        }

        var referenceBytes = MemoryMarshal.AsBytes(referenceWorkspace);
        var indexBytes = MemoryMarshal.AsBytes(uniqueIndices);
        var destinationBytes = MemoryMarshal.AsBytes(destination);

        if (destinationBytes.Overlaps(referenceBytes)
            || destinationBytes.Overlaps(indexBytes)
            || referenceBytes.Overlaps(indexBytes))
        {
            throw new ArgumentException("The AssetReference destination and scan workspaces must not overlap.", nameof(destination));
        }

        var previousIndex = -1;

        for (var index = 0; index < uniqueIndices.Length; index++)
        {
            var sourceIndex = uniqueIndices[index];

            if ((sourceIndex <= previousIndex) || (sourceIndex >= referenceWorkspace.Length))
            {
                throw new InvalidOperationException("The AssetReference unique-index workspace is not strictly increasing and in range.");
            }

            destination[index] = referenceWorkspace[sourceIndex];
            previousIndex = sourceIndex;
        }
    }

    internal static int CountAssetIds(
        scoped EbinRecordReadView record,
        DataSchema schema,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(schemas);
        var referenceCount = 0;
        VisitRecord(record, schema, schemas, null, null, [], false, [], false, ref referenceCount, 0);
        return referenceCount;
    }

    internal static ImmutableArray<AssetReference> Scan(
        scoped EbinRecordReadView record,
        DataSchema schema,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(schemas);
        var references = new List<AssetReference>();
        var seen = new HashSet<AssetReference>();
        var referenceCount = 0;
        VisitRecord(record, schema, schemas, references, seen, [], false, [], false, ref referenceCount, 0);
        return [.. references];
    }

    internal static int ScanInto(
        scoped EbinRecordReadView record,
        DataSchema schema,
        CompiledContentSchemaRegistry schemas,
        scoped Span<AssetReference> referenceWorkspace,
        scoped Span<int> indexWorkspace)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(schemas);
        var referenceCount = CountAssetIds(record, schema, schemas);

        if (referenceWorkspace.Length != referenceCount)
        {
            throw new ArgumentException("The AssetReference workspace must match the exact occurrence count.", nameof(referenceWorkspace));
        }

        if (indexWorkspace.Length != referenceCount)
        {
            throw new ArgumentException("The AssetReference index workspace must match the exact occurrence count.", nameof(indexWorkspace));
        }

        var referenceBytes = MemoryMarshal.AsBytes(referenceWorkspace);
        var indexBytes = MemoryMarshal.AsBytes(indexWorkspace);

        if (referenceBytes.Overlaps(record.EncodedBytes)
            || indexBytes.Overlaps(record.EncodedBytes)
            || referenceBytes.Overlaps(indexBytes))
        {
            throw new ArgumentException("The AssetReference scan workspaces must not overlap the record or each other.");
        }

        var copiedCount = 0;
        VisitRecord(record, schema, schemas, null, null, [], false, referenceWorkspace, true, ref copiedCount, 0);

        if (copiedCount != referenceCount)
        {
            throw new InvalidOperationException("The AssetReference occurrences changed during scan.");
        }

        for (var index = 0; index < referenceCount; index++)
        {
            indexWorkspace[index] = index;
        }

        HeapSortReferenceIndices(referenceWorkspace, indexWorkspace);
        var uniqueCount = 0;
        var cursor = 0;

        while (cursor < referenceCount)
        {
            var firstIndex = indexWorkspace[cursor];
            var reference = referenceWorkspace[firstIndex];
            indexWorkspace[uniqueCount++] = firstIndex;
            cursor++;

            while ((cursor < referenceCount)
                && AreEqual(reference, referenceWorkspace[indexWorkspace[cursor]]))
            {
                cursor++;
            }
        }

        HeapSortIndices(indexWorkspace[..uniqueCount]);
        return uniqueCount;
    }

    private static void AddReference(
        AssetReference reference,
        List<AssetReference>? references,
        HashSet<AssetReference>? seen,
        scoped Span<uint> destination,
        bool copyAssetIds,
        scoped Span<AssetReference> referenceDestination,
        bool copyAssetReferences,
        ref int referenceCount)
    {
        if (references is not null)
        {
            if (seen is null)
            {
                throw new InvalidOperationException("The asset-reference set has no uniqueness tracker.");
            }

            if (seen.Contains(reference))
            {
                return;
            }
        }

        if (referenceCount == MaximumReferences)
        {
            throw new InvalidDataException("The asset-reference count exceeds the admitted limit.");
        }

        if (copyAssetIds)
        {
            if (referenceCount >= destination.Length)
            {
                throw new InvalidOperationException("The AssetId destination is smaller than the measured reference count.");
            }

            destination[referenceCount] = checked((uint)reference.AssetId.Value);
        }

        if (copyAssetReferences)
        {
            if (referenceCount >= referenceDestination.Length)
            {
                throw new InvalidOperationException("The AssetReference destination is smaller than the measured reference count.");
            }

            referenceDestination[referenceCount] = reference;
        }

        if (references is not null)
        {
            _ = seen!.Add(reference);
            references.Add(reference);
        }

        referenceCount++;
    }

    private static bool AreEqual(AssetReference left, AssetReference right)
        => (left.AssetId == right.AssetId)
            && (left.SchemaId == right.SchemaId)
            && (left.SchemaMajor == right.SchemaMajor);

    private static int CompareReferenceIndices(
        scoped ReadOnlySpan<AssetReference> references,
        int leftIndex,
        int rightIndex)
    {
        var comparison = references[leftIndex].AssetId.Value.CompareTo(references[rightIndex].AssetId.Value);

        if (comparison == 0)
        {
            comparison = references[leftIndex].SchemaId.CompareTo(references[rightIndex].SchemaId);
        }

        if (comparison == 0)
        {
            comparison = references[leftIndex].SchemaMajor.CompareTo(references[rightIndex].SchemaMajor);
        }

        return (comparison != 0) ? comparison : leftIndex.CompareTo(rightIndex);
    }

    private static DataSchema FindAssetReferenceSchema()
    {
        foreach (var schema in NativeAssetSchemaCatalogue.Schemas)
        {
            if (StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, "native/asset-reference"))
            {
                return schema;
            }
        }

        throw new InvalidOperationException("The compiled native-asset-reference schema is missing.");
    }

    private static void HeapSortIndices(scoped Span<int> indices)
    {
        for (var start = indices.Length / 2; start > 0; start--)
        {
            SiftDownIndices(indices, start - 1, indices.Length);
        }

        for (var end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftDownIndices(indices, 0, end);
        }
    }

    private static void HeapSortReferenceIndices(
        scoped ReadOnlySpan<AssetReference> references,
        scoped Span<int> indices)
    {
        for (var start = indices.Length / 2; start > 0; start--)
        {
            SiftDownReferenceIndices(references, indices, start - 1, indices.Length);
        }

        for (var end = indices.Length - 1; end > 0; end--)
        {
            (indices[0], indices[end]) = (indices[end], indices[0]);
            SiftDownReferenceIndices(references, indices, 0, end);
        }
    }

    private static bool MatchesField(EbinFieldReadView field, ContentFieldDescriptor descriptor)
        => (field.Tag == descriptor.Tag)
        && (field.Kind == descriptor.WireKind)
        && (field.IsRequired == descriptor.IsRequired)
        && (field.Count >= descriptor.MinimumCount)
        && (field.Count <= descriptor.MaximumCount);

    private static AssetReference ReadReference(scoped EbinRecordReadView record)
    {
        if (record.FieldCount != s_assetReferenceSchema.Fields.Length)
        {
            throw new InvalidDataException("The native asset-reference record has the wrong field count.");
        }

        var assetIdField = record.GetField(0);
        var schemaIdField = record.GetField(1);
        var schemaMajorField = record.GetField(2);

        if (!MatchesField(assetIdField, s_assetReferenceSchema.Fields[0])
            || !MatchesField(schemaIdField, s_assetReferenceSchema.Fields[1])
            || !MatchesField(schemaMajorField, s_assetReferenceSchema.Fields[2])
            || (assetIdField.Payload.Length != sizeof(uint))
            || (schemaIdField.Payload.Length != 16)
            || (schemaMajorField.Payload.Length != sizeof(uint)))
        {
            throw new InvalidDataException("The native asset-reference record has an invalid field.");
        }

        var assetIdValue = BinaryPrimitives.ReadUInt32LittleEndian(assetIdField.Payload);
        var schemaId = new Guid(schemaIdField.Payload, bigEndian: true);
        var schemaMajor = BinaryPrimitives.ReadUInt32LittleEndian(schemaMajorField.Payload);

        if ((assetIdValue is 0 or > int.MaxValue) || (schemaId == Guid.Empty) || (schemaMajor is 0 or > int.MaxValue))
        {
            throw new InvalidDataException("The native asset-reference target identity is invalid.");
        }

        return new AssetReference(new AssetId(checked((int)assetIdValue)), schemaId, checked((int)schemaMajor));
    }

    private static void SiftDownIndices(scoped Span<int> indices, int root, int length)
    {
        while (root < (length / 2))
        {
            var child = checked((root * 2) + 1);
            var selected = root;

            if (indices[selected] < indices[child])
            {
                selected = child;
            }

            var rightChild = child + 1;

            if ((rightChild < length) && (indices[selected] < indices[rightChild]))
            {
                selected = rightChild;
            }

            if (selected == root)
            {
                return;
            }

            (indices[root], indices[selected]) = (indices[selected], indices[root]);
            root = selected;
        }
    }

    private static void SiftDownReferenceIndices(
        scoped ReadOnlySpan<AssetReference> references,
        scoped Span<int> indices,
        int root,
        int length)
    {
        while (root < (length / 2))
        {
            var child = checked((root * 2) + 1);
            var selected = root;

            if (CompareReferenceIndices(references, indices[selected], indices[child]) < 0)
            {
                selected = child;
            }

            var rightChild = child + 1;

            if ((rightChild < length)
                && (CompareReferenceIndices(references, indices[selected], indices[rightChild]) < 0))
            {
                selected = rightChild;
            }

            if (selected == root)
            {
                return;
            }

            (indices[root], indices[selected]) = (indices[selected], indices[root]);
            root = selected;
        }
    }

    private static void VisitNested(
        scoped EbinRecordReadView nestedRecord,
        ContentFieldDescriptor field,
        CompiledContentSchemaRegistry schemas,
        List<AssetReference>? references,
        HashSet<AssetReference>? seen,
        scoped Span<uint> destination,
        bool copyAssetIds,
        scoped Span<AssetReference> referenceDestination,
        bool copyAssetReferences,
        ref int referenceCount,
        int depth)
    {
        var header = nestedRecord.Header;

        if ((header.SchemaId == Guid.Empty) || (header.Major <= 0) || (header.Minor < 0))
        {
            throw new InvalidDataException("A nested content record has an invalid schema header.");
        }

        var reference = new ContentSchemaReference(
            header.SchemaId,
            header.Major,
            header.Minor,
            ContentSchemaScope.Content);

        if (!schemas.TryGetSchema(reference, out var nestedSchema))
        {
            throw new NotSupportedException("A nested content record uses an unregistered schema tuple.");
        }

        var admitted = false;

        foreach (var nestedSchemaReference in field.NestedSchemas)
        {
            if (nestedSchemaReference == reference)
            {
                admitted = true;
                break;
            }
        }

        if (!admitted && (field.NestedRoleMask != 0))
        {
            admitted = (field.NestedRoleMask & (1UL << ((int)nestedSchema.Role - 1))) != 0;
        }

        if (!admitted)
        {
            throw new InvalidDataException("A nested content record is outside the compiled field selection.");
        }

        if ((field.Reference == ContentReferenceKind.Asset)
            && (reference != s_assetReferenceSchema.Schema))
        {
            throw new InvalidDataException("Asset reference annotations require the native asset-reference wrapper.");
        }

        VisitRecord(
            nestedRecord,
            nestedSchema,
            schemas,
            references,
            seen,
            destination,
            copyAssetIds,
            referenceDestination,
            copyAssetReferences,
            ref referenceCount,
            depth + 1);
    }

    private static void VisitRecord(
        scoped EbinRecordReadView record,
        DataSchema schema,
        CompiledContentSchemaRegistry schemas,
        List<AssetReference>? references,
        HashSet<AssetReference>? seen,
        scoped Span<uint> destination,
        bool copyAssetIds,
        scoped Span<AssetReference> referenceDestination,
        bool copyAssetReferences,
        ref int referenceCount,
        int depth)
    {
        if (depth >= MaximumDepth)
        {
            throw new InvalidDataException("The content record nesting exceeds the admitted depth.");
        }

        var header = record.Header;

        if ((header.SchemaId != schema.Schema.SchemaId)
            || (header.Major != schema.Schema.Major)
            || (header.Minor != schema.Schema.Minor))
        {
            throw new InvalidDataException("A content record does not match its compiled schema tuple.");
        }

        if (schema.Schema == s_assetReferenceSchema.Schema)
        {
            AddReference(
                ReadReference(record),
                references,
                seen,
                destination,
                copyAssetIds,
                referenceDestination,
                copyAssetReferences,
                ref referenceCount);
            return;
        }

        var fieldIndex = 0;

        foreach (var descriptor in schema.Fields)
        {
            if (fieldIndex >= record.FieldCount)
            {
                if (descriptor.IsRequired)
                {
                    throw new InvalidDataException($"Required field {descriptor.Tag} is missing.");
                }

                continue;
            }

            var field = record.GetField(fieldIndex);

            if (field.Tag > descriptor.Tag)
            {
                if (descriptor.IsRequired)
                {
                    throw new InvalidDataException($"Required field {descriptor.Tag} is missing.");
                }

                continue;
            }

            if ((field.Tag != descriptor.Tag) || !MatchesField(field, descriptor))
            {
                throw new InvalidDataException($"Field {field.Tag} does not match its compiled schema descriptor.");
            }

            if (field.Kind == 22)
            {
                VisitNested(
                    record.GetRecord(field.Tag),
                    descriptor,
                    schemas,
                    references,
                    seen,
                    destination,
                    copyAssetIds,
                    referenceDestination,
                    copyAssetReferences,
                    ref referenceCount,
                    depth);
            }
            else if (field.Kind == 24)
            {
                var elementCount = checked((int)field.Count);

                for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
                {
                    VisitNested(
                        record.GetRecordArrayElement(field.Tag, elementIndex),
                        descriptor,
                        schemas,
                        references,
                        seen,
                        destination,
                        copyAssetIds,
                        referenceDestination,
                        copyAssetReferences,
                        ref referenceCount,
                        depth);
                }
            }
            else if (descriptor.Reference == ContentReferenceKind.Asset)
            {
                throw new InvalidDataException("Asset references require the native asset-reference wrapper.");
            }

            fieldIndex++;
        }

        if (fieldIndex != record.FieldCount)
        {
            throw new NotSupportedException("The content record contains an unregistered field tag.");
        }
    }
}
