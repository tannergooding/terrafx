// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using TerraFX;

namespace TerraFX.Scenes;

internal static class SceneEntityReferenceBindingValidator
{
    internal static void Validate(
        ImmutableArray<SceneEntityReferenceBinding> bindings,
        ImmutableArray<SceneExternalSlot> externalSlots,
        int localLeafCount)
    {
        var localBindingCount = 0;

        for (var index = 0; index < bindings.Length; index++)
        {
            var binding = bindings[index];

            if (binding.Source.PlacementPath.IsEmpty)
            {
                localBindingCount++;
            }

            if (binding.Target.Kind == SceneReferenceTargetKind.ExternalSlot)
            {
                var externalSlotId = binding.Target.ExternalSlotId.GetValueOrDefault();

                if (!ContainsExternalSlot(externalSlots, externalSlotId))
                {
                    throw new InvalidDataException("An Entity-reference binding names an undeclared ExternalSlot.");
                }
            }
        }

        if (localBindingCount != localLeafCount)
        {
            throw new InvalidDataException("The Scene or Prefab Entity-reference bindings do not exactly cover its authored Entity leaves.");
        }
    }

    internal static int ValidateComponentRecord(
        EbinRecordReadView record,
        Guid authoredEntityId,
        ContentSchemaReference componentSchema,
        DataSchema metadata,
        ImmutableArray<SceneEntityReferenceBinding> bindings,
        CompiledContentSchemaRegistry schemas,
        bool allowInstanceAbsoluteSources)
    {
        Span<ScenePropertyPathSegment> propertyPath = stackalloc ScenePropertyPathSegment[65];
        var leafCount = 0;
        VisitRecord(
            record,
            metadata,
            propertyPath,
            pathLength: 0,
            authoredEntityId,
            componentSchema,
            bindings,
            schemas,
            allowInstanceAbsoluteSources,
            insideRecordArray: false,
            ref leafCount);
        return leafCount;
    }

