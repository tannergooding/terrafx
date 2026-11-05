// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using TerraFX.Assets;

namespace TerraFX.Entities;

internal static class WorldPersistenceAssetIdSetWriter
{
    internal static int CopySortedUniqueAssetIds(
        CompiledContentSchemaRegistry schemas,
        scoped ReadOnlySpan<byte> ecsRecord,
        scoped ReadOnlySpan<byte> scenesRecord,
        EbinReadLimits limits,
        scoped Span<uint> destination)
    {
        if (MemoryMarshal.AsBytes(destination).Overlaps(ecsRecord)
            || MemoryMarshal.AsBytes(destination).Overlaps(scenesRecord))
        {
            throw new ArgumentException("The AssetId workspace must not overlap its source records.", nameof(destination));
        }

        var referenceCount = MeasureReferenceCount(schemas, ecsRecord, scenesRecord, limits);

        if (destination.Length != referenceCount)
        {
            throw new ArgumentException("The AssetId workspace must match the exact live reference count.", nameof(destination));
        }

        var action = new AssetIdCopyAction(schemas, destination);
        ReadRecord(ecsRecord, limits, ref action);
        ReadRecord(scenesRecord, limits, ref action);

        if (action.Count != referenceCount)
        {
            throw new InvalidOperationException("The live AssetId references changed during capture.");
        }

        destination.Sort();
        var uniqueCount = 0;

        for (var index = 0; index < destination.Length; index++)
        {
            var assetId = destination[index];

            if ((uniqueCount == 0) || (destination[uniqueCount - 1] != assetId))
            {
                destination[uniqueCount++] = assetId;
            }
        }

        var maximumArrayElements = Math.Min(limits.MaximumArrayElements, limits.MaximumAggregateArrayElements);

        if (uniqueCount > maximumArrayElements)
        {
            throw new InvalidDataException("The live AssetId set exceeds the admitted array count.");
        }

        return uniqueCount;
    }

    internal static int CopySortedUniqueAssetReferences(
        CompiledContentSchemaRegistry schemas,
        scoped ReadOnlySpan<byte> ecsRecord,
        scoped ReadOnlySpan<byte> scenesRecord,
        EbinReadLimits limits,
        scoped Span<AssetReference> destination)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(limits);

        if (MemoryMarshal.AsBytes(destination).Overlaps(ecsRecord)
            || MemoryMarshal.AsBytes(destination).Overlaps(scenesRecord))
        {
            throw new ArgumentException("The AssetReference workspace must not overlap its source records.", nameof(destination));
        }

        var referenceCount = MeasureReferenceCount(schemas, ecsRecord, scenesRecord, limits);

        if (destination.Length != referenceCount)
        {
            throw new ArgumentException("The AssetReference workspace must match the exact live reference count.", nameof(destination));
        }

        var action = new AssetReferenceCopyAction(schemas, destination);
        ReadRecord(ecsRecord, limits, ref action);
        ReadRecord(scenesRecord, limits, ref action);

        if (action.Count != referenceCount)
        {
            throw new InvalidOperationException("The live AssetReferences changed during capture.");
        }

        destination.Sort(AssetReferenceComparer.Instance);
        var uniqueCount = 0;

        for (var index = 0; index < destination.Length; index++)
        {
            var reference = destination[index];

            if ((uniqueCount == 0) || (destination[uniqueCount - 1] != reference))
            {
                destination[uniqueCount++] = reference;
            }
        }

