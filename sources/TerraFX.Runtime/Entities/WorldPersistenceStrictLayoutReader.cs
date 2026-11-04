// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceStrictLayoutReader
{
    private const int ArchetypeKeyDigestBytes = 32;
    private const ushort ArrayKind = 24;
    private const ushort BlobKind = 13;
    private const ushort FixedArrayKind = 23;
    private const ushort GuidKind = 11;
    private const ushort Int64Kind = 8;
    private const int MaximumArchetypeCount = 4096;
    private const int MaximumChunkCapacity = 256;
    private const int MaximumComponentCount = 256;
    private const int MaximumEntitySlots = 2_097_152;
    private const int MaximumLogicalRowBytes = 32_768;
    private const int MinimumLogicalRowBytes = 12;
    private const int RootFieldCount = 5;
    private const ushort UInt16Kind = 2;
    private const ushort UInt32Kind = 3;
    private const ushort UInt8Kind = 1;

    private static readonly Guid s_archetypeSchemaId = new ContentSchemaIdentityAssignment("state/archetype").SchemaId;
    private static readonly Guid s_chunkSchemaId = new ContentSchemaIdentityAssignment("state/chunk").SchemaId;
    private static readonly Guid s_componentLayoutSchemaId = new ContentSchemaIdentityAssignment("state/component-layout").SchemaId;
    private static readonly Guid s_componentRegistrationSchemaId = new ContentSchemaIdentityAssignment("state/component-registration").SchemaId;
    private static readonly Guid s_registrationSchemaId = new ContentSchemaIdentityAssignment("state/registration").SchemaId;
    private static readonly Guid s_schemaReferenceSchemaId = new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId;
    private static readonly Guid s_slotSchemaId = new ContentSchemaIdentityAssignment("state/slot").SchemaId;
    private static readonly Guid s_strictLayoutSchemaId = new ContentSchemaIdentityAssignment("state/strict-layout").SchemaId;

    internal static WorldPersistenceStrictLayoutReadWorkspaceRequirements Measure(
        ReadOnlyMemory<byte> recordBytes,
        EbinReadLimits limits,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ValidateCanonicalRecord(recordBytes.Span, limits, parameterName);
        return Measure(new EbinRecordReadView(recordBytes.Span), limits, parameterName);
    }

    internal static void Validate(
        ReadOnlyMemory<byte> strictLayoutRecord,
        ReadOnlyMemory<byte> ecsRecord,
        ReadOnlyMemory<byte> registrationRecord,
        EbinReadLimits limits,
        WorldPersistenceConfiguration configuration,
        WorldRegistration? targetRegistration,
        WorldPersistenceLiveIdentitySet liveIdentities,
        WorldPersistenceStrictLayoutReadWorkspaceRequirements workspace,
        scoped ref WorldPersistenceStrictLayoutReadBuffers buffers,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(liveIdentities);
        ValidateCanonicalRecord(strictLayoutRecord.Span, limits, parameterName);
        ValidateCanonicalRecord(ecsRecord.Span, limits, parameterName);
        ValidateCanonicalRecord(registrationRecord.Span, limits, parameterName);

        var record = new EbinRecordReadView(strictLayoutRecord.Span);
        var measuredWorkspace = Measure(record, limits, parameterName);

        if (!workspace.Matches(measuredWorkspace))
        {
            throw new ArgumentException("The StrictLocal read workspace does not match the saved layout.", parameterName);
        }

        ValidateBuffers(workspace, ref buffers, strictLayoutRecord.Span, ecsRecord.Span, registrationRecord.Span, parameterName);
        var registration = new EbinRecordReadView(registrationRecord.Span);
        var ecs = new EbinRecordReadView(ecsRecord.Span);
        ValidateRegistration(record, registration, targetRegistration, parameterName);
        ValidateArchetypes(record, registration, targetRegistration, ref buffers, parameterName);
        ValidateSlots(record, configuration, liveIdentities, ref buffers, parameterName);
        ValidateEntityArchetypes(ecs, record, liveIdentities, parameterName);
        ValidateChunkRows(record, liveIdentities, parameterName);
    }

    private static int CompareEntitySchemaSetToKey(
        EbinRecordReadView entity,
        EbinRecordReadView archetype,
        string parameterName)
    {
        var componentCount = GetCount(entity, 2, ArrayKind, 256, parameterName);
        var tagCount = GetCount(entity, 3, ArrayKind, 256, parameterName);
        var transientCount = GetCount(entity, 4, ArrayKind, 256, parameterName);
        var key = GetRequiredField(archetype, 1, FixedArrayKind, expectedCount: null, parameterName);
        var keyCount = checked((int)key.Count);
        var entitySchemaCount = checked(componentCount + tagCount + transientCount);
        var compareCount = Math.Min(entitySchemaCount, keyCount);
        var componentIndex = 0;
        var tagIndex = 0;
        var transientIndex = 0;

        for (var index = 0; index < compareCount; index++)
        {
            var hasComponent = componentIndex < componentCount;
            var hasTag = tagIndex < tagCount;
            var hasTransient = transientIndex < transientCount;
            var componentId = hasComponent ? ReadEntitySchemaId(entity, 2, componentIndex, isTransient: false, parameterName) : Guid.Empty;
            var tagId = hasTag ? ReadEntitySchemaId(entity, 3, tagIndex, isTransient: false, parameterName) : Guid.Empty;
            var transientId = hasTransient ? ReadEntitySchemaId(entity, 4, transientIndex, isTransient: true, parameterName) : Guid.Empty;
            var selected = Guid.Empty;
            var sourceCount = 0;

            if (hasComponent)
            {
                selected = componentId;
                sourceCount = 1;
            }

            if (hasTag)
            {
                var comparison = (sourceCount == 0) ? -1 : FoundationCatalogue.CompareRfc(tagId, selected);

                if (comparison == 0)
                {
                    throw new ArgumentException("A state/entity schema appears in more than one component, tag, or transient array.", parameterName);
                }

                if (comparison < 0)
                {
                    selected = tagId;
                }

                sourceCount++;
            }

            if (hasTransient)
            {
                var comparison = (sourceCount == 0) ? -1 : FoundationCatalogue.CompareRfc(transientId, selected);

                if (comparison == 0)
                {
                    throw new ArgumentException("A state/entity schema appears in more than one component, tag, or transient array.", parameterName);
                }

                if (comparison < 0)
                {
                    selected = transientId;
                }
            }

            var keyId = ReadGuidArrayElement(key, index, parameterName);
            var keyComparison = FoundationCatalogue.CompareRfc(selected, keyId);

            if (keyComparison != 0)
            {
                return keyComparison;
            }

            if (hasComponent && (componentId == selected))
            {
                componentIndex++;
            }
            else if (hasTag && (tagId == selected))
            {
                tagIndex++;
            }
            else
            {
                transientIndex++;
            }
        }

        return entitySchemaCount.CompareTo(keyCount);
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

    private static int CompareSchemaSets(scoped ReadOnlySpan<Guid> left, scoped ReadOnlySpan<Guid> right)
    {
        var count = Math.Min(left.Length, right.Length);

        for (var index = 0; index < count; index++)
        {
            var comparison = FoundationCatalogue.CompareRfc(left[index], right[index]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static int ComputeLogicalRowBytes(
        EbinRecordReadView record,
        EbinRecordReadView registration,
        WorldRegistration? targetRegistration,
        scoped ReadOnlySpan<Guid> key,
        int componentLayoutCount,
        int tagCount,
        string parameterName)
    {
        long logicalRowBytes = MinimumLogicalRowBytes;

        foreach (var schemaId in key)
        {
            var layoutIndex = FindComponentLayoutIndex(record, componentLayoutCount, schemaId, parameterName);

            if (layoutIndex >= 0)
            {
                var layout = record.GetRecordArrayElement(5, layoutIndex);
                logicalRowBytes = checked(logicalRowBytes + ReadUInt32(layout, 2, parameterName));
            }
            else if (!IsRegisteredTag(registration, tagCount, targetRegistration, schemaId, parameterName))
            {
                throw new ArgumentException("A StrictLocal archetype key is not a registered component/tag set.", parameterName);
            }
        }

        if (logicalRowBytes is < MinimumLogicalRowBytes or > MaximumLogicalRowBytes)
        {
            throw new ArgumentException("A StrictLocal archetype row size exceeds its supported range.", parameterName);
        }

        return checked((int)logicalRowBytes);
    }

    private static int FindArchetypeByChunkOrdinal(
        scoped ReadOnlySpan<WorldPersistenceStrictLayoutChunkOrdinalEntry> entries,
        long ordinal,
        string parameterName)
    {
        var low = 0;
        var high = entries.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var entry = entries[middle];
            var comparison = entry.Ordinal.CompareTo(ordinal);

            if (comparison == 0)
            {
                return entry.ArchetypeIndex;
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

        throw new ArgumentException("A live StrictLocal slot references an absent chunk.", parameterName);
    }

    private static int FindComponentLayoutIndex(
        EbinRecordReadView record,
        int componentLayoutCount,
        Guid schemaId,
        string parameterName)
    {
        var low = 0;
        var high = componentLayoutCount - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var componentLayout = record.GetRecordArrayElement(5, middle);
            var schema = ReadSchemaReference(componentLayout.GetRecord(1), parameterName);
            var comparison = FoundationCatalogue.CompareRfc(schema.SchemaId, schemaId);

            if (comparison == 0)
            {
                return middle;
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

        return -1;
    }

    private static ContentSchemaReference GetComponentRegistrationSchema(
        EbinRecordReadView registration,
        int index,
        string parameterName)
    {
        var component = registration.GetRecordArrayElement(1, index);
        ValidateRecordHeader(component, s_componentRegistrationSchemaId, expectedFieldCount: 5, parameterName);
        return ReadSchemaReference(component.GetRecord(1), parameterName);
    }

    private static int GetCount(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        int maximumCount,
        string parameterName)
    {
        var field = GetRequiredField(record, tag, kind, expectedCount: null, parameterName);

        if ((field.Count > checked((ulong)maximumCount)) || (field.Count > int.MaxValue))
        {
            throw new ArgumentException("A StrictLocal layout array exceeds its supported count.", parameterName);
        }

        return checked((int)field.Count);
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong? expectedCount,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || !field.IsRequired
            || (expectedCount.HasValue && (field.Count != expectedCount.Value)))
        {
            throw new ArgumentException("A StrictLocal layout field has an invalid wire shape.", parameterName);
        }

        return field;
    }

    private static int GetSchemaReferenceArrayCount(
        EbinRecordReadView registration,
        uint tag,
        int maximumCount,
        string parameterName)
    {
        var field = GetRequiredField(registration, tag, ArrayKind, expectedCount: null, parameterName);

        if ((field.Count > checked((ulong)maximumCount)) || (field.Count > int.MaxValue))
        {
            throw new ArgumentException("A state/registration schema array exceeds its supported count.", parameterName);
        }

        return checked((int)field.Count);
    }

    private static bool IsOptionalScalar(EbinFieldReadView field, ushort kind, ulong count, int payloadBytes)
        => (field.Kind == kind)
        && !field.IsRequired
        && (field.Count == count)
        && (field.Payload.Length == payloadBytes);
    private static bool IsRegisteredTag(
        EbinRecordReadView registration,
        int tagCount,
        WorldRegistration? targetRegistration,
        Guid schemaId,
        string parameterName)
    {
        var low = 0;
        var high = tagCount - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var schema = (targetRegistration is null)
                ? ReadSchemaReference(registration.GetRecordArrayElement(2, middle), parameterName)
                : targetRegistration.Tags[middle].Schema;
            var comparison = FoundationCatalogue.CompareRfc(schema.SchemaId, schemaId);

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

    private static WorldPersistenceStrictLayoutReadWorkspaceRequirements Measure(
        EbinRecordReadView record,
        EbinReadLimits limits,
        string parameterName)
    {
        ValidateRecordHeader(record, s_strictLayoutSchemaId, expectedFieldCount: RootFieldCount, parameterName);
        var archetypeField = GetRequiredField(record, 1, ArrayKind, expectedCount: null, parameterName);
        var slotField = GetRequiredField(record, 2, ArrayKind, expectedCount: null, parameterName);
        _ = GetRequiredField(record, 3, FixedArrayKind, expectedCount: null, parameterName);
        _ = GetRequiredField(record, 4, Int64Kind, expectedCount: 1, parameterName);
        _ = GetRequiredField(record, 5, ArrayKind, expectedCount: null, parameterName);

        if ((archetypeField.Count > MaximumArchetypeCount)
            || (slotField.Count > MaximumEntitySlots)
            || (archetypeField.Count > int.MaxValue)
            || (slotField.Count > int.MaxValue))
        {
            throw new ArgumentException("The StrictLocal layout exceeds its supported archetype or slot count.", parameterName);
        }

        var archetypeCount = checked((int)archetypeField.Count);
        long chunkCount = 0;

        for (var index = 0; index < archetypeCount; index++)
        {
            var archetype = record.GetRecordArrayElement(1, index);
            ValidateRecordHeader(archetype, s_archetypeSchemaId, expectedFieldCount: 2, parameterName);
            var chunks = GetRequiredField(archetype, 2, ArrayKind, expectedCount: null, parameterName);
            chunkCount = checked(chunkCount + (long)chunks.Count);

            if ((chunkCount > limits.MaximumAggregateArrayElements) || (chunkCount > int.MaxValue))
            {
                throw new ArgumentException("The StrictLocal chunk array exceeds its supported count.", parameterName);
            }
        }

        return new WorldPersistenceStrictLayoutReadWorkspaceRequirements(
            archetypeCount,
            checked((int)slotField.Count),
            checked((int)chunkCount));
    }

    private static Guid ReadEntitySchemaId(
        EbinRecordReadView entity,
        uint fieldTag,
        int index,
        bool isTransient,
        string parameterName)
    {
        var child = entity.GetRecordArrayElement(fieldTag, index);

        if (!isTransient)
        {
            var schemaId = child.Header.SchemaId;

            if (schemaId == Guid.Empty)
            {
                throw new ArgumentException("A state/entity schema identity cannot be empty.", parameterName);
            }

            return schemaId;
        }

        return ReadSchemaReference(child, parameterName).SchemaId;
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, GuidKind, expectedCount: 1, parameterName);

        if (field.Payload.Length != 16)
        {
            throw new ArgumentException("A StrictLocal layout GUID has an invalid payload length.", parameterName);
        }

        var value = new Guid(field.Payload, bigEndian: true);

        if (value == Guid.Empty)
        {
            throw new ArgumentException("A StrictLocal layout GUID cannot be empty.", parameterName);
        }

        return value;
    }

    private static Guid ReadGuidArrayElement(EbinFieldReadView field, int index, string parameterName)
    {
        var offset = checked(32 + (index * 16));

        if ((index < 0) || (offset > field.Payload.Length - 16))
        {
            throw new ArgumentException("A StrictLocal GUID array element is outside its payload.", parameterName);
        }

        var value = new Guid(field.Payload.Slice(offset, 16), bigEndian: true);

        if (value == Guid.Empty)
        {
            throw new ArgumentException("A StrictLocal GUID array cannot contain an empty identity.", parameterName);
        }

        return value;
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("A StrictLocal layout Int64 has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static ContentSchemaReference ReadSchemaReference(EbinRecordReadView record, string parameterName)
    {
        if ((record.Header.SchemaId != s_schemaReferenceSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != 4))
        {
            throw new ArgumentException("A StrictLocal component layout schema reference has the wrong identity or shape.", parameterName);
        }

        var schemaField = GetRequiredField(record, 1, GuidKind, expectedCount: 1, parameterName);
        var majorField = GetRequiredField(record, 2, UInt32Kind, expectedCount: 1, parameterName);
        var minorField = GetRequiredField(record, 3, UInt32Kind, expectedCount: 1, parameterName);
        var scopeField = GetRequiredField(record, 4, UInt8Kind, expectedCount: 1, parameterName);

        if ((schemaField.Payload.Length != 16)
            || (majorField.Payload.Length != sizeof(uint))
            || (minorField.Payload.Length != sizeof(uint))
            || (scopeField.Payload.Length != sizeof(byte)))
        {
            throw new ArgumentException("A StrictLocal schema reference has an invalid field payload.", parameterName);
        }

        var schemaId = new Guid(schemaField.Payload, bigEndian: true);
        var major = BinaryPrimitives.ReadUInt32LittleEndian(majorField.Payload);
        var minor = BinaryPrimitives.ReadUInt32LittleEndian(minorField.Payload);
        var scopeValue = scopeField.Payload[0];

        if ((schemaId == Guid.Empty)
            || (major is 0 or > int.MaxValue)
            || (minor > int.MaxValue)
            || !Enum.IsDefined((ContentSchemaScope)scopeValue))
        {
            throw new ArgumentException("A StrictLocal schema reference contains an invalid tuple.", parameterName);
        }

        return new ContentSchemaReference(
            schemaId,
            checked((int)major),
            checked((int)minor),
            (ContentSchemaScope)scopeValue);
    }

    private static ushort ReadUInt16(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt16Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(ushort))
        {
            throw new ArgumentException("A StrictLocal layout UInt16 has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(field.Payload);
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt32Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new ArgumentException("A StrictLocal layout UInt32 has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt8Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new ArgumentException("A StrictLocal layout UInt8 has an invalid payload length.", parameterName);
        }

        return field.Payload[0];
    }

    private static void ValidateArchetypes(
        EbinRecordReadView record,
        EbinRecordReadView registration,
        WorldRegistration? targetRegistration,
        scoped ref WorldPersistenceStrictLayoutReadBuffers buffers,
        string parameterName)
    {
        var archetypeCount = GetCount(record, 1, ArrayKind, MaximumArchetypeCount, parameterName);
        var componentLayoutCount = GetCount(record, 5, ArrayKind, MaximumComponentCount, parameterName);
        var tagCount = GetSchemaReferenceArrayCount(registration, 2, MaximumComponentCount, parameterName);
        Span<Guid> previousKey = stackalloc Guid[MaximumComponentCount];
        Span<Guid> currentKey = stackalloc Guid[MaximumComponentCount];
        var previousKeyCount = -1;
        var chunkOffset = 0;

        for (var archetypeIndex = 0; archetypeIndex < archetypeCount; archetypeIndex++)
        {
            var archetype = record.GetRecordArrayElement(1, archetypeIndex);
            var key = GetRequiredField(archetype, 1, FixedArrayKind, expectedCount: null, parameterName);
            var keyCount = checked((int)key.Count);

            if ((keyCount > MaximumComponentCount)
                || (key.Payload.Length != checked(32 + (keyCount * 16))))
            {
                throw new ArgumentException("A StrictLocal archetype key has an invalid extent.", parameterName);
            }

            for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                var schemaId = ReadGuidArrayElement(key, keyIndex, parameterName);

                if ((keyIndex > 0) && (FoundationCatalogue.CompareRfc(currentKey[keyIndex - 1], schemaId) >= 0))
                {
                    throw new ArgumentException("A StrictLocal archetype key is not in canonical schema order.", parameterName);
                }

                if ((FindComponentLayoutIndex(record, componentLayoutCount, schemaId, parameterName) < 0)
                    && !IsRegisteredTag(registration, tagCount, targetRegistration, schemaId, parameterName))
                {
                    throw new ArgumentException("A StrictLocal archetype contains an unregistered component or tag.", parameterName);
                }

                currentKey[keyIndex] = schemaId;
            }

            if ((previousKeyCount >= 0) && (CompareSchemaSets(previousKey[..previousKeyCount], currentKey[..keyCount]) >= 0))
            {
                throw new ArgumentException("The StrictLocal archetype array is not in canonical key order.", parameterName);
            }

            currentKey[..keyCount].CopyTo(previousKey);
            previousKeyCount = keyCount;
            WorldPersistenceArchetypeKeyDigest.Compute(
                currentKey[..keyCount],
                buffers.ArchetypeDigestScratch.Slice(archetypeIndex * ArchetypeKeyDigestBytes, ArchetypeKeyDigestBytes));

            var logicalRowBytes = ComputeLogicalRowBytes(
                record,
                registration,
                targetRegistration,
                currentKey[..keyCount],
                componentLayoutCount,
                tagCount,
                parameterName);
            var expectedCapacity = checked((ushort)Math.Min(
                MaximumChunkCapacity,
                MaximumLogicalRowBytes / logicalRowBytes));
            var chunks = GetRequiredField(archetype, 2, ArrayKind, expectedCount: null, parameterName);
            var previousChunkOrdinal = -1L;

            var chunkCount = checked((int)chunks.Count);

            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                var chunk = archetype.GetRecordArrayElement(2, chunkIndex);
                ValidateRecordHeader(chunk, s_chunkSchemaId, expectedFieldCount: 4, parameterName);
                var ordinal = ReadInt64(chunk, 1, parameterName);
                var rows = GetRequiredField(chunk, 2, FixedArrayKind, expectedCount: null, parameterName);
                var rowCount = checked((int)rows.Count);
                var capacity = ReadUInt16(chunk, 3, parameterName);
                var actualLogicalRowBytes = ReadUInt32(chunk, 4, parameterName);

                if ((ordinal < 0)
                    || (ordinal <= previousChunkOrdinal)
                    || (rowCount > MaximumChunkCapacity)
                    || (rows.Payload.Length != checked(32 + (rowCount * 16)))
                    || (capacity != expectedCapacity)
                    || (rowCount > capacity)
                    || (actualLogicalRowBytes != checked((uint)logicalRowBytes)))
                {
                    throw new ArgumentException("A StrictLocal chunk does not match its archetype capacity, row size, or order.", parameterName);
                }

                buffers.ChunkOrdinalScratch[chunkOffset++] = new WorldPersistenceStrictLayoutChunkOrdinalEntry(ordinal, archetypeIndex);
                previousChunkOrdinal = ordinal;
            }
        }

        buffers.ChunkOrdinalScratch.Sort();

        for (var index = 1; index < buffers.ChunkOrdinalScratch.Length; index++)
        {
            if (buffers.ChunkOrdinalScratch[index - 1].Ordinal == buffers.ChunkOrdinalScratch[index].Ordinal)
            {
                throw new ArgumentException("StrictLocal chunk ordinals are not globally unique.", parameterName);
            }
        }

        var nextChunkOrdinal = ReadInt64(record, 4, parameterName);

        if ((nextChunkOrdinal < 0)
            || ((buffers.ChunkOrdinalScratch.Length > 0)
                && (nextChunkOrdinal <= buffers.ChunkOrdinalScratch[^1].Ordinal)))
        {
            throw new ArgumentException("The StrictLocal next-chunk cursor is not greater than every allocated chunk.", parameterName);
        }
    }

    private static void ValidateBuffers(
        WorldPersistenceStrictLayoutReadWorkspaceRequirements workspace,
        scoped ref WorldPersistenceStrictLayoutReadBuffers buffers,
        scoped ReadOnlySpan<byte> strictLayoutRecord,
        scoped ReadOnlySpan<byte> ecsRecord,
        scoped ReadOnlySpan<byte> registrationRecord,
        string parameterName)
    {
        if (!buffers.Matches(workspace))
        {
            throw new ArgumentException("StrictLocal read scratch must match the measured workspace exactly.", parameterName);
        }

        if (buffers.ArchetypeDigestScratch.Overlaps(strictLayoutRecord)
            || buffers.ArchetypeDigestScratch.Overlaps(ecsRecord)
            || buffers.ArchetypeDigestScratch.Overlaps(registrationRecord)
            || buffers.FreeSlotBitset.Overlaps(strictLayoutRecord)
            || buffers.FreeSlotBitset.Overlaps(ecsRecord)
            || buffers.FreeSlotBitset.Overlaps(registrationRecord)
            || System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffers.ChunkOrdinalScratch).Overlaps(strictLayoutRecord)
            || System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffers.ChunkOrdinalScratch).Overlaps(ecsRecord)
            || System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffers.ChunkOrdinalScratch).Overlaps(registrationRecord)
            || buffers.ArchetypeDigestScratch.Overlaps(buffers.FreeSlotBitset)
            || buffers.ArchetypeDigestScratch.Overlaps(System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffers.ChunkOrdinalScratch))
            || buffers.FreeSlotBitset.Overlaps(System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffers.ChunkOrdinalScratch)))
        {
            throw new ArgumentException("StrictLocal read scratch must not overlap any source or sibling buffer.", parameterName);
        }
    }

    private static void ValidateCanonicalRecord(scoped ReadOnlySpan<byte> record, EbinReadLimits limits, string parameterName)
    {
        var status = EbinStructuralReader.Validate(record, limits, EbinReadPolicy.Canonical, out var bytesConsumed, out _);

        if ((status is not EbinReadStatus.Complete) || (bytesConsumed != record.Length))
        {
            throw new ArgumentException("A StrictLocal layout source is not a complete canonical EBIN record.", parameterName);
        }
    }

    private static void ValidateChunkRows(
        EbinRecordReadView record,
        WorldPersistenceLiveIdentitySet liveIdentities,
        string parameterName)
    {
        var archetypeCount = GetCount(record, 1, ArrayKind, MaximumArchetypeCount, parameterName);

        for (var archetypeIndex = 0; archetypeIndex < archetypeCount; archetypeIndex++)
        {
            var archetype = record.GetRecordArrayElement(1, archetypeIndex);
            var chunks = GetRequiredField(archetype, 2, ArrayKind, expectedCount: null, parameterName);
            var chunkCount = checked((int)chunks.Count);

            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                var chunk = archetype.GetRecordArrayElement(2, chunkIndex);
                var ordinal = ReadInt64(chunk, 1, parameterName);
                var rows = GetRequiredField(chunk, 2, FixedArrayKind, expectedCount: null, parameterName);
                var rowCount = checked((int)rows.Count);

                for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
                {
                    var entityId = ReadGuidArrayElement(rows, rowIndex, parameterName);
                    liveIdentities.MatchStrictLayoutRow(entityId, archetypeIndex, ordinal, rowIndex, parameterName);
                }
            }
        }
    }

    private static void ValidateEntityArchetypes(
        EbinRecordReadView ecs,
        EbinRecordReadView strictLayout,
        WorldPersistenceLiveIdentitySet liveIdentities,
        string parameterName)
    {
        var entities = GetRequiredField(ecs, 1, ArrayKind, expectedCount: null, parameterName);
        var archetypeCount = GetCount(strictLayout, 1, ArrayKind, MaximumArchetypeCount, parameterName);
        var entityCount = checked((int)entities.Count);

        for (var index = 0; index < entityCount; index++)
        {
            var entity = ecs.GetRecordArrayElement(1, index);
            var idField = GetRequiredField(entity, 1, GuidKind, expectedCount: 1, parameterName);
            var entityId = ReadGuid(entity, 1, parameterName);
            var low = 0;
            var high = archetypeCount - 1;
            var matchedArchetype = -1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var archetype = strictLayout.GetRecordArrayElement(1, middle);
                var comparison = CompareEntitySchemaSetToKey(entity, archetype, parameterName);

                if (comparison == 0)
                {
                    matchedArchetype = middle;
                    break;
                }

                if (comparison < 0)
                {
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }

            if ((matchedArchetype < 0) || (idField.Payload.Length != 16))
            {
                throw new ArgumentException("A state/entity schema set has no exact StrictLocal archetype.", parameterName);
            }

            liveIdentities.MatchStrictLayoutEntityArchetype(entityId, matchedArchetype, parameterName);
        }
    }

    private static void ValidateRecordHeader(
        EbinRecordReadView record,
        Guid schemaId,
        int expectedFieldCount,
        string parameterName)
    {
        if ((record.Header.SchemaId != schemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != expectedFieldCount))
        {
            throw new ArgumentException("A StrictLocal layout child has the wrong schema identity, version, or field count.", parameterName);
        }
    }

    private static void ValidateRegistration(
        EbinRecordReadView strictLayout,
        EbinRecordReadView registration,
        WorldRegistration? targetRegistration,
        string parameterName)
    {
        ValidateRecordHeader(registration, s_registrationSchemaId, expectedFieldCount: 4, parameterName);
        var componentCount = GetCount(strictLayout, 5, ArrayKind, MaximumComponentCount, parameterName);
        var registrationComponentCount = GetCount(registration, 1, ArrayKind, MaximumComponentCount, parameterName);
        var registrationTagCount = GetCount(registration, 2, ArrayKind, MaximumComponentCount, parameterName);

        if ((componentCount != registrationComponentCount)
            || (targetRegistration is not null
                && ((componentCount != targetRegistration.Components.Length)
                    || (registrationTagCount != targetRegistration.Tags.Length))))
        {
            throw new ArgumentException("The StrictLocal component-layout table does not match the saved registration.", parameterName);
        }

        var previousComponentSchema = default(ContentSchemaReference);

        for (var index = 0; index < componentCount; index++)
        {
            var layout = strictLayout.GetRecordArrayElement(5, index);
            ValidateRecordHeader(layout, s_componentLayoutSchemaId, expectedFieldCount: 3, parameterName);
            var schema = ReadSchemaReference(layout.GetRecord(1), parameterName);
            var logicalBytes = ReadUInt32(layout, 2, parameterName);
            var layoutId = ReadGuid(layout, 3, parameterName);
            var savedSchema = GetComponentRegistrationSchema(registration, index, parameterName);

            if ((schema != savedSchema)
                || (schema.Scope != ContentSchemaScope.Content)
                || (logicalBytes is 0 or > 32_756)
                || ((index > 0)
                    && ((previousComponentSchema.SchemaId == schema.SchemaId)
                        || (CompareSchemaReferences(previousComponentSchema, schema) >= 0))))
            {
                throw new ArgumentException("The StrictLocal component-layout row does not match its saved component registration.", parameterName);
            }

            if (targetRegistration is not null)
            {
                var target = targetRegistration.Components[index];

                if ((schema != target.SchemaReference)
                    || (logicalBytes != checked((uint)target.Layout.LogicalBytes))
                    || (layoutId != target.Layout.LayoutId))
                {
                    throw new ArgumentException("The StrictLocal component layout does not match the target WorldRegistration.", parameterName);
                }
            }

            previousComponentSchema = schema;
        }

        var previousTagSchema = default(ContentSchemaReference);

        for (var index = 0; index < registrationTagCount; index++)
        {
            var schema = (targetRegistration is null)
                ? ReadSchemaReference(registration.GetRecordArrayElement(2, index), parameterName)
                : targetRegistration.Tags[index].Schema;

            if ((schema.Scope != ContentSchemaScope.Content)
                || ((index > 0)
                    && ((previousTagSchema.SchemaId == schema.SchemaId)
                        || (CompareSchemaReferences(previousTagSchema, schema) >= 0))))
            {
                throw new ArgumentException("The saved tag-registration array is not in canonical schema order.", parameterName);
            }

            if (FindComponentLayoutIndex(strictLayout, componentCount, schema.SchemaId, parameterName) >= 0)
            {
                throw new ArgumentException("A StrictLocal schema identity is registered as both a component and a tag.", parameterName);
            }

            previousTagSchema = schema;
        }
    }

    private static void ValidateSlotHeader(EbinRecordReadView slot, string parameterName)
    {
        if ((slot.Header.SchemaId != s_slotSchemaId)
            || (slot.Header.Major != 1)
            || (slot.Header.Minor != 0)
            || (slot.FieldCount is not 3 and not 7))
        {
            throw new ArgumentException("A StrictLocal slot has the wrong schema identity, version, or field count.", parameterName);
        }
    }

    private static void ValidateSlots(
        EbinRecordReadView record,
        WorldPersistenceConfiguration configuration,
        WorldPersistenceLiveIdentitySet liveIdentities,
        scoped ref WorldPersistenceStrictLayoutReadBuffers buffers,
        string parameterName)
    {
        var slotCount = GetCount(record, 2, ArrayKind, MaximumEntitySlots, parameterName);
        var freeSlots = GetRequiredField(record, 3, FixedArrayKind, expectedCount: null, parameterName);

        if ((slotCount > configuration.MaximumEntitySlots)
            || (freeSlots.Count > checked((ulong)slotCount))
            || (freeSlots.Payload.Length != checked(32 + (checked((int)freeSlots.Count) * sizeof(uint)))))
        {
            throw new ArgumentException("The StrictLocal slot or free-list count exceeds the saved configuration.", parameterName);
        }

        buffers.FreeSlotBitset.Clear();
        var freeSlotCount = 0;

        for (var index = 0; index < slotCount; index++)
        {
            var slot = record.GetRecordArrayElement(2, index);
            ValidateSlotHeader(slot, parameterName);
            var slotIndex = ReadUInt32(slot, 1, parameterName);
            var generation = ReadUInt32(slot, 2, parameterName);
            var status = ReadUInt8(slot, 3, parameterName);

            if ((slotIndex != checked((uint)(index + 1)))
                || (generation is 0)
                || (status > 2))
            {
                throw new ArgumentException("The StrictLocal slot sequence, generation, or status is invalid.", parameterName);
            }

            var hasEntity = slot.TryGetField(4, out var entityField);
            var hasDigest = slot.TryGetField(5, out var digestField);
            var hasChunk = slot.TryGetField(6, out var chunkField);
            var hasRow = slot.TryGetField(7, out var rowField);

            if (status is 0)
            {
                if ((slot.FieldCount != 7)
                    || !IsOptionalScalar(entityField, GuidKind, count: 1, payloadBytes: 16)
                    || !IsOptionalScalar(digestField, BlobKind, count: ArchetypeKeyDigestBytes, payloadBytes: ArchetypeKeyDigestBytes)
                    || !IsOptionalScalar(chunkField, Int64Kind, count: 1, payloadBytes: sizeof(long))
                    || !IsOptionalScalar(rowField, UInt16Kind, count: 1, payloadBytes: sizeof(ushort)))
                {
                    throw new ArgumentException("A live StrictLocal slot is missing its Entity or physical location fields.", parameterName);
                }

                var entityId = new Guid(entityField.Payload, bigEndian: true);
                var chunkOrdinal = BinaryPrimitives.ReadInt64LittleEndian(chunkField.Payload);
                var row = BinaryPrimitives.ReadUInt16LittleEndian(rowField.Payload);

                if ((entityId == Guid.Empty)
                    || (chunkOrdinal < 0)
                    || (row >= MaximumChunkCapacity))
                {
                    throw new ArgumentException("A live StrictLocal slot has an invalid identity or physical location.", parameterName);
                }

                var archetypeIndex = FindArchetypeByChunkOrdinal(buffers.ChunkOrdinalScratch, chunkOrdinal, parameterName);
                var digest = buffers.ArchetypeDigestScratch.Slice(
                    archetypeIndex * ArchetypeKeyDigestBytes,
                    ArchetypeKeyDigestBytes);

                if (!digest.SequenceEqual(digestField.Payload))
                {
                    throw new ArgumentException("A live StrictLocal slot has an incorrect archetype-key digest.", parameterName);
                }

                liveIdentities.MatchStrictLayoutSlot(entityId, archetypeIndex, chunkOrdinal, row, parameterName);
            }
            else
            {
                if ((slot.FieldCount != 3) || hasEntity || hasDigest || hasChunk || hasRow)
                {
                    throw new ArgumentException("A non-live StrictLocal slot contains live Entity or location fields.", parameterName);
                }

                if ((status is 1) && (generation < 2))
                {
                    throw new ArgumentException("A free StrictLocal slot has a reusable generation below two.", parameterName);
                }

                if ((status is 2) && (generation != uint.MaxValue))
                {
                    throw new ArgumentException("A retired StrictLocal slot does not have the exhausted generation.", parameterName);
                }

                if (status is 1)
                {
                    freeSlotCount++;
                }
            }
        }

        var serializedFreeSlotCount = checked((int)freeSlots.Count);

        if (freeSlotCount != serializedFreeSlotCount)
        {
            throw new ArgumentException("The StrictLocal free-list count does not equal the Free slots.", parameterName);
        }

        for (var index = 0; index < serializedFreeSlotCount; index++)
        {
            var payloadOffset = checked(32 + (checked((int)index) * sizeof(uint)));
            var slotIndex = BinaryPrimitives.ReadUInt32LittleEndian(freeSlots.Payload[payloadOffset..]);

            if ((slotIndex is 0) || (slotIndex > checked((uint)slotCount)))
            {
                throw new ArgumentException("The StrictLocal free list contains an out-of-range slot index.", parameterName);
            }

            var bitIndex = checked((int)slotIndex - 1);
            var mask = checked((byte)(1 << (bitIndex & 7)));
            var bitByte = bitIndex >> 3;

            if ((buffers.FreeSlotBitset[bitByte] & mask) != 0)
            {
                throw new ArgumentException("The StrictLocal free list contains a duplicate slot index.", parameterName);
            }

            buffers.FreeSlotBitset[bitByte] |= mask;
            var slot = record.GetRecordArrayElement(2, checked((int)slotIndex - 1));

            if (ReadUInt8(slot, 3, parameterName) is not 1)
            {
                throw new ArgumentException("The StrictLocal free list contains a non-Free slot.", parameterName);
            }
        }
    }
}