    internal static int ValidateEncodedComponentRecord(
        SceneAuthoredComponentRecord component,
        Guid authoredEntityId,
        DataSchema metadata,
        ImmutableArray<SceneEntityReferenceBinding> bindings,
        CompiledContentSchemaRegistry schemas,
        EbinReadLimits limits,
        bool allowInstanceAbsoluteSources)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(limits);
        var action = new EncodedComponentValidationAction(
            authoredEntityId,
            component.Schema,
            metadata,
            bindings,
            schemas,
            allowInstanceAbsoluteSources);
        var encodedRecord = component.EncodedRecord;
        var status = EbinStructuralReader.Read(
            encodedRecord,
            limits,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        if (status is EbinReadStatus.UnsupportedSchema)
        {
            throw new NotSupportedException($"The authored Component uses an unsupported schema: {diagnostic?.Category ?? status.ToString()}.");
        }

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != encodedRecord.Length))
        {
            throw new InvalidDataException($"The authored Component record is invalid: {diagnostic?.Category ?? status.ToString()}.");
        }

        return action.LeafCount;
    }

    private static int CompareBindingToSourceKey(
        SceneEntityReferenceBinding binding,
        SceneEntitySelectorScope scope,
        Guid authoredEntityId,
        ContentSchemaReference componentSchema,
        ReadOnlySpan<ScenePropertyPathSegment> propertyPath)
    {
        var comparison = binding.Source.Scope.CompareTo(scope);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = binding.Source.PlacementPath.Length.CompareTo(0);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = FoundationCatalogue.CompareRfc(binding.Source.AuthoredEntityId, authoredEntityId);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareSchemaReferences(binding.ComponentSchema, componentSchema);
        return (comparison != 0) ? comparison : ComparePropertyPaths(binding.PropertyPath, propertyPath);
    }

    private static int ComparePropertyPaths(
        ImmutableArray<ScenePropertyPathSegment> left,
        ReadOnlySpan<ScenePropertyPathSegment> right)
    {
        var sharedLength = Math.Min(left.Length, right.Length);

        for (var index = 0; index < sharedLength; index++)
        {
            var comparison = left[index].Kind.CompareTo(right[index].Kind);

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left[index].Value.CompareTo(right[index].Value);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

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

    private static bool ContainsExternalSlot(ImmutableArray<SceneExternalSlot> externalSlots, Guid externalSlotId)
    {
        var low = 0;
        var high = externalSlots.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = FoundationCatalogue.CompareRfc(externalSlots[middle].SlotId, externalSlotId);

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

    private static SceneEntityReferenceBinding? FindBinding(
        ImmutableArray<SceneEntityReferenceBinding> bindings,
        SceneEntitySelectorScope scope,
        Guid authoredEntityId,
        ContentSchemaReference componentSchema,
        ReadOnlySpan<ScenePropertyPathSegment> propertyPath)
    {
        var low = 0;
        var high = bindings.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var binding = bindings[middle];
            var comparison = CompareBindingToSourceKey(
                binding,
                scope,
                authoredEntityId,
                componentSchema,
                propertyPath);

            if (comparison == 0)
            {
                return binding;
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

        return null;
    }

    private static Guid ReadEntityReferenceGuid(EbinFieldReadView field, int elementIndex = -1)
    {
        var payload = field.Payload;

        if (elementIndex < 0)
        {
            if ((field.Kind != 11) || (field.Count != 1) || (payload.Length != 16))
            {
                throw new InvalidDataException("An Entity-reference leaf does not contain one GUID.");
            }

            var value = new Guid(payload, bigEndian: true);

            if (value == Guid.Empty)
            {
                throw new InvalidDataException("An Entity-reference leaf cannot contain Guid.Empty.");
            }

            return value;
        }

        if ((field.Kind != 23)
            || (elementIndex >= checked((int)field.Count))
            || (payload.Length != checked(32 + (checked((int)field.Count) * 16)))
            || (BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]) != 11)
            || (BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]) != 16))
        {
            throw new InvalidDataException("An Entity-reference FixedArray does not match its registered GUID element shape.");
        }

        var offset = checked(32 + (elementIndex * 16));
        var element = new Guid(payload.Slice(offset, 16), bigEndian: true);

        if (element == Guid.Empty)
        {
            throw new InvalidDataException("An Entity-reference FixedArray element cannot contain Guid.Empty.");
        }

        return element;
    }

    private static void ValidateEntityFixedArray(EbinFieldReadView field)
    {
        var payload = field.Payload;

        if ((field.Count > int.MaxValue)
            || (payload.Length != checked(32 + (checked((int)field.Count) * 16)))
            || (BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]) != 11)
            || (BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]) != 16))
        {
            throw new InvalidDataException("An Entity-reference FixedArray does not match its registered GUID element shape.");
        }
    }

    private static void ValidateEntityReferenceLeaf(
        Guid value,
        Guid authoredEntityId,
        ContentSchemaReference componentSchema,
        ReadOnlySpan<ScenePropertyPathSegment> propertyPath,
        ImmutableArray<SceneEntityReferenceBinding> bindings,
        bool allowInstanceAbsoluteSources)
    {
        if (propertyPath.IsEmpty || (propertyPath.Length > 64))
        {
            throw new InvalidDataException("An Entity-reference leaf exceeds the supported property-path length.");
        }

        var occurrenceRelativeBinding = FindBinding(
            bindings,
            SceneEntitySelectorScope.OccurrenceRelative,
            authoredEntityId,
            componentSchema,
            propertyPath);
        var instanceAbsoluteBinding = allowInstanceAbsoluteSources
            ? FindBinding(
                bindings,
                SceneEntitySelectorScope.InstanceAbsolute,
                authoredEntityId,
                componentSchema,
                propertyPath)
            : null;

        var binding = occurrenceRelativeBinding ?? instanceAbsoluteBinding
            ?? throw new InvalidDataException("An authored Entity-reference leaf has no source binding.");

        if ((occurrenceRelativeBinding is not null) && (instanceAbsoluteBinding is not null))
        {
            throw new InvalidDataException("An authored Entity-reference leaf has multiple source bindings.");
        }

        var symbolicTarget = binding.Target.Kind switch {
            SceneReferenceTargetKind.Internal => binding.Target.Entity.GetValueOrDefault().AuthoredEntityId,
            SceneReferenceTargetKind.ExternalSlot => binding.Target.ExternalSlotId.GetValueOrDefault(),
            _ => throw new InvalidDataException("An Entity-reference binding has an unknown target kind."),
        };

        if (value != symbolicTarget)
        {
            throw new InvalidDataException("An authored Entity-reference GUID does not match its declared binding target token.");
        }
    }

    private static void ValidateField(
        EbinRecordReadView record,
        ContentFieldDescriptor descriptor,
        out EbinFieldReadView field,
        out bool isPresent)
    {
        if (!record.TryGetField(descriptor.Tag, out field))
        {
            if (descriptor.IsRequired)
            {
                throw new InvalidDataException($"Required component reference field {descriptor.Tag} is absent.");
            }

            isPresent = false;
            return;
        }

        if ((field.Kind != descriptor.WireKind)
            || (field.IsRequired != descriptor.IsRequired)
            || (field.Count < descriptor.MinimumCount)
            || (field.Count > descriptor.MaximumCount))
        {
            throw new InvalidDataException($"Component reference field {descriptor.Tag} does not match its registered descriptor.");
        }

        isPresent = true;
    }

    private static void VisitRecord(
        EbinRecordReadView record,
        DataSchema metadata,
        Span<ScenePropertyPathSegment> propertyPath,
        int pathLength,
        Guid authoredEntityId,
        ContentSchemaReference componentSchema,
        ImmutableArray<SceneEntityReferenceBinding> bindings,
        CompiledContentSchemaRegistry schemas,
        bool allowInstanceAbsoluteSources,
        bool insideRecordArray,
        ref int leafCount)
    {
        for (var fieldIndex = 0; fieldIndex < metadata.Fields.Length; fieldIndex++)
        {
            var descriptor = metadata.Fields[fieldIndex];
            ValidateField(record, descriptor, out var field, out var isPresent);

            if (!isPresent)
            {
                continue;
            }

            if (descriptor.Reference == ContentReferenceKind.Entity)
            {
                if (insideRecordArray)
                {
                    throw new NotSupportedException("The version-one property path cannot address Entity-reference leaves inside RecordArray elements.");
                }

                if (descriptor.WireKind == 11)
                {
                    if (pathLength >= 64)
                    {
                        throw new InvalidDataException("An Entity-reference leaf exceeds the supported property-path length.");
                    }

                    propertyPath[pathLength] = new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, descriptor.Tag);
                    ValidateEntityReferenceLeaf(
                        ReadEntityReferenceGuid(field),
                        authoredEntityId,
                        componentSchema,
                        propertyPath[..(pathLength + 1)],
                        bindings,
                        allowInstanceAbsoluteSources);
                    leafCount++;
                }
                else if ((descriptor.WireKind == 23) && (descriptor.ElementKind == 11))
                {
                    if ((pathLength + 2) > 64)
                    {
                        throw new InvalidDataException("An Entity-reference FixedArray exceeds the supported property-path length.");
                    }

                    ValidateEntityFixedArray(field);
                    propertyPath[pathLength] = new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, descriptor.Tag);

                    for (var elementIndex = 0; elementIndex < checked((int)field.Count); elementIndex++)
                    {
                        propertyPath[pathLength + 1] = new ScenePropertyPathSegment(
                            ScenePropertyPathSegmentKind.FixedArrayIndex,
                            checked((uint)elementIndex));
                        ValidateEntityReferenceLeaf(
                            ReadEntityReferenceGuid(field, elementIndex),
                            authoredEntityId,
                            componentSchema,
                            propertyPath[..(pathLength + 2)],
                            bindings,
                            allowInstanceAbsoluteSources);
                        leafCount++;
                    }
                }
                else
                {
                    throw new InvalidDataException("An Entity-reference descriptor must identify a GUID scalar or FixedArray of GUIDs.");
                }
            }
            else if (descriptor.WireKind == 22)
            {
                if (pathLength >= 65)
                {
                    throw new InvalidDataException("A nested component path exceeds the supported structural depth.");
                }

                propertyPath[pathLength] = new ScenePropertyPathSegment(ScenePropertyPathSegmentKind.FieldTag, descriptor.Tag);
                var nestedRecord = record.GetRecord(descriptor.Tag);
                var nestedMetadata = ScenePrefabOverrideApplier.ResolveNestedSchema(descriptor, nestedRecord, schemas);
                VisitRecord(
                    nestedRecord,
                    nestedMetadata,
                    propertyPath,
                    pathLength + 1,
                    authoredEntityId,
                    componentSchema,
                    bindings,
                    schemas,
                    allowInstanceAbsoluteSources,
                    insideRecordArray,
                    ref leafCount);
            }
            else if (descriptor.WireKind == 24)
            {
                for (var elementIndex = 0; elementIndex < checked((int)field.Count); elementIndex++)
                {
                    var nestedRecord = record.GetRecordArrayElement(descriptor.Tag, elementIndex);
                    var nestedMetadata = ScenePrefabOverrideApplier.ResolveNestedSchema(descriptor, nestedRecord, schemas);
                    VisitRecord(
                        nestedRecord,
                        nestedMetadata,
                        propertyPath,
                        pathLength,
                        authoredEntityId,
                        componentSchema,
                        bindings,
                        schemas,
                        allowInstanceAbsoluteSources,
                        insideRecordArray: true,
                        ref leafCount);
                }
            }
        }
    }

    private struct EncodedComponentValidationAction : IAction<EbinRecordReadView>
    {
        private readonly bool _allowInstanceAbsoluteSources;
        private readonly Guid _authoredEntityId;
        private readonly ImmutableArray<SceneEntityReferenceBinding> _bindings;
        private readonly ContentSchemaReference _componentSchema;
        private readonly DataSchema _metadata;
        private readonly CompiledContentSchemaRegistry _schemas;
        private int _leafCount;

        internal EncodedComponentValidationAction(
            Guid authoredEntityId,
            ContentSchemaReference componentSchema,
            DataSchema metadata,
            ImmutableArray<SceneEntityReferenceBinding> bindings,
            CompiledContentSchemaRegistry schemas,
            bool allowInstanceAbsoluteSources)
        {
            _authoredEntityId = authoredEntityId;
            _componentSchema = componentSchema;
            _metadata = metadata;
            _bindings = bindings;
            _schemas = schemas;
            _allowInstanceAbsoluteSources = allowInstanceAbsoluteSources;
            _leafCount = 0;
        }

        internal readonly int LeafCount => _leafCount;

        public void Invoke(scoped EbinRecordReadView record)
        {
            var header = record.Header;

            if ((header.SchemaId != _componentSchema.SchemaId)
                || (header.Major != _componentSchema.Major)
                || (header.Minor != _componentSchema.Minor))
            {
                throw new InvalidDataException("The authored Component record does not match its selected schema tuple.");
            }

            _leafCount = ValidateComponentRecord(
                record,
                _authoredEntityId,
                _componentSchema,
                _metadata,
                _bindings,
                _schemas,
                _allowInstanceAbsoluteSources);
        }
    }
}