        return uniqueCount;
    }

    internal static int MeasureReferenceCount(
        CompiledContentSchemaRegistry schemas,
        scoped ReadOnlySpan<byte> ecsRecord,
        scoped ReadOnlySpan<byte> scenesRecord,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(limits);
        var action = new AssetIdCountAction(schemas);
        ReadRecord(ecsRecord, limits, ref action);
        ReadRecord(scenesRecord, limits, ref action);
        return action.Count;
    }

    private static DataSchema GetSchema(EbinRecordReadView record, CompiledContentSchemaRegistry schemas)
    {
        var header = record.Header;

        if ((header.SchemaId == Guid.Empty) || (header.Major == 0))
        {
            throw new InvalidDataException("A persistence root has an invalid schema identity.");
        }

        var schemaReference = new ContentSchemaReference(
            header.SchemaId,
            checked((int)header.Major),
            checked((int)header.Minor),
            ContentSchemaScope.Content);

        if (!schemas.TryGetSchema(schemaReference, out var schema))
        {
            throw new NotSupportedException(
                $"A persistence root uses unregistered schema tuple {header.SchemaId:N}/{header.Major}.{header.Minor}.");
        }

        return schema;
    }

    private static void ReadRecord<TAction>(
        scoped ReadOnlySpan<byte> record,
        EbinReadLimits limits,
        scoped ref TAction action)
        where TAction : struct, IAction<EbinRecordReadView>, allows ref struct
    {
        var status = EbinStructuralReader.Read(
            record,
            limits,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != record.Length))
        {
            throw new InvalidDataException(
                $"A persistence record could not be scanned for AssetIds: {diagnostic?.Category ?? status.ToString()}.");
        }
    }

    private ref struct AssetIdCopyAction : IAction<EbinRecordReadView>
    {
        private readonly CompiledContentSchemaRegistry _schemas;
        private readonly Span<uint> _destination;

        internal AssetIdCopyAction(CompiledContentSchemaRegistry schemas, Span<uint> destination)
        {
            _schemas = schemas;
            _destination = destination;
            Count = 0;
        }

        internal int Count { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var schema = GetSchema(record, _schemas);
            var copiedCount = AssetReferenceScanner.CopyAssetIds(
                record,
                schema,
                _schemas,
                _destination[Count..]);
            Count = checked(Count + copiedCount);
        }
    }

    private ref struct AssetIdCountAction : IAction<EbinRecordReadView>
    {
        private readonly CompiledContentSchemaRegistry _schemas;

        internal AssetIdCountAction(CompiledContentSchemaRegistry schemas)
        {
            _schemas = schemas;
            Count = 0;
        }

        internal int Count { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var schema = GetSchema(record, _schemas);
            Count = checked(Count + AssetReferenceScanner.CountAssetIds(record, schema, _schemas));
        }
    }

    private sealed class AssetReferenceComparer : IComparer<AssetReference>
    {
        internal static AssetReferenceComparer Instance { get; } = new();

        public int Compare(AssetReference left, AssetReference right)
        {
            var assetIdComparison = left.AssetId.Value.CompareTo(right.AssetId.Value);

            if (assetIdComparison != 0)
            {
                return assetIdComparison;
            }

            var schemaIdComparison = left.SchemaId.CompareTo(right.SchemaId);
            return (schemaIdComparison != 0)
                ? schemaIdComparison
                : left.SchemaMajor.CompareTo(right.SchemaMajor);
        }
    }

    private ref struct AssetReferenceCopyAction : IAction<EbinRecordReadView>
    {
        private readonly CompiledContentSchemaRegistry _schemas;
        private readonly Span<AssetReference> _destination;

        internal AssetReferenceCopyAction(
            CompiledContentSchemaRegistry schemas,
            Span<AssetReference> destination)
        {
            _schemas = schemas;
            _destination = destination;
            Count = 0;
        }

        internal int Count { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var schema = GetSchema(record, _schemas);
            var referenceCount = AssetReferenceScanner.CountAssetIds(record, schema, _schemas);
            var copiedCount = AssetReferenceScanner.CopyAssetReferences(
                record,
                schema,
                _schemas,
                _destination.Slice(Count, referenceCount));

            if (copiedCount != referenceCount)
            {
                throw new InvalidOperationException("The AssetReference count changed during persistence capture.");
            }

            Count = checked(Count + copiedCount);
        }
    }
}
